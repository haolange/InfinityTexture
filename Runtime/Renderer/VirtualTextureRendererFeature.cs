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
        DrawTerrain,
    }

    internal static class VirtualTextureShaderID
    {
        public static int FeedbackTexture = Shader.PropertyToID("_FeedbackTexture");
        public static int VTFeedbackParams = Shader.PropertyToID("_VTFeedbackParams");
        public static int VTFeedbackFactor = Shader.PropertyToID("_VTFeedbackFactor");
        public static int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
    }

    internal class TerrainRenderPass : ScriptableRenderPass
    {
        LayerMask m_LayerMask;
        ShaderTagId m_ShaderPassID;
        EFeedbackScale m_feedbackScale;
        FilteringSettings m_FilterSetting;
        ProfilingSampler m_ProfilingSampler;
        RenderTargetIdentifier m_FeedbackTexture;
        //static Material vtMaterial = new Material(Shader.Find("Landscape/TerrainFeedback"));


        public TerrainRenderPass(in LayerMask layerMask, in EFeedbackScale feedbackScale)
        {
            m_LayerMask = layerMask;
            m_feedbackScale = feedbackScale;
            m_ShaderPassID = new ShaderTagId("VTFeedback");
            m_FilterSetting = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);
            m_ProfilingSampler = ProfilingSampler.Get(EVirtualTexturePass.DrawTerrain);
            m_FeedbackTexture = new RenderTargetIdentifier(VirtualTextureShaderID.FeedbackTexture);
        }

        public override void OnCameraSetup(CommandBuffer cmdBuffer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            cmdBuffer.GetTemporaryRT(VirtualTextureShaderID.FeedbackTexture, camera.pixelWidth / (int)m_feedbackScale, camera.pixelHeight / (int)m_feedbackScale, 16, FilterMode.Point, GraphicsFormat.B8G8R8A8_UNorm, 1, true);
            ConfigureTarget(m_FeedbackTexture);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext renderContext, ref RenderingData renderingData)
        {
            CommandBuffer cmdBuffer = CommandBufferPool.Get();
            Camera camera = renderingData.cameraData.camera;

            using (new ProfilingScope(cmdBuffer, m_ProfilingSampler))
            {
                renderContext.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();

                DrawingSettings drawSetting = new DrawingSettings(m_ShaderPassID, new SortingSettings(camera) { criteria = SortingCriteria.QuantizedFrontToBack })
                {
                    enableInstancing = true,
                    //overrideMaterial = m_FeedbackMaterial,
                    //overrideMaterialPassIndex = 0
                };
                renderContext.DrawRenderers(renderingData.cullResults, ref drawSetting, ref m_FilterSetting);
            }

            renderContext.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);
        }

        public override void OnCameraCleanup(CommandBuffer cmdBuffer)
        {
            cmdBuffer.ReleaseTemporaryRT(VirtualTextureShaderID.FeedbackTexture);
        }
    }

    public class VirtualTextureRendererFeature : ScriptableRendererFeature
    {
        public LayerMask layerMask;
        public EFeedbackScale feedbackScale;
        TerrainRenderPass m_TerrainRenderPass;


        public override void Create()
        {
            m_TerrainRenderPass = new TerrainRenderPass(layerMask, feedbackScale);
            m_TerrainRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.AddPass(m_TerrainRenderPass);
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}


