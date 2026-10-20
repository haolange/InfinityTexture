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
            public bool update;
            public RoundResult round;
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
            public FeedbackReadback readback;
            public TextureHandle feedback;
        }

        readonly ShaderTagId m_ShaderTag = new ShaderTagId("VTFeedback");
        readonly FilteringSettings m_Filtering;
        readonly FeedbackReadback m_Readback;
        readonly RoundResult m_Round = new RoundResult();
        VirtualTextureSystem m_System;
        bool m_IndirectReady;

        public VirtualTexturePass(LayerMask layerMask)
        {
            m_Filtering = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            m_Readback = new FeedbackReadback(OnFeedbackArrived);
        }

        static void OnFeedbackArrived(Unity.Collections.NativeArray<uint> pixels)
        {
            VirtualTextureSystem.active?.requests.Schedule(pixels);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            VirtualTextureSystem system = Application.isPlaying ? VirtualTextureSystem.active : null;
            if (system != m_System)
            {
                m_Readback.Cancel();
                m_Round.Clear();
                m_IndirectReady = false;
                m_System = system;
            }
            if (system == null) { return; }

            var cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game) { return; }

            bool hasRound = system.requests.TryTakeResult(m_Round);
            if (hasRound && m_Round.bakes.Length > 0) { AddBakePass(renderGraph, system); }
            AddIndirectPass(renderGraph, system, !m_IndirectReady || (hasRound && m_Round.residencyChanged));

            TextureHandle feedback = AddFeedbackPass(renderGraph, frameData, cameraData);
            if (!system.requests.isBusy && !m_Readback.inFlight) { AddReadbackPass(renderGraph, feedback); }
        }

        void AddBakePass(RenderGraph renderGraph, VirtualTextureSystem system)
        {
            using (var builder = renderGraph.AddUnsafePass<BakeData>("VT Bake", out var data, ProfilingSampler.Get(EVirtualTexturePass.Bake)))
            {
                data.system = system;
                data.round = m_Round;
                system.physical.UseResources(renderGraph, builder, AccessFlags.ReadWrite);
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
                data.round = m_Round;
                data.update = update;
                TextureHandle indirect = renderGraph.ImportTexture(system.indirectMap.texture);
                builder.UseTexture(indirect, update ? AccessFlags.Write : AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(indirect, Shader.PropertyToID("_VTIndirectMap"));
                system.physical.UseResources(renderGraph, builder, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (IndirectData d, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    if (d.update) { d.indirectMap.Update(cmd, d.round.indirectWrites, d.round.levelRanges); }
                    VirtualTextureSystem.SetReady(cmd);
                });
            }
            m_IndirectReady = true;
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

        void AddReadbackPass(RenderGraph renderGraph, TextureHandle feedback)
        {
            using (var builder = renderGraph.AddUnsafePass<ReadbackData>("VT Readback", out var data, ProfilingSampler.Get(EVirtualTexturePass.Readback)))
            {
                data.readback = m_Readback;
                data.feedback = feedback;
                builder.UseTexture(feedback, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ReadbackData d, UnsafeGraphContext context) =>
                {
                    d.readback.Request(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), d.feedback);
                });
            }
        }

        public void Dispose()
        {
            m_Readback.Dispose();
            m_Round.Dispose();
            m_System = null;
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
            m_Pass?.Dispose();
            m_Pass = null;
        }
    }
}
