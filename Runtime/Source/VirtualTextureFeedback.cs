using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    public enum FeedbackBits
    {
        B8,
        B16
    }

    internal unsafe class FVirtualTextureFeedback
    {
        internal bool isReady;
        internal const FeedbackBits bits = FeedbackBits.B8;
        internal NativeArray<byte> readbackDatas;

        public FVirtualTextureFeedback(in bool isReady)
        {
            this.isReady = isReady;
        }

        internal void RequestReadback(CommandBuffer cmdBuffer, RenderTexture feedbackTexture)
        {
            if (isReady == true)
            {
                isReady = false;
                cmdBuffer.RequestAsyncReadback(feedbackTexture, 0, feedbackTexture.graphicsFormat, EnqueueCopy);
            }
        }

        private void EnqueueCopy(AsyncGPUReadbackRequest request)
        {
            if (request.hasError || request.done == true)
            {
                isReady = true;
                readbackDatas = request.GetData<byte>();
            }
        }
    }
}