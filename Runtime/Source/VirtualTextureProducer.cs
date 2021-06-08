using System;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe class FPageProducer : IDisposable
    {
        public NativeArray<FPageTable> pageTables;
        public NativeHashMap<int2, int3> activePageMap;

        public FPageProducer(in int numTile, in int pageSize, in int maxMipLevel)
        {
            pageTables = new NativeArray<FPageTable>(maxMipLevel, Allocator.Persistent);
            activePageMap = new NativeHashMap<int2, int3>(numTile * numTile, Allocator.Persistent);

            for (int i = 0; i < maxMipLevel; ++i)
            {
                pageTables[i] = new FPageTable(i, pageSize);
            }
        }

        public void ProcessFeedback(ref NativeArray<int4> readbackDatas, in int maxMip, in int tileNum, in int pageSize, FLruCache* lruCache, ref NativeList<FPageLoadInfo> loadRequests)
        {
            FAnalysisFeedbackJob analysisFeedbackJob;
            analysisFeedbackJob.maxMip = maxMip - 1;
            analysisFeedbackJob.tileNum = tileNum;
            analysisFeedbackJob.pageSize = pageSize;
            analysisFeedbackJob.lruCache = lruCache;
            analysisFeedbackJob.pageTables = pageTables;
            analysisFeedbackJob.loadRequests = loadRequests;
            analysisFeedbackJob.frameCount = Time.frameCount;
            analysisFeedbackJob.readbackDatas = readbackDatas;
            analysisFeedbackJob.Run();
        }

        public void Reset()
        {
            for (int i = 0; i < pageTables.Length; ++i)
            {
                FPageTable pageTable = pageTables[i];

                for (int j = 0; j < pageTable.cellCount; ++j)
                {
                    for (int k = 0; k < pageTable.cellCount; k++)
                    {
                        ref FPage page = ref pageTable.pageBuffer[j * pageTable.cellCount + k];
                        InvalidatePage(page.payload.pageCoord);
                    }
                }
            }
            activePageMap.Clear();
        }
        
        public void InvalidatePage(in int2 id)
        {
            if (!activePageMap.TryGetValue(id, out int3 index))
                return;

            FPageTable pageTable = pageTables[index.z];
            ref FPage page = ref pageTable.GetPage(index.x, index.y);

            page.payload.ResetTileIndex();
            activePageMap.Remove(id);
        }
        
        public void Dispose()
        {
            for (int i = 0; i < pageTables.Length; ++i)
            {
                FPageTable pageTable = pageTables[i];
                pageTable.Dispose();
            }
            pageTables.Dispose();
            activePageMap.Dispose();
        }
    }
}
