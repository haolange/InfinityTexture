using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 一套运行中的虚拟纹理：页请求（含页目录与物理池）、间接表、物理页，以及唯一的生产者。
    /// 同一时间只有一套处于 active，渲染特性从这里取数据。
    /// </summary>
    internal unsafe class VirtualTextureSystem : IDisposable
    {
        static readonly int s_ParamsID = Shader.PropertyToID("_VTParams");
        static readonly int s_PoolParamsID = Shader.PropertyToID("_VTPoolParams");
        static readonly int s_LayerCountID = Shader.PropertyToID("_VTLayerCount");
        static readonly int s_FilterParamsID = Shader.PropertyToID("_VTFilterParams");
        internal static readonly ProfilingSampler bakeSampler = new ProfilingSampler("VT Bake Page");

        public static VirtualTextureSystem active { get; private set; }

        public readonly VirtualTextureAsset settings;
        public readonly RequestPipeline requests;
        public readonly IndirectMap indirectMap;
        public readonly PhysicalLayers physical;
        public readonly RoundResult round = new RoundResult();
        public readonly FeedbackReadback readback;
        public bool indirectReady;
        public bool rootReady { get; private set; }
        bool m_RootBaked;

        readonly List<Camera> m_Cameras = new List<Camera>(4);
        readonly List<Camera> m_SeenCameras = new List<Camera>(4);
        int m_NextCamera;
        int m_ConsumedFrame = -1;
        bool m_ReadbackScheduled;

        readonly IPageProducer m_Producer;

        public VirtualTextureSystem(VirtualTextureAsset settings, IPageProducer producer)
        {
            this.settings = settings;
            m_Producer = producer;

            int poolCapacity = settings.poolSize * settings.poolSize;
            requests = new RequestPipeline(settings.pageCount, settings.poolSize, settings.bakesPerRound, settings.queueCapacity, settings.queueTimeout);
            indirectMap = new IndirectMap(settings.pageCount, poolCapacity, settings.indirectShader);
            EPageLayer[] layers = PhysicalLayers.Enabled(settings.layers);
            physical = new PhysicalLayers(settings, layers, settings.format, settings.encodeShader);
            readback = new FeedbackReadback(requests.Schedule);
            RenderPipelineManager.beginContextRendering += BeginContext;
            RenderPipelineManager.endContextRendering += EndContext;

            Shader.SetGlobalVector(s_ParamsID, new Vector4(settings.pageCount, settings.mipCount, settings.pageTexels, (int)settings.pageBorder));
            Shader.SetGlobalVector(s_PoolParamsID, new Vector4(settings.slotTexels, 1.0f / settings.poolTexels, 0, 0));
            Shader.SetGlobalInt(s_LayerCountID, layers.Length);
            Shader.SetGlobalVector(s_FilterParamsID, new Vector4((int)settings.filter, settings.anisoSamples, 0, 0));
            active = this;
            // 原生材质没有反馈 pass；根页先走同一条请求链，避免启动依赖反馈。
            var bootstrap = new NativeArray<uint>(1, Allocator.Temp);
            try
            {
                bootstrap[0] = new PageId(0, 0, settings.mipCount - 1).packed;
                requests.Schedule(bootstrap);
            }
            finally { bootstrap.Dispose(); }
        }

        void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            m_SeenCameras.Clear();
            for (int i = m_Cameras.Count - 1; i >= 0; --i)
            {
                Camera camera = m_Cameras[i];
                if (camera == null || !camera.isActiveAndEnabled || !cameras.Contains(camera)) { RemoveCamera(i); }
            }
        }

        void EndContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            for (int i = m_Cameras.Count - 1; i >= 0; --i)
            {
                if (!m_SeenCameras.Contains(m_Cameras[i])) { RemoveCamera(i); }
            }
        }

        void RemoveCamera(int index)
        {
            m_Cameras.RemoveAt(index);
            if (index < m_NextCamera) { --m_NextCamera; }
            if (m_NextCamera >= m_Cameras.Count) { m_NextCamera = 0; }
        }

        public bool TryTakeRound()
        {
            if (m_ConsumedFrame == Time.frameCount || !requests.TryTakeResult(round)) { return false; }
            m_ConsumedFrame = Time.frameCount;
            return true;
        }

        public bool ReserveReadback(Camera camera)
        {
            if (!m_SeenCameras.Contains(camera)) { m_SeenCameras.Add(camera); }
            if (!m_Cameras.Contains(camera)) { m_Cameras.Add(camera); }
            if (requests.isBusy || readback.inFlight || m_ReadbackScheduled) { return false; }
            if (m_Cameras.Count > 0 && m_Cameras[m_NextCamera] != camera) { return false; }
            m_ReadbackScheduled = true;
            return true;
        }

        public void RequestReadback(CommandBuffer cmd, RenderTexture feedback)
        {
            readback.Request(cmd, feedback);
            m_ReadbackScheduled = false;
            if (m_Cameras.Count > 0) { m_NextCamera = (m_NextCamera + 1) % m_Cameras.Count; }
        }

        /// <summary>设置与当前平台是否能建出这套虚拟纹理；不能时给出原因，调用方保持非 VT 着色。</summary>
        public static bool CanCreate(VirtualTextureAsset settings, out string error)
        {
            if (!settings.Validate(out error)) { return false; }
            if ((long)settings.poolSize * ((long)settings.pageTexels + 2 * (int)settings.pageBorder) > SystemInfo.maxTextureSize || settings.pageCount > SystemInfo.maxTextureSize)
            {
                error = "物理图尺寸超出当前平台上限";
                return false;
            }
            if (!SystemInfo.supportsAsyncGPUReadback || !SystemInfo.IsFormatSupported(GraphicsFormat.R32_UInt, GraphicsFormatUsage.Render))
            {
                error = "当前平台不支持 R32_UInt 反馈绘制和异步回读";
                return false;
            }

            PageFormat format = settings.format;
            foreach (EPageLayer layer in PhysicalLayers.Enabled(settings.layers))
            {
                GraphicsFormat physicalFormat = format.PhysicalFormat(PhysicalLayers.IsColor(layer));
                if (!SystemInfo.IsFormatSupported(physicalFormat, GraphicsFormatUsage.Sample)) { error = $"当前平台不支持 {physicalFormat}"; return false; }
                GraphicsFormat scratchFormat = PageFormat.Of(EPageCompression.None).PhysicalFormat(PhysicalLayers.IsColor(layer));
                if (!SystemInfo.IsFormatSupported(scratchFormat, GraphicsFormatUsage.Render)) { error = $"当前平台不支持 {scratchFormat} 烘焙目标"; return false; }
            }
            if (format.isCompressed)
            {
                if (!SystemInfo.supportsComputeShaders) { error = "当前平台不支持 compute shader，无法压缩物理页"; return false; }
                if (!SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_UInt, GraphicsFormatUsage.LoadStore)) { error = "当前平台不支持压缩块图的随机写入"; return false; }
                if ((SystemInfo.copyTextureSupport & (CopyTextureSupport.DifferentTypes | CopyTextureSupport.RTToTexture)) != (CopyTextureSupport.DifferentTypes | CopyTextureSupport.RTToTexture))
                {
                    error = "当前平台不支持把块图拷贝进压缩纹理";
                    return false;
                }
            }
            return true;
        }

        /// <summary>烘焙与间接表命令录制完成；生产者可在下一帧切换到 VT 材质。</summary>
        public void Publish()
        {
            indirectReady = true;
            rootReady = m_RootBaked;
        }

        /// <summary>烘焙通路：逐页清空 scratch → 生产者画内容 → 编码进物理槽。</summary>
        public void Bake(CommandBuffer cmd, NativeList<BakeOrder> bakes)
        {
            for (int i = 0; i < bakes.Length; ++i)
            {
                BakeOrder bake = bakes[i];
                using (new ProfilingScope(cmd, bakeSampler))
                {
                    physical.BindScratch(cmd);
                    m_Producer.Bake(cmd, new PageBakeRequest
                    {
                        page = bake.page,
                        uvRect = VirtualTextureMath.PageUVRect(bake.page, settings.pageCount, settings.pageTexels, (int)settings.pageBorder)
                    });
                }
                physical.Encode(cmd, requests.pool->SlotCoord(bake.slot));
                if (bake.page.mip == settings.mipCount - 1) { m_RootBaked = true; }
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginContextRendering -= BeginContext;
            RenderPipelineManager.endContextRendering -= EndContext;
            readback.Dispose();
            if (active == this) { active = null; }
            rootReady = false;
            requests.Dispose();
            indirectMap.Dispose();
            physical.Dispose();
            round.Dispose();
        }
    }
}
