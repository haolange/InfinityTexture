using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Landscape.ProceduralVirtualTexture
{
	public class FPageRenderer 
	{
        [SerializeField]
		private int m_Limit = 5;

		private List<FPageRequestInfo> m_PageRequests = new List<FPageRequestInfo>();


        public void AllocateRquestInfo(in int x, in int y, in int mip, ref FPageRequestInfo pageRequest)
		{
            for (int i = 0; i < m_PageRequests.Count; ++i)
            {
                pageRequest = m_PageRequests[i];
                if (pageRequest.PageX == x && pageRequest.PageY == y && pageRequest.MipLevel == mip)
                {
                    pageRequest = new FPageRequestInfo(x, y, mip, true);
                    return;
                }
            }

            pageRequest = new FPageRequestInfo(x, y, mip);
            m_PageRequests.Add(pageRequest);
        }

        public void UpdatePage(RuntimeVirtualTextureSystem PageSystem, FPageProducer InPageProducer, RuntimeVirtualTexture InPageTexture)
        {
            if (m_PageRequests.Count <= 0)
                return;

            // 优先处理mipmap等级高的请求
            m_PageRequests.Sort();

            int count = m_Limit;
            while (count > 0 && m_PageRequests.Count > 0)
            {
                count--;
                // 将第一个请求从等待队列移到运行队列
                FPageRequestInfo PageRequestInfo = m_PageRequests[m_PageRequests.Count - 1];
                m_PageRequests.RemoveAt(m_PageRequests.Count - 1);

                // 开始渲染
                RenderPage(PageSystem, InPageProducer, InPageTexture, PageRequestInfo);
            }
        }

        private void RenderPage(RuntimeVirtualTextureSystem PageSystem, FPageProducer InPageProducer, RuntimeVirtualTexture InPageTexture, in FPageRequestInfo PageRequestInfo)
        {
            // 找到对应页表
            int3 UVW = new int3(PageRequestInfo.PageX, PageRequestInfo.PageY, PageRequestInfo.MipLevel);
            PageTable pageTable = InPageProducer.PageTable[UVW.z];
            ref FPage page = ref pageTable.GetPage(UVW.x, UVW.y);

            if (page.bNull == true || page.Payload.pageRequestInfo.NotEquals(PageRequestInfo))
                return;

            page.Payload.pageRequestInfo.bNull = true;

            Vector2Int PageCoord = InPageTexture.RequestTile();
            if (InPageTexture.SetActive(PageCoord))
            {
                InPageProducer.InvalidatePage(PageCoord);
                PageSystem.DrawMesh(new RectInt(PageCoord.x * InPageTexture.TileSizePadding, PageCoord.y * InPageTexture.TileSizePadding, InPageTexture.TileSizePadding, InPageTexture.TileSizePadding), PageRequestInfo);
            }

            page.Payload.TileIndex = PageCoord;
            InPageProducer.ActivePages.Add(PageCoord, UVW);
        }
    }
}
