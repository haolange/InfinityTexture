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

        public void ProcessFeedback(in NativeArray<byte> readbackDatas, FeedbackBits bits, in int maxMip, in int tileNum, in int pageSize, FLruCache* lruCache, NativeList<FPageRequestInfo> pageRequests)
        {
            if(bits == FeedbackBits.B16)
            {
                FProcessFeedbackJob64 processFeedbackJob;
                processFeedbackJob.maxMip = maxMip - 1;
                processFeedbackJob.tileNum = tileNum;
                processFeedbackJob.pageSize = pageSize;
                processFeedbackJob.lruCache = lruCache;
                processFeedbackJob.pageTables = pageTables;
                processFeedbackJob.pageRequests = pageRequests;
                processFeedbackJob.frameCount = Time.frameCount;
                processFeedbackJob.readbackDatas = readbackDatas.Reinterpret<half4>(1);
                processFeedbackJob.Run();
            }
            else
            {
                FProcessFeedbackJob32 processFeedbackJob;
                processFeedbackJob.maxMip = maxMip - 1;
                processFeedbackJob.tileNum = tileNum;
                processFeedbackJob.pageSize = pageSize;
                processFeedbackJob.lruCache = lruCache;
                processFeedbackJob.pageTables = pageTables;
                processFeedbackJob.pageRequests = pageRequests;
                processFeedbackJob.frameCount = Time.frameCount;
                processFeedbackJob.readbackDatas = readbackDatas.Reinterpret<Color32>(1);
                processFeedbackJob.Run();
            }

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
