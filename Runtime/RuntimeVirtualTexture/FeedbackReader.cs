using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.ProceduralVirtualTexture
{
    public class FeedbackReader
	{
		private AsyncGPUReadbackRequest m_ReadbackRequest;


        public bool bReady
        {
            get
            {
                return m_ReadbackRequest.done || m_ReadbackRequest.hasError;
            }
        }

		public void RequestReadback(RenderTexture texture)
		{
            if (!m_ReadbackRequest.done && !m_ReadbackRequest.hasError)
                return;

            m_ReadbackRequest = AsyncGPUReadback.Request(texture);
        }

		public void GetReadbackData(FPageProducer InPageProducer)
		{
			if(m_ReadbackRequest.done && !m_ReadbackRequest.hasError)
			{
                InPageProducer.ProcessFeedback(m_ReadbackRequest.GetData<Color32>());
            }
		}
    }
}