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

        public FPageProducer(in int pageSize, in int maxMipLevel)
        {
            pageTables = new NativeArray<FPageTable>(maxMipLevel, Allocator.Persistent);
            activePageMap = new NativeHashMap<int2, int3>(256, Allocator.Persistent);

            for (int i = 0; i < maxMipLevel; ++i)
            {
                pageTables[i] = new FPageTable(i, pageSize);
            }

            //ActivatePage(0, 0, pageTexture.MaxMipLevel);
        }

        public void ProcessFeedback(in NativeArray<Color32> readbackDatas, in int maxMip, in int tileNum, in int pageSize, FLruCache* lruCache, in NativeList<FPageRequestInfo> pageRequests)
        {
            FProcessFeedbackJob processFeedbackJob;
            processFeedbackJob.maxMip = maxMip - 1;
            processFeedbackJob.tileNum = tileNum;
            processFeedbackJob.pageSize = pageSize;
            processFeedbackJob.lruCache = lruCache;
            processFeedbackJob.pageTables = pageTables;
            processFeedbackJob.pageRequests = pageRequests;
            processFeedbackJob.frameCount = Time.frameCount;
            processFeedbackJob.readbackDatas = readbackDatas;
            processFeedbackJob.Run();

            /*for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                FVirtualTextureUtility.ActivatePage(readbackData.r, readbackData.g, readbackData.b, maxMip - 1, Time.frameCount, tileNum, pageSize, ref lruCache[0], pageTables, pageRequests);
            }*/
        }
        public void ProcessFeedbackV2(in NativeArray<Color32> readbackDatas, in int maxMip, in int tileNum, in int pageSize, FLruCache* lruCache, in NativeList<FPageRequestInfo> pageRequests)
        {
            PreprocessFeedbackJob preprocessFeedbackJob;
            preprocessFeedbackJob.readbackDatas = readbackDatas;
            NativeArray<int> processedDataArray = new NativeArray<int>(readbackDatas.Length, Allocator.TempJob);
            preprocessFeedbackJob.processedDatas = processedDataArray;
            var phase1 = preprocessFeedbackJob.Schedule(readbackDatas.Length, 200);
            var phase2 = NativeSortExtension.Sort(processedDataArray, phase1);
            NativeArray<int> unifiedCount = new NativeArray<int>(1, Allocator.TempJob);
            UnifyFeedbackJob unifyFeedbackJob;
            unifyFeedbackJob.processedDatas = processedDataArray;
            unifyFeedbackJob.unifiedCount = unifiedCount;
            var phase3 = unifyFeedbackJob.Schedule(phase2);

            FProcessFeedbackJobV2 processFeedbackJob;
            processFeedbackJob.maxMip = maxMip - 1;
            processFeedbackJob.tileNum = tileNum;
            processFeedbackJob.pageSize = pageSize;
            processFeedbackJob.lruCache = lruCache;
            processFeedbackJob.pageTables = pageTables;
            processFeedbackJob.pageRequests = pageRequests;
            processFeedbackJob.frameCount = Time.frameCount;
            processFeedbackJob.processedDatas = processedDataArray;
            processFeedbackJob.processedDatasCount = unifiedCount;
            var phase4 = processFeedbackJob.Schedule(phase3);
            phase4.Complete();

            /*for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                FVirtualTextureUtility.ActivatePage(readbackData.r, readbackData.g, readbackData.b, maxMip - 1, Time.frameCount, tileNum, pageSize, ref lruCache[0], pageTables, pageRequests);
            }*/
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
