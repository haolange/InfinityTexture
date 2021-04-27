using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

namespace Landscape.RuntimeVirtualTexture
{
    [BurstCompile]
    internal unsafe struct FProcessFeedbackJob64 : IJob
    {
        internal int maxMip;

        internal int pageSize;

        internal int tileNum;

        internal int frameCount;

        [NativeDisableUnsafePtrRestriction]
        internal FLruCache* lruCache;

        [ReadOnly]
        internal NativeArray<half4> readbackDatas;

        [ReadOnly]
        internal NativeArray<FPageTable> pageTables;

        internal NativeList<FPageRequestInfo> pageRequests;

        public void Execute()
        {
            int3 prevValue = -1;
            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                half4 readbackData = readbackDatas[i];
                int x = (int)(readbackData.x * 255), y = (int)(readbackData.y * 255), mip = (int)(readbackData.z * 255);

                int3 value = new int3(x, y, mip);
                if (value.Equals(prevValue)) //skip same page
                    continue;
                prevValue = value;

                if (mip > maxMip || mip < 0 || x < 0 || y < 0 || x >= pageSize || y >= pageSize)
                    continue;

                ref FPage page = ref pageTables[mip].GetPage(x, y);
                if (page.isNull)
                    continue;

                if (!page.payload.isReady)
                {
                    if (!page.payload.notLoading)
                        continue;
                    page.payload.notLoading = false;
                    pageRequests.AddNoResize(new FPageRequestInfo(x, y, mip));
                }

                if (page.payload.isReady)
                {
                    page.payload.activeFrame = frameCount;
                    lruCache[0].SetActive(page.payload.pageCoord.y * tileNum + page.payload.pageCoord.x);
                }
            }
        }
    }

    [BurstCompile]
    internal unsafe struct FProcessFeedbackJob32 : IJob
    {
        internal int maxMip;

        internal int pageSize;

        internal int tileNum;

        internal int frameCount;

        [NativeDisableUnsafePtrRestriction]
        internal FLruCache* lruCache;

        [ReadOnly]
        internal NativeArray<Color32> readbackDatas;

        [ReadOnly]
        internal NativeArray<FPageTable> pageTables;

        internal NativeList<FPageRequestInfo> pageRequests;

        public void Execute()
        {
            int3 prevValue = -1;
            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                int x = (int)(readbackData.r), y = (int)(readbackData.g), mip = (int)(readbackData.b);

                int3 value = new int3(x, y, mip);
                if (value.Equals(prevValue)) //skip same page
                    continue;
                prevValue = value;

                if (mip > maxMip || mip < 0 || x < 0 || y < 0 || x >= pageSize || y >= pageSize)
                    continue;

                ref FPage page = ref pageTables[mip].GetPage(x, y);
                if (page.isNull)
                    continue;

                if (!page.payload.isReady)
                {
                    if (!page.payload.notLoading)
                        continue;
                    page.payload.notLoading = false;
                    pageRequests.AddNoResize(new FPageRequestInfo(x, y, mip));
                }

                if (page.payload.isReady)
                {
                    page.payload.activeFrame = frameCount;
                    lruCache[0].SetActive(page.payload.pageCoord.y * tileNum + page.payload.pageCoord.x);
                }
            }
        }
    }


    [BurstCompile]
    internal struct FPageDrawInfoBuildJob : IJob
    {
        internal int pageSize;

        internal int frameTime;

        [ReadOnly]
        internal NativeArray<FPageTable> pageTables;

        [WriteOnly]
        internal NativeList<FPageDrawInfo> drawInfos;

        [ReadOnly]
        internal NativeHashMap<int2, int3>.Enumerator pageEnumerator;

        public void Execute()
        {
            while (pageEnumerator.MoveNext())
            {
                var pageCoord = pageEnumerator.Current.Value;
                FPageTable pageTable = pageTables[pageCoord.z];
                ref FPage page = ref pageTable.GetPage(pageCoord.x, pageCoord.y);
                if (page.payload.activeFrame != frameTime) { continue; }

                int2 rectXY = new int2(page.rect.xMin, page.rect.yMin);
                while (rectXY.x < 0)
                {
                    rectXY.x += pageSize;
                }
                while (rectXY.y < 0)
                {
                    rectXY.y += pageSize;
                }

                FPageDrawInfo drawInfo;
                drawInfo.mip = page.mipLevel;
                drawInfo.rect = new FRect(rectXY.x, rectXY.y, page.rect.width, page.rect.height);
                drawInfo.drawPos = new float2((float)page.payload.pageCoord.x / 255, (float)page.payload.pageCoord.y / 255);
                drawInfos.Add(drawInfo);
            }
        }
    }

    [BurstCompile]
    internal struct FPageDrawInfoSortJob : IJob
    {
        internal NativeList<FPageDrawInfo> drawInfos;

        public void Execute()
        {
            drawInfos.Sort();
        }
    }

    [BurstCompile]
    internal struct FPageTableInfoBuildJob : IJobParallelFor
    {
        internal int pageSize;

        [ReadOnly]
        internal NativeList<FPageDrawInfo> drawInfos;

        [WriteOnly]
        internal NativeArray<FPageTableInfo> pageTableInfos;

        public void Execute(int i)
        {
            FPageTableInfo pageInfo;
            pageInfo.pageData = new float4(drawInfos[i].drawPos.x, drawInfos[i].drawPos.y, drawInfos[i].mip / 255f, 0);
            pageInfo.matrix_M = float4x4.TRS(new float3(drawInfos[i].rect.x / pageSize, drawInfos[i].rect.y / pageSize, 0), quaternion.identity, drawInfos[i].rect.width / pageSize);
            pageTableInfos[i] = pageInfo;
        }
    }

    [BurstCompile]
    internal struct FPageRequestInfoSortJob : IJob
    {
        internal NativeList<FPageRequestInfo> pageRequests;

        public void Execute()
        {
            pageRequests.Sort();
        }
    }
}
