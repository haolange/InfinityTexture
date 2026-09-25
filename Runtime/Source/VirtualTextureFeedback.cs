using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe class FVirtualTextureFeedback : System.IDisposable
    {
        internal bool isReady;
        internal NativeArray<Color32> readbackDatas;
        private bool m_InFlight;
        private bool m_Disposed;
        private int m_Generation;

        public FVirtualTextureFeedback(in bool bReady)
        {
            isReady = bReady;
        }

        internal void RequestReadback(CommandBuffer cmdBuffer, RenderTexture feedbackTexture)
        {
            if (m_InFlight || m_Disposed || readbackDatas.IsCreated) { return; }
            m_InFlight = true;
            isReady = false;
            int generation = m_Generation;
            cmdBuffer.RequestAsyncReadback(feedbackTexture, 0, feedbackTexture.graphicsFormat, request => EnqueueCopy(request, generation));
        }

        private void EnqueueCopy(AsyncGPUReadbackRequest request, int generation)
        {
            if (m_Disposed || generation != m_Generation) { return; }
            m_InFlight = false;
            if (request.hasError || !request.done) { return; }

            NativeArray<Color32> source = request.GetData<Color32>();
            if (readbackDatas.IsCreated) { readbackDatas.Dispose(); }
            readbackDatas = new NativeArray<Color32>(source.Length, Allocator.Persistent);
            NativeArray<Color32>.Copy(source, readbackDatas);
            isReady = true;
        }

        internal void ReleaseData()
        {
            isReady = false;
            if (readbackDatas.IsCreated)
            {
                readbackDatas.Dispose();
            }
        }

        internal void Reset()
        {
            ++m_Generation;
            m_InFlight = false;
            ReleaseData();
        }

        public void Dispose()
        {
            m_Disposed = true;
            Reset();
        }
    }
}
