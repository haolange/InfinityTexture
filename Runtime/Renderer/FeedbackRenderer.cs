using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Landscape.RuntimeVirtualTexture
{
    internal enum EVirtualTexturePass
    {
        VirtualTexture,
        DrawFeedback,
        DrawPageTable,
        DrawPageColor,
        CompressPage,
        CopyPageToPhyscis
    }

    internal unsafe class FeedbackRenderPass : ScriptableRenderPass
    {
        static readonly int s_FeedbackScaleID = Shader.PropertyToID("_VTFeedbackScale");

        LayerMask m_LayerMask;
        ShaderTagId m_ShaderPassID;
        FilteringSettings m_FilterSetting;
        ProfilingSampler m_DrawFeedbackSampler;
        ProfilingSampler m_DrawPageTableSampler;
        ProfilingSampler m_DrawPageColorSampler;
        ProfilingSampler m_VirtualTextureSampler;
        FeedbackReadback m_Readback;
        NativeList<BakeOrder> m_Bakes;
        private bool m_PageTableInitialized;
        private VirtualTextureVolume m_ActiveVolume;

        private class PassData
        {
            internal FeedbackRenderPass pass;
            internal UniversalCameraData cameraData;
            internal Vector2 feedbackScale;
            internal VirtualTextureVolume volume;
            internal bool hasRound;
            internal bool residencyChanged;
            internal bool requestReadback;
            internal RendererListHandle rendererList;
            internal TextureHandle feedback;
            internal TextureHandle feedbackDepth;
            internal TextureHandle pageTable;
            internal TextureHandle colorA;
            internal TextureHandle colorB;
        }

        public FeedbackRenderPass(in LayerMask layerMask)
        {
            m_LayerMask = layerMask;
            m_ShaderPassID = new ShaderTagId("VTFeedback");
            m_FilterSetting = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);
            m_DrawFeedbackSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawFeedback);
            m_DrawPageTableSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawPageTable);
            m_DrawPageColorSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawPageColor);
            m_VirtualTextureSampler = ProfilingSampler.Get(EVirtualTexturePass.VirtualTexture);
            m_Readback = new FeedbackReadback(OnFeedbackArrived);
            m_Bakes = new NativeList<BakeOrder>(64, Allocator.Persistent);
        }

        static void OnFeedbackArrived(NativeArray<uint> pixels)
        {
            VirtualTextureVolume volume = VirtualTextureVolume.s_VirtualTextureVolume;
            if (volume != null) { volume.requestPipeline.Schedule(pixels); }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            VirtualTextureVolume volume = VirtualTextureVolume.s_VirtualTextureVolume;
            if (!Application.isPlaying || volume == null)
            {
                m_Readback.Cancel();
                m_PageTableInitialized = false;
                m_ActiveVolume = null;
                return;
            }
            if (m_ActiveVolume != volume)
            {
                m_Readback.Cancel();
                m_PageTableInitialized = false;
                m_ActiveVolume = volume;
            }

            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game) { return; }

            var camera = cameraData.camera;
            var renderingData = frameData.Get<UniversalRenderingData>();
            var virtualTexture = volume.virtualTexture;
            int2 renderSize = new int2(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
            int2 feedbackSize = FeedbackLayout.Size(renderSize);
            var feedbackDesc = new TextureDesc(feedbackSize.x, feedbackSize.y)
            {
                name = "FeedbackTexture",
                format = GraphicsFormat.R32_UInt
            };
            var depthDesc = new TextureDesc(feedbackSize.x, feedbackSize.y)
            {
                name = "FeedbackDepth",
                format = GraphicsFormat.D16_UNorm
            };
            var feedback = renderGraph.CreateTexture(feedbackDesc);
            var feedbackDepth = renderGraph.CreateTexture(depthDesc);
            var pageTable = renderGraph.ImportTexture(virtualTexture.tableTextureHandle);
            var colorA = renderGraph.ImportTexture(virtualTexture.colorTextureHandles[0]);
            var colorB = renderGraph.ImportTexture(virtualTexture.colorTextureHandles[1]);
            var drawingSettings = new DrawingSettings(m_ShaderPassID, new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack })
            {
                enableInstancing = true
            };
            var rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawingSettings, m_FilterSetting));

            using (var builder = renderGraph.AddUnsafePass<PassData>("Virtual Texture Feedback", out var passData, m_VirtualTextureSampler))
            {
                passData.pass = this;
                passData.cameraData = cameraData;
                passData.feedbackScale = (Vector2)FeedbackLayout.Scale(renderSize);
                passData.volume = volume;
                // 只取已经跑完的一轮；没跑完就沿用现有驻留状态。
                passData.hasRound = volume.requestPipeline.TryTakeResult(m_Bakes, out passData.residencyChanged);
                passData.requestReadback = !volume.requestPipeline.isBusy && !m_Readback.inFlight;
                passData.rendererList = rendererList;
                passData.feedback = feedback;
                passData.feedbackDepth = feedbackDepth;
                passData.pageTable = pageTable;
                passData.colorA = colorA;
                passData.colorB = colorB;
                builder.UseRendererList(rendererList);
                builder.UseTexture(feedback, AccessFlags.ReadWrite);
                builder.UseTexture(feedbackDepth, AccessFlags.Write);
                builder.UseTexture(pageTable, AccessFlags.ReadWrite);
                builder.UseTexture(colorA, AccessFlags.ReadWrite);
                builder.UseTexture(colorB, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmdBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.pass.DrawVirtualTexture(cmdBuffer, data);
                });
            }
        }

        private void DrawVirtualTexture(CommandBuffer cmdBuffer, PassData data)
        {
            VirtualTextureVolume volume = data.volume;
            FPageRenderer pageRenderer = volume.pageRenderer;
            VirtualTextureAsset virtualTexture = volume.virtualTexture;

            if (data.hasRound && m_Bakes.Length > 0)
            {
                using (new ProfilingScope(cmdBuffer, m_DrawPageColorSampler))
                {
                    cmdBuffer.SetRenderTarget(new RenderTargetIdentifier[] { data.colorA, data.colorB }, data.colorA);
                    pageRenderer.BakePages(cmdBuffer, m_Bakes, volume.requestPipeline.pool, virtualTexture, volume.GetDrawPageParamter());
                }
            }

            using (new ProfilingScope(cmdBuffer, m_DrawPageTableSampler))
            {
                if (!m_PageTableInitialized || data.residencyChanged)
                {
                    cmdBuffer.SetRenderTarget(data.pageTable);
                    cmdBuffer.ClearRenderTarget(false, true, Color.clear);
                    pageRenderer.DrawPageTable(cmdBuffer, volume.requestPipeline.pool);
                    cmdBuffer.SetGlobalInt("_VTReady", 1);
                    m_PageTableInitialized = true;
                }
            }

            using (new ProfilingScope(cmdBuffer, m_DrawFeedbackSampler))
            {
                cmdBuffer.SetRenderTarget(data.feedback, data.feedbackDepth);
                cmdBuffer.ClearRenderTarget(true, true, Color.clear);
                // 用当前相机的投影（不带抖动）；mip 按这次实际的缩小倍数补偿。
                cmdBuffer.SetGlobalVector(s_FeedbackScaleID, data.feedbackScale);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, data.cameraData.GetViewMatrix(), data.cameraData.GetGPUProjectionMatrixNoJitter(), false);
                cmdBuffer.DrawRendererList(data.rendererList);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, data.cameraData.GetViewMatrix(), data.cameraData.GetGPUProjectionMatrix(), false);
            }

            if (data.requestReadback)
            {
                m_Readback.Request(cmdBuffer, data.feedback);
            }
        }

        internal void Dispose()
        {
            m_Readback.Dispose();
            m_Bakes.Dispose();
            m_ActiveVolume = null;
            m_PageTableInitialized = false;
        }
    }

    public class FeedbackRenderer : ScriptableRendererFeature
    {
        [Header("Filter")]
        public LayerMask layerMask;

        FeedbackRenderPass m_FeedbackRenderPass;

        public override void Create()
        {
            m_FeedbackRenderPass = new FeedbackRenderPass(layerMask);
            m_FeedbackRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_FeedbackRenderPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_FeedbackRenderPass != null) { m_FeedbackRenderPass.Dispose(); }
            m_FeedbackRenderPass = null;
        }
    }
}
