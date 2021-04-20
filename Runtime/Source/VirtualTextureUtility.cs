using Unity.Burst;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture
{
    [BurstCompile]
    internal static class FVirtualTextureUtility
    {
        [BurstCompile]
        public static void AllocateRquestInfo(in int x, in int y, in int mip, ref FPageRequestInfo pageRequest, in NativeList<FPageRequestInfo> pageRequests)
        {
            for (int i = 0; i < pageRequests.Length; ++i)
            {
                pageRequest = pageRequests[i];
                if (pageRequest.pageX == x && pageRequest.pageY == y && pageRequest.mipLevel == mip)
                {
                    pageRequest = new FPageRequestInfo(x, y, mip, true);
                    return;
                }
            }

            pageRequest = new FPageRequestInfo(x, y, mip);
            pageRequests.Add(pageRequest);
            return;
        }

        [BurstCompile]
        public static void LoadPage(in int x, in int y, ref FPage page, in NativeList<FPageRequestInfo> pageRequests)
        {
            if (page.isNull == true) { return; }
            if (page.payload.pageRequestInfo.isNull == false) { return; }
            AllocateRquestInfo(x, y, page.mipLevel, ref page.payload.pageRequestInfo, pageRequests);
        }

        [BurstCompile]
        public static void ActivatePage(in int x, in int y, in int mip, in int maxMip, in int frameCount, in int pageSize, in int tileNum, in FLruCache lruCache, in NativeArray<FPageTable> pageTables, in NativeList<FPageRequestInfo> pageRequests)
        {
            if (mip > maxMip || mip < 0 || x < 0 || y < 0 || x >= pageSize || y >= pageSize) { return; }

            ref FPage page = ref pageTables[mip].GetPage(x, y);
            if (page.isNull == true) { return; }

            if (!page.payload.isReady)
            {
                LoadPage(x, y, ref page, pageRequests);
            }

            if (page.payload.isReady)
            {
                page.payload.activeFrame = frameCount;
                lruCache.SetActive(page.payload.tileIndex.y * tileNum + page.payload.tileIndex.x);
                return;
            }

            return;
        }
    }
}
