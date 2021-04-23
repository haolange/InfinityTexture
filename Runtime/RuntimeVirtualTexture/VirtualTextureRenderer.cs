using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Landscape.ProceduralVirtualTexture
{
	internal class FPageRenderer 
	{
        [SerializeField]
		private int m_Limit = 12;

		private List<FPageRequestInfo> m_PageRequests = new List<FPageRequestInfo>();


        public void AllocateRquestInfo(in int x, in int y, in int mip, ref FPageRequestInfo pageRequest)
		{
            for (int i = 0; i < m_PageRequests.Count; ++i)
            {
                pageRequest = m_PageRequests[i];
                if (pageRequest.pageX == x && pageRequest.pageY == y && pageRequest.mipLevel == mip)
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

                int3 pageUV = new int3(PageRequestInfo.pageX, PageRequestInfo.pageY, PageRequestInfo.mipLevel);
                FPageTable pageTable = pageProducer.pageTables[pageUV.z];
                ref FPage page = ref pageTable.GetPage(pageUV.x, pageUV.y);

                if (page.isNull == true || page.payload.pageRequestInfo.NotEquals(PageRequestInfo)) { continue; }
                page.payload.pageRequestInfo.isNull = true;

                int2 pageCoord = new int2(lruCache.First % tileNum, lruCache.First / tileNum);
                if (lruCache.SetActive(pageCoord.y * tileNum + pageCoord.x))
                {
                    pageProducer.InvalidatePage(pageCoord);
                    PageSystem.DrawMesh(new RectInt(pageCoord.x * tileSize, pageCoord.y * tileSize, tileSize, tileSize), PageRequestInfo);
                }

                page.payload.pageCoord = pageCoord;
                pageProducer.activePageMap.Add(pageCoord, pageUV);
                Debug.Log(lruCache.First);
            }
        }
    }
}
