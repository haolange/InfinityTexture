using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    [BurstCompile]
    internal unsafe struct FProcessFeedbackJob : IJob
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
            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                FVirtualTextureUtility.ActivatePage(readbackData.r, readbackData.g, readbackData.b, maxMip, frameCount, tileNum, pageSize, ref lruCache[0], ref pageTables, ref pageRequests);
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
