using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 反馈图的异步回读。同一时间最多一个请求在途；数据到达时交给 onArrived（主线程）。
    /// </summary>
    internal class FeedbackReadback : IDisposable
    {
        readonly Action<NativeArray<uint>> m_OnArrived;
        bool m_InFlight;
        int m_Generation;

        public bool inFlight => m_InFlight;

        public FeedbackReadback(Action<NativeArray<uint>> onArrived)
        {
            m_OnArrived = onArrived;
        }

        public void Request(CommandBuffer cmd, RenderTexture feedback)
        {
            if (m_InFlight) { return; }
            m_InFlight = true;
            int generation = m_Generation;
            cmd.RequestAsyncReadback(feedback, 0, request => OnComplete(request, generation));
        }

        void OnComplete(AsyncGPUReadbackRequest request, int generation)
        {
            if (generation != m_Generation) { return; }
            m_InFlight = false;
            if (request.hasError || !request.done) { return; }
            m_OnArrived(request.GetData<uint>());
        }

        /// <summary>丢弃在途请求：之后到达的回调会被忽略。</summary>
        public void Cancel()
        {
            ++m_Generation;
            m_InFlight = false;
        }

        public void Dispose() => Cancel();
    }
}
