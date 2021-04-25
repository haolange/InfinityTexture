using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe class FeedbackReader
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

		public void ProcessAndDrawPageTable(FPageProducer pageProducer, FPageRenderer pageRenderer, VirtualTextureAsset virtualTexture)
		{
			if(m_ReadbackRequest.done && !m_ReadbackRequest.hasError)
			{
                pageProducer.ProcessFeedback(m_ReadbackRequest.GetData<Color32>(), virtualTexture.NumMip, virtualTexture.tileNum, virtualTexture.pageSize, virtualTexture.lruCache, pageRenderer.pageRequests);
                pageRenderer.DrawPageTable(pageProducer, virtualTexture);
            }
		}
    }
}