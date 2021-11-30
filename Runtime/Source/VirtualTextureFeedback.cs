using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe class FVirtualTextureFeedback
    {
        internal bool isReady;
        internal NativeArray<Color32> readbackDatas;

        public FVirtualTextureFeedback(in bool bReady)
        {
            isReady = bReady;
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
                readbackDatas = request.GetData<Color32>();
            }
        }
    }
}