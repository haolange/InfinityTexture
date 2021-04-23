using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    internal class FeedbackReader
	{
        public bool bReady
        {
            get
            {
                return m_ReadbackRequest.done || m_ReadbackRequest.hasError;
            }
        }

        private AsyncGPUReadbackRequest m_ReadbackRequest;

		public void RequestReadback(RenderTexture texture)
		{
            if (!m_ReadbackRequest.done && !m_ReadbackRequest.hasError) { return; }
            m_ReadbackRequest = AsyncGPUReadback.Request(texture);
        }

		public void ProcessAndDrawPageTable(FPageRenderer pageRenderer, FPageProducer pageProducer, VirtualTextureAsset virtualTexture)
		{
			if(m_ReadbackRequest.done && !m_ReadbackRequest.hasError)
			{
                pageProducer.ProcessFeedback(m_ReadbackRequest.GetData<Color32>(), virtualTexture.MaxMip, virtualTexture.tileNum, virtualTexture.pageSize, ref virtualTexture.lruCache, pageRenderer.pageRequests);
                pageRenderer.DrawPageTable(virtualTexture.pageTableTexture, pageProducer);
            }
		}
    }
}