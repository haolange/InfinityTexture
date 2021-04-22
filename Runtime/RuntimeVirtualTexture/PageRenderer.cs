using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Landscape.ProceduralVirtualTexture
{
	public class FPageRenderer 
	{
        [SerializeField]
		private int m_Limit = 12;

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

        public void DrawPageColor(RuntimeVirtualTextureSystem PageSystem, FPageProducer pageProducer, ref FLruCache lruCache, in int tileNum, in int tileSize)
        {
            if (m_PageRequests.Count <= 0) { return; }

            m_PageRequests.Sort();

            int count = m_Limit;
            while (count > 0 && m_PageRequests.Count > 0)
            {
                count--;
                FPageRequestInfo PageRequestInfo = m_PageRequests[m_PageRequests.Count - 1];
                m_PageRequests.RemoveAt(m_PageRequests.Count - 1);

                int3 pageUV = new int3(PageRequestInfo.PageX, PageRequestInfo.PageY, PageRequestInfo.MipLevel);
                PageTable pageTable = pageProducer.PageTable[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.bNull == true || page.Payload.pageRequestInfo.NotEquals(PageRequestInfo)) { continue; }
                page.Payload.pageRequestInfo.bNull = true;

                Vector2Int PageCoord = new Vector2Int(lruCache.First % tileNum, lruCache.First / tileNum);
                if (lruCache.SetActive(PageCoord.y * tileNum + PageCoord.x))
                {
                    pageProducer.InvalidatePage(PageCoord);
                    PageSystem.DrawMesh(new RectInt(PageCoord.x * tileSize, PageCoord.y * tileSize, tileSize, tileSize), PageRequestInfo);
                }

                page.Payload.TileIndex = PageCoord;
                pageProducer.ActivePages.Add(PageCoord, pageUV);
                //Debug.Log(lruCache.First);
            }
        }
    }
}
