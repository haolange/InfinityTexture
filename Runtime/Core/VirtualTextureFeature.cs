using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Landscape.RuntimeVirtualTexture
{
    internal enum EVirtualTexturePass
    {
        Bake,
        Indirect,
        Feedback,
        Readback
    }

    /// <summary>
    /// 每帧的虚拟纹理 Pass，按顺序：
    /// Bake（消费已完成的烘焙单）→ Indirect（驻留变化时重写间接表）→ Feedback（绘制 PageId）→ Readback（异步回读）。
    /// 解析在 Job 里完成，这里只取已经完成的结果。
    /// </summary>
    internal class VirtualTexturePass : ScriptableRenderPass
    {
        static readonly int s_FeedbackScaleID = Shader.PropertyToID("_VTFeedbackScale");


        class BakeData
        {
            public VirtualTextureSystem system;
            public RoundResult round;
        }

        class IndirectData
        {
            public IndirectMap indirectMap;
            public RoundResult round;
            public bool update;
        }

        class FeedbackData
        {
            public RendererListHandle rendererList;
            public Vector4 feedbackScale;
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public Matrix4x4 projectionNoJitter;
        }

        class ReadbackData
        {
            public VirtualTextureSystem system;
            public TextureHandle feedback;
        }

        readonly ShaderTagId m_ShaderTag = new ShaderTagId("VTFeedback");
        readonly FilteringSettings m_Filtering;

        public VirtualTexturePass(LayerMask layerMask)
        {
            m_Filtering = new FilteringSettings(RenderQueueRange.opaque, layerMask);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            VirtualTextureSystem system = Application.isPlaying ? VirtualTextureSystem.active : null;
            if (system == null) { return; }

            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game) { return; }

            bool hasRound = system.TryTakeRound();
            bool bake = hasRound && system.round.bakes.Length > 0;
            system.physical.ImportResources(renderGraph, bake);
            if (bake) { AddBakePass(renderGraph, system); }
            AddIndirectPass(renderGraph, system, !system.indirectReady || (hasRound && system.round.residencyChanged));

            TextureHandle feedback = AddFeedbackPass(renderGraph, frameData, cameraData);
            if (system.ReserveReadback(cameraData.camera)) { AddReadbackPass(renderGraph, feedback, system); }
        }

        void AddBakePass(RenderGraph renderGraph, VirtualTextureSystem system)
        {
            using (var builder = renderGraph.AddUnsafePass<BakeData>("VT Bake", out var data, ProfilingSampler.Get(EVirtualTexturePass.Bake)))
            {
                data.system = system;
                data.round = system.round;
                system.physical.UseResources(builder, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BakeData d, UnsafeGraphContext context) =>
                {
                    d.system.Bake(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), d.round.bakes);
                });
            }
        }

        void AddIndirectPass(RenderGraph renderGraph, VirtualTextureSystem system, bool update)
        {
            using (var builder = renderGraph.AddUnsafePass<IndirectData>("VT Indirect", out var data, ProfilingSampler.Get(EVirtualTexturePass.Indirect)))
            {
                data.indirectMap = system.indirectMap;
                data.round = system.round;
                data.update = update;
                TextureHandle indirect = renderGraph.ImportTexture(system.indirectMap.texture);
                builder.UseTexture(indirect, update ? AccessFlags.Write : AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(indirect, Shader.PropertyToID("_VTIndirectMap"));
                system.physical.UseResources(builder, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (IndirectData d, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    if (d.update) { d.indirectMap.Update(cmd, d.round.indirectWrites, d.round.levelRanges); }
                    VirtualTextureSystem.SetReady(cmd);
                });
            }
            system.indirectReady = true;
        }

        TextureHandle AddFeedbackPass(RenderGraph renderGraph, ContextContainer frameData, UniversalCameraData cameraData)
        {
            int2 renderSize = new int2(cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
            int2 feedbackSize = FeedbackLayout.Size(renderSize);
            TextureHandle feedback = renderGraph.CreateTexture(new TextureDesc(feedbackSize.x, feedbackSize.y)
            {
                name = "VTFeedback",
                format = GraphicsFormat.R32_UInt,
                clearBuffer = true,
                clearColor = Color.clear
            });
            TextureHandle depth = renderGraph.CreateTexture(new TextureDesc(feedbackSize.x, feedbackSize.y)
            {
                name = "VTFeedbackDepth",
                format = GraphicsFormat.D16_UNorm,
                clearBuffer = true
            });

            var renderingData = frameData.Get<UniversalRenderingData>();
            var drawing = new DrawingSettings(m_ShaderTag, new SortingSettings(cameraData.camera) { criteria = SortingCriteria.QuantizedFrontToBack })
            {
                enableInstancing = true
            };
            RendererListHandle rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, m_Filtering));

            using (var builder = renderGraph.AddRasterRenderPass<FeedbackData>("VT Feedback", out var data, ProfilingSampler.Get(EVirtualTexturePass.Feedback)))
            {
                data.rendererList = rendererList;
                data.feedbackScale = (Vector2)FeedbackLayout.Scale(renderSize);
                data.view = cameraData.GetViewMatrix();
                data.projection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), !frameData.Get<UniversalResourceData>().isActiveTargetBackBuffer);
                data.projectionNoJitter = GL.GetGPUProjectionMatrix(cameraData.camera.nonJitteredProjectionMatrix, true);

                builder.SetRenderAttachment(feedback, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.UseRendererList(rendererList);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (FeedbackData d, RasterGraphContext context) =>
                {
                    // 当前相机的投影（不带抖动）；mip 按这次实际的缩小倍数补偿。
                    context.cmd.SetGlobalVector(s_FeedbackScaleID, d.feedbackScale);
                    RenderingUtils.SetViewAndProjectionMatrices(context.cmd, d.view, d.projectionNoJitter, false);
                    context.cmd.DrawRendererList(d.rendererList);
                    RenderingUtils.SetViewAndProjectionMatrices(context.cmd, d.view, d.projection, false);
                });
            }
            return feedback;
        }

        void AddReadbackPass(RenderGraph renderGraph, TextureHandle feedback, VirtualTextureSystem system)
        {
            using (var builder = renderGraph.AddUnsafePass<ReadbackData>("VT Readback", out var data, ProfilingSampler.Get(EVirtualTexturePass.Readback)))
            {
                data.system = system;
                data.feedback = feedback;
                builder.UseTexture(feedback, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ReadbackData d, UnsafeGraphContext context) =>
                {
                    d.system.RequestReadback(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), d.feedback);
                });
            }
        }

    }

    /// <summary>URP 渲染特性：把 VirtualTexturePass 加到每个 Game 相机的不透明物体之前。</summary>
    public class VirtualTextureFeature : ScriptableRendererFeature
    {
        [Tooltip("参与反馈的渲染层")]
        public LayerMask layerMask = ~0;

        VirtualTexturePass m_Pass;

        public override void Create()
        {
            m_Pass = new VirtualTexturePass(layerMask) { renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
        }
    }
}
