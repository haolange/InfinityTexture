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
    public enum EFeedbackScale
    {
        X1 = 1,
        X2 = 2,
        X4 = 4,
        X8 = 8,
        X16 = 16
    }

    internal enum EVirtualTexturePass
    {
        VirtualTexture,
        DrawFeedback,
        DrawPageTable,
        DrawPageColor,
        CompressPage,
        CopyPageToPhyscis
    }

    internal class FeedbackRenderPass : ScriptableRenderPass
    {
        int2 m_FeedbackSize;
        LayerMask m_LayerMask;
        ShaderTagId m_ShaderPassID;
        EFeedbackScale m_FeedbackScale;
        FilteringSettings m_FilterSetting;
        ProfilingSampler m_DrawFeedbackSampler;
        ProfilingSampler m_DrawPageTableSampler;
        ProfilingSampler m_DrawPageColorSampler;
        ProfilingSampler m_VirtualTextureSampler;
        FVirtualTextureFeedback m_FeedbackProcessor;
        private bool m_PageTableInitialized;
        private VirtualTextureVolume m_ActiveVolume;

        private class PassData
        {
            internal FeedbackRenderPass pass;
            internal Camera camera;
            internal RendererListHandle rendererList;
            internal TextureHandle feedback;
            internal TextureHandle feedbackDepth;
            internal TextureHandle pageTable;
            internal TextureHandle colorA;
            internal TextureHandle colorB;
        }

        public FeedbackRenderPass(in LayerMask layerMask, int2 feedbackSize, in EFeedbackScale feedbackScale)
        {
            m_LayerMask = layerMask;
            m_FeedbackSize = feedbackSize;
            m_FeedbackScale = feedbackScale;
            m_ShaderPassID = new ShaderTagId("VTFeedback");
            m_FilterSetting = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);
            m_DrawFeedbackSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawFeedback);
            m_DrawPageTableSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawPageTable);
            m_DrawPageColorSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawPageColor);
            m_VirtualTextureSampler = ProfilingSampler.Get(EVirtualTexturePass.VirtualTexture);
            m_FeedbackProcessor = new FVirtualTextureFeedback(true);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!Application.isPlaying || VirtualTextureVolume.s_VirtualTextureVolume == null)
            {
                m_FeedbackProcessor.Reset();
                m_PageTableInitialized = false;
                m_ActiveVolume = null;
                return;
            }
            if (m_ActiveVolume != VirtualTextureVolume.s_VirtualTextureVolume)
            {
                m_FeedbackProcessor.Reset();
                m_PageTableInitialized = false;
                m_ActiveVolume = VirtualTextureVolume.s_VirtualTextureVolume;
            }
            var camera = frameData.Get<UniversalCameraData>().camera;
            var renderingData = frameData.Get<UniversalRenderingData>();
            var virtualTexture = VirtualTextureVolume.s_VirtualTextureVolume.virtualTexture;
            int width = math.max(1, math.min(m_FeedbackSize.x, camera.pixelWidth) / (int)m_FeedbackScale);
            int height = math.max(1, math.min(m_FeedbackSize.y, camera.pixelHeight) / (int)m_FeedbackScale);
            var feedbackDesc = new TextureDesc(width, height)
            {
                name = "FeedbackTexture",
                format = GraphicsFormat.R8G8B8A8_UNorm
            };
            var depthDesc = new TextureDesc(width, height)
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
                passData.camera = camera;
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

        private unsafe void DrawVirtualTexture(CommandBuffer cmdBuffer, PassData data)
        {
            Camera camera = data.camera;
            FPageProducer pageProducer = VirtualTextureVolume.s_VirtualTextureVolume.pageProducer;
            FPageRenderer pageRenderer = VirtualTextureVolume.s_VirtualTextureVolume.pageRenderer;
            VirtualTextureAsset virtualTexture = VirtualTextureVolume.s_VirtualTextureVolume.virtualTexture;
            
            using (new ProfilingScope(cmdBuffer, m_DrawPageTableSampler))
            {
                if (!m_PageTableInitialized)
                {
                    cmdBuffer.SetRenderTarget(data.pageTable);
                    cmdBuffer.ClearRenderTarget(false, true, Color.clear);
                    cmdBuffer.SetGlobalInt("_VTReady", 1);
                    m_PageTableInitialized = true;
                }
                if (m_FeedbackProcessor.isReady && m_FeedbackProcessor.readbackDatas.IsCreated)
                {
                    NativeArray<int4> decodeDatas = new NativeArray<int4>(m_FeedbackProcessor.readbackDatas.Length, Allocator.TempJob);

                    FDecodeFeedbackJob decodeFeedbackJob;
                    decodeFeedbackJob.pageSize = virtualTexture.pageSize;
                    decodeFeedbackJob.decodeDatas = decodeDatas;
                    decodeFeedbackJob.encodeDatas = m_FeedbackProcessor.readbackDatas;
                    decodeFeedbackJob.Schedule(m_FeedbackProcessor.readbackDatas.Length, 256).Complete();

                    pageProducer.ProcessFeedback(ref decodeDatas, virtualTexture.NumMip, virtualTexture.tileNum, virtualTexture.pageSize, virtualTexture.lruCache, ref pageRenderer.loadRequests);
                    decodeDatas.Dispose();
                    m_FeedbackProcessor.ReleaseData();

                    cmdBuffer.SetRenderTarget(data.pageTable);
                    cmdBuffer.ClearRenderTarget(false, true, Color.clear);
                    pageRenderer.DrawPageTable(cmdBuffer, pageProducer);
                    
                }
            }

            using (new ProfilingScope(cmdBuffer, m_DrawFeedbackSampler))
            {
                cmdBuffer.SetRenderTarget(data.feedback, data.feedbackDepth);
                cmdBuffer.ClearRenderTarget(true, true, Color.black);
                // x: 页表大小(单位: 页)
                // y: 虚拟贴图大小(单位: 像素)
                // z: 最大mipmap等级
                // w: mipBias
                cmdBuffer.SetGlobalVector("_VTFeedbackParams", new Vector4(virtualTexture.pageSize, virtualTexture.pageSize * virtualTexture.tileSize * (1.0f / (float)m_FeedbackScale), virtualTexture.NumMip, 0.1f));

                float cameraAspect = (float) camera.pixelRect.width / (float) camera.pixelRect.height;
                Matrix4x4 projectionMatrix = Matrix4x4.Perspective(90, cameraAspect, camera.nearClipPlane, camera.farClipPlane);
                projectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, camera.worldToCameraMatrix, projectionMatrix, false);
                cmdBuffer.DrawRendererList(data.rendererList);

                projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                RenderingUtils.SetViewAndProjectionMatrices(cmdBuffer, camera.worldToCameraMatrix, projectionMatrix, false);
            }

            //read-back feedback
            m_FeedbackProcessor.RequestReadback(cmdBuffer, data.feedback);

            using (new ProfilingScope(cmdBuffer, m_DrawPageColorSampler))
            {
                cmdBuffer.SetRenderTarget(new RenderTargetIdentifier[] { data.colorA, data.colorB }, data.colorA);

                FDrawPageParameter drawPageParameter = VirtualTextureVolume.s_VirtualTextureVolume.GetDrawPageParamter();
                pageRenderer.DrawPageColor(cmdBuffer, pageProducer, virtualTexture, ref virtualTexture.lruCache[0], drawPageParameter);
            }
        }

        internal void Dispose()
        {
            m_FeedbackProcessor.Dispose();
            m_ActiveVolume = null;
            m_PageTableInitialized = false;
        }
    }

    public class FeedbackRenderer : ScriptableRendererFeature
    {
        [Header("Filter")]
        public LayerMask layerMask;

        [Header("Feedback")]
        public int2 feedbackSize;
        public EFeedbackScale feedbackScale;

        FeedbackRenderPass m_FeedbackRenderPass;

        public override void Create()
        {
            m_FeedbackRenderPass = new FeedbackRenderPass(layerMask, feedbackSize, feedbackScale);
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
