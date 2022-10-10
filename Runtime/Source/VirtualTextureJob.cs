using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    [BurstCompile]
    internal struct FDecodeFeedbackJob : IJobParallelFor
    {
        [ReadOnly]
        internal int pageSize;

        [ReadOnly]
        internal NativeArray<Color32> encodeDatas;

        [WriteOnly]
        internal NativeArray<int4> decodeDatas;

        public void Execute(int index)
        {
            Color32 x = encodeDatas[index];
            float4 xRaw = new float4((float)x.r / 255.0f, (float)x.g / 255.0f, (float)x.b / 255.0f, x.a);

            float3 x888 = math.floor(xRaw.xyz * 255);
            float High =  math.floor(x888.z / 16);	// x888.z >> 4
            float Low = x888.z - High * 16;		// x888.z & 15
            float2 x1212 = x888.xy + new float2(Low, High) * 256;
            x1212 = math.saturate(x1212 / 4095) * pageSize;

            decodeDatas[index] = new int4((int)x1212.x, (int)x1212.y, x.a, 255);
        }
    }

    [BurstCompile]
    internal unsafe struct FAnalysisFeedbackJob : IJob
    {
        internal int maxMip;

        internal int pageSize;

        internal int tileNum;

        internal int frameCount;

        [NativeDisableUnsafePtrRestriction]
        internal FLruCache* lruCache;

        [ReadOnly]
        internal NativeArray<int4> readbackDatas;

        [ReadOnly]
        internal NativeArray<FPageTable> pageTables;

        internal NativeList<FPageLoadInfo> loadRequests;

        public void Execute()
        {
            int4 prevValue = -1;

            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                int4 readbackData = readbackDatas[i];

                if (readbackData.Equals(prevValue)) //skip same page
                    continue;

                prevValue = readbackData;

                if (readbackData.z > maxMip || readbackData.z < 0 || readbackData.x < 0 || readbackData.y < 0 || readbackData.x >= pageSize || readbackData.y >= pageSize)
                    continue;

                ref FPage page = ref pageTables[readbackData.z].GetPage(readbackData.x, readbackData.y);

                if (page.isNull)
                    continue;

                if (!page.payload.isReady && page.payload.notLoading)
                {
                    page.payload.notLoading = false;
                    loadRequests.AddNoResize(new FPageLoadInfo(readbackData.x, readbackData.y, readbackData.z));
                }

                if (page.payload.isReady && page.payload.activeFrame != frameCount)
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
        internal NativeParallelHashMap<int2, int3>.Enumerator pageEnumerator;

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
        internal NativeList<FPageLoadInfo> loadRequests;

        public void Execute()
        {
            loadRequests.Sort();
        }
    }
}
