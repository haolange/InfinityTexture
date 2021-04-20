using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture
{
    [BurstCompile]
    internal struct FProcessFeedbackJob : IJob
    {
        internal int maxMip;

        internal int pageSize;

        internal int tileNum;

        internal FLruCache lruCache;

        [ReadOnly]
        internal NativeArray<Color32> readbackDatas;

        [ReadOnly]
        internal NativeArray<FPageTable> pageTables;

        [WriteOnly]
        internal NativeList<FPageRequestInfo> pageRequests;

        public void Execute()
        {
            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                FVirtualTextureUtility.ActivatePage(readbackData.r, readbackData.g, readbackData.b, maxMip, Time.frameCount, pageSize, tileNum, lruCache, pageTables, pageRequests);
            }
        }
    }
}
