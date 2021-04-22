using UnityEngine;
using UnityEngine.Rendering;

namespace Landscape.ProceduralVirtualTexture
{
    public class FeedbackReader
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

		public void ProcessFeedback(FPageProducer InPageProducer)
		{
			if(m_ReadbackRequest.done && !m_ReadbackRequest.hasError)
			{
                InPageProducer.ProcessFeedback(m_ReadbackRequest.GetData<Color32>());
                InPageProducer.DrawPageTable();
            }
		}
    }
}