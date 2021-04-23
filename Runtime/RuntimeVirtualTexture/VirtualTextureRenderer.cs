using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using System;

namespace Landscape.ProceduralVirtualTexture
{
	internal class FPageRenderer : IDisposable
    {
        [SerializeField]
		private int m_Limit;
		internal NativeList<FPageRequestInfo> pageRequests;

        public FPageRenderer(in int pageSize, in int limit = 12)
        {
            this.m_Limit = limit;
            this.pageRequests = new NativeList<FPageRequestInfo>(pageSize, Allocator.Persistent);
        }

        public void DrawPageColor(RuntimeVirtualTextureSystem PageSystem, FPageProducer pageProducer, ref FLruCache lruCache, in int tileNum, in int tileSize)
        {
            if (pageRequests.Length <= 0) { return; }

            pageRequests.Sort();

            int count = m_Limit;
            while (count > 0 && pageRequests.Length > 0)
            {
                count--;
                FPageRequestInfo PageRequestInfo = pageRequests[pageRequests.Length - 1];
                pageRequests.RemoveAt(pageRequests.Length - 1);

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

        public void Dispose()
        {
            pageRequests.Dispose();
        }
    }
}
