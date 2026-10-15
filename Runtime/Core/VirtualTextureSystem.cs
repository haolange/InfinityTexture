using System;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

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
        static readonly int s_ReadyID = Shader.PropertyToID("_VTReady");
        static readonly int s_LayerCountID = Shader.PropertyToID("_VTLayerCount");
        static readonly int s_FilterParamsID = Shader.PropertyToID("_VTFilterParams");

        public static VirtualTextureSystem active { get; private set; }

        public readonly VirtualTextureAsset settings;
        public readonly RequestPipeline requests;
        public readonly IndirectMap indirectMap;
        public readonly PhysicalLayers physical;

        readonly IPageProducer m_Producer;

        public VirtualTextureSystem(VirtualTextureAsset settings, IPageProducer producer)
        {
            this.settings = settings;
            m_Producer = producer;

            int poolCapacity = settings.poolSize * settings.poolSize;
            requests = new RequestPipeline(settings.pageCount, settings.poolSize, settings.bakesPerRound, settings.queueCapacity, settings.queueTimeout);
            indirectMap = new IndirectMap(settings.pageCount, poolCapacity, settings.indirectShader);
            EPageLayer[] layers = PhysicalLayers.Enabled(settings.layers);
            physical = new PhysicalLayers(settings, layers);

            Shader.SetGlobalVector(s_ParamsID, new Vector4(settings.pageCount, settings.mipCount, settings.pageTexels, (int)settings.pageBorder));
            Shader.SetGlobalVector(s_PoolParamsID, new Vector4(settings.slotTexels, 1.0f / settings.poolTexels, 0, 0));
            Shader.SetGlobalInt(s_LayerCountID, layers.Length);
            Shader.SetGlobalVector(s_FilterParamsID, new Vector4((int)settings.filter, settings.anisoSamples, 0, 0));
            Shader.SetGlobalInt(s_ReadyID, 0);
            active = this;
        }

        /// <summary>间接表第一次清空后才允许着色器读虚拟纹理。</summary>
        public static void SetReady(CommandBuffer cmd) => cmd.SetGlobalInt(s_ReadyID, 1);

        /// <summary>烘焙通路：逐页清空 scratch → 生产者画内容 → 编码进物理槽。</summary>
        public void Bake(CommandBuffer cmd, NativeList<BakeOrder> bakes)
        {
            for (int i = 0; i < bakes.Length; ++i)
            {
                BakeOrder bake = bakes[i];
                physical.BindScratch(cmd);
                m_Producer.Bake(cmd, new PageBakeRequest
                {
                    page = bake.page,
                    uvRect = VirtualTextureMath.PageUVRect(bake.page, settings.pageCount, settings.pageTexels, (int)settings.pageBorder)
                });
                physical.Encode(cmd, requests.pool->SlotCoord(bake.slot));
            }
        }

        public void Dispose()
        {
            if (active == this) { active = null; }
            Shader.SetGlobalInt(s_ReadyID, 0);
            requests.Dispose();
            indirectMap.Dispose();
            physical.Dispose();
        }
    }
}
