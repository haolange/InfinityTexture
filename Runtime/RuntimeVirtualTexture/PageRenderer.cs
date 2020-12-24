using UnityEngine;
using System.Collections.Generic;

namespace Landscape.ProceduralVirtualTexture
{
	public class FPageRenderer 
	{
        [SerializeField]
		private int m_Limit = 5;

		private List<FPageRequestInfo> m_PendingRequests = new List<FPageRequestInfo>();


        public FPageRequestInfo AllocateRquestInfo(int x, int y, int mip)
		{
			// 是否已经在请求队列中
			foreach(var r in m_PendingRequests)
			{
				if(r.PageX == x && r.PageY == y && r.MipLevel == mip)
					return new FPageRequestInfo(x, y, mip, true);
			}

			// 加入待处理列表
			var request = new FPageRequestInfo(x, y, mip);
			m_PendingRequests.Add(request);

			return request;
		}

        public void UpdatePage(RuntimeVirtualTextureSystem PageSystem, FPageProducer InPageProducer, RuntimeVirtualTexture InPageTexture)
        {
            if (m_PendingRequests.Count <= 0)
                return;

            // 优先处理mipmap等级高的请求
            m_PendingRequests.Sort((x, y) => { return x.MipLevel.CompareTo(y.MipLevel); });

            int count = m_Limit;
            while (count > 0 && m_PendingRequests.Count > 0)
            {
                count--;
                // 将第一个请求从等待队列移到运行队列
                FPageRequestInfo PageRequestInfo = m_PendingRequests[m_PendingRequests.Count - 1];
                m_PendingRequests.RemoveAt(m_PendingRequests.Count - 1);

                // 开始渲染
                RenderPage(PageSystem, InPageProducer, InPageTexture, PageRequestInfo);
            }
        }

        private void RenderPage(RuntimeVirtualTextureSystem PageSystem, FPageProducer InPageProducer, RuntimeVirtualTexture InPageTexture, FPageRequestInfo PageRequestInfo)
        {
            // 找到对应页表
            FPage Page = InPageProducer.PageTable[PageRequestInfo.MipLevel].GetPage(PageRequestInfo.PageX, PageRequestInfo.PageY);


            if (Page == null || Page.Payload.pageRequestInfo.NotEquals(PageRequestInfo))
                return;

			Page.Payload.pageRequestInfo.bNull = true;

            Vector2Int PageCoord = InPageTexture.RequestTile();
            if (InPageTexture.SetActive(PageCoord))
            {
                InPageProducer.InvalidatePage(PageCoord);
                PageSystem.DrawMesh(new RectInt(PageCoord.x * InPageTexture.TileSizePadding, PageCoord.y * InPageTexture.TileSizePadding, InPageTexture.TileSizePadding, InPageTexture.TileSizePadding), PageRequestInfo);
            }

            Page.Payload.TileIndex = PageCoord;
            InPageProducer.ActivePages[PageCoord] = Page;
        }
    }
}
