using System;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>反馈图尺寸：渲染分辨率 / 16（向上取整），以及实际的逐轴缩小倍数。</summary>
    internal static class FeedbackLayout
    {
        public const int Downscale = 16;

        public static int2 Size(int2 renderSize) => math.max(1, (renderSize + Downscale - 1) / Downscale);

        /// <summary>着色器用这个倍数把反馈图上的导数换算回渲染分辨率。</summary>
        public static float2 Scale(int2 renderSize) => (float2)renderSize / Size(renderSize);
    }

    /// <summary>
    /// 反馈图的异步回读。同一时间最多一个请求在途；数据到达时交给 onArrived（主线程）。
    /// </summary>
    internal class FeedbackReadback : IDisposable
    {
        static readonly ProfilerMarker s_Complete = new ProfilerMarker("VT Readback Callback");
        readonly Action<NativeArray<uint>> m_OnArrived;
        bool m_InFlight;
        sealed class Generation
        {
            public bool cancelled;
            public readonly Action<AsyncGPUReadbackRequest> callback;

            public Generation(FeedbackReadback owner)
            {
                callback = request =>
                {
                    if (!cancelled) { owner.OnComplete(request); }
                };
            }
        }

        Generation m_Generation;

        public bool inFlight => m_InFlight;

        public FeedbackReadback(Action<NativeArray<uint>> onArrived)
        {
            m_OnArrived = onArrived;
            m_Generation = new Generation(this);
        }

        public void Request(CommandBuffer cmd, RenderTexture feedback)
        {
            if (m_InFlight) { return; }
            m_InFlight = true;
            cmd.RequestAsyncReadback(feedback, 0, m_Generation.callback);
        }

        void OnComplete(AsyncGPUReadbackRequest request)
        {
            using var complete = s_Complete.Auto();
            m_InFlight = false;
            if (request.hasError || !request.done) { return; }
            m_OnArrived(request.GetData<uint>());
        }

        /// <summary>丢弃在途请求：之后到达的回调会被忽略。</summary>
        public void Cancel()
        {
            m_Generation.cancelled = true;
            m_Generation = new Generation(this);
            m_InFlight = false;
        }

        public void Dispose()
        {
            m_Generation.cancelled = true;
            m_InFlight = false;
        }
    }
}
