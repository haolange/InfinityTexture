using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

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
        DrawFeedback,
        DrawPageTable,
        DrawPageColor,
        CompressPage,
        CopyPageToPhyscis
    }

    internal static class VirtualTextureShaderID
    {
        public static int FeedbackTexture = Shader.PropertyToID("_FeedbackTexture");
        public static int VTFeedbackParams = Shader.PropertyToID("_VTFeedbackParams");
        public static int VTFeedbackFactor = Shader.PropertyToID("_VTFeedbackFactor");
        public static int PageTableBuffer = Shader.PropertyToID("_PageTableBuffer");
        public static int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
    }

    internal class FeedbackRenderPass : ScriptableRenderPass
    {
        LayerMask m_LayerMask;
        ShaderTagId m_ShaderPassID;
        EFeedbackScale m_feedbackScale;
        FilteringSettings m_FilterSetting;
        ProfilingSampler m_FeedbackSampler;
        RenderTexture m_FeedbackTexture;
        RenderTargetIdentifier m_FeedbackTextureID;
        FVirtualTextureFeedback m_Feedbacker;

        public FeedbackRenderPass(in LayerMask layerMask, in EFeedbackScale feedbackScale)
        {
            m_LayerMask = layerMask;
            m_feedbackScale = feedbackScale;
            m_ShaderPassID = new ShaderTagId("VTFeedback");
            m_FilterSetting = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);
            m_FeedbackSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawFeedback);
            m_Feedbacker = new FVirtualTextureFeedback(true);
        }

        public override void OnCameraSetup(CommandBuffer cmdBuffer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            m_FeedbackTexture = RenderTexture.GetTemporary(camera.pixelWidth / (int)m_feedbackScale, camera.pixelHeight / (int)m_feedbackScale, 1, GraphicsFormat.B8G8R8A8_UNorm, 1);
            m_FeedbackTextureID = new RenderTargetIdentifier(m_FeedbackTexture);
            ConfigureTarget(m_FeedbackTextureID);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext renderContext, ref RenderingData renderingData)
        {
            if(!Application.isPlaying) { return; }

            CommandBuffer cmdBuffer = CommandBufferPool.Get();
            Camera camera = renderingData.cameraData.camera;

            using (new ProfilingScope(cmdBuffer, m_FeedbackSampler))
            {
                renderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();
                ProcessFeedback(renderContext, cmdBuffer, camera, renderingData);
            }

            renderContext.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
        }

        public override void OnCameraCleanup(CommandBuffer cmdBuffer)
        {
            RenderTexture.ReleaseTemporary(m_FeedbackTexture);
        }

        public unsafe void ProcessFeedback(ScriptableRenderContext renderContext, CommandBuffer cmdBuffer, Camera camera, in RenderingData renderingData)
        {
            FPageProducer pageProducer = VirtualTextureVolume.s_VirtualTextureVolume.pageProducer;
            FPageRenderer pageRenderer = VirtualTextureVolume.s_VirtualTextureVolume.pageRenderer;
            VirtualTextureAsset virtualTexture = VirtualTextureVolume.s_VirtualTextureVolume.virtualTexture;

            if (m_Feedbacker.isReady)
            {
                //draw pageTable
                if (m_Feedbacker.readbackDatas.IsCreated)
                {
                    //Debug.Log(m_Feedbacker.readbackDatas[0]);
                    pageProducer.ProcessFeedback(m_Feedbacker.readbackDatas, virtualTexture.MaxMipLevel, virtualTexture.tileNum, virtualTexture.pageSize, virtualTexture.lruCache, pageRenderer.pageRequests);
                    pageRenderer.DrawPageTable(renderContext, cmdBuffer, virtualTexture.pageTableTexture, pageProducer);
                }

                //draw feedback
                DrawingSettings drawSetting = new DrawingSettings(m_ShaderPassID, new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack })
                {
                    enableInstancing = true,
                    //overrideMaterial = m_FeedbackMaterial,
                    //overrideMaterialPassIndex = 0
                };
                renderContext.DrawRenderers(renderingData.cullResults, ref drawSetting, ref m_FilterSetting);

                //request readBack
                m_Feedbacker.RequestReadback(cmdBuffer, m_FeedbackTexture);
            }

            pageRenderer.DrawPageColor(renderContext, cmdBuffer, virtualTexture.colorBuffers, pageProducer, virtualTexture.lruCache, virtualTexture.tileNum, virtualTexture.tileSize);
        }
    }

    public class FeedbackRenderer : ScriptableRendererFeature
    {
        public LayerMask layerMask;
        public EFeedbackScale feedbackScale;
        FeedbackRenderPass m_FeedbackRenderPass;


        public override void Create()
        {
            m_FeedbackRenderPass = new FeedbackRenderPass(layerMask, feedbackScale);
            m_FeedbackRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPrepasses;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.AddPass(m_FeedbackRenderPass);
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}


