using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe class FPageProducer : IDisposable
    {
        internal NativeArray<FPageTable> pageTables;
        internal NativeHashMap<int2, int3> activePageMap;

        internal FPageProducer(in int pageSize, in int maxMipLevel)
        {
            pageTables = new NativeArray<FPageTable>(maxMipLevel, Allocator.Persistent);
            activePageMap = new NativeHashMap<int2, int3>(256, Allocator.Persistent);

            for (int i = 0; i < maxMipLevel; ++i)
            {
                pageTables[i] = new FPageTable(i, pageSize);
            }
        }

        public void ProcessFeedbackData(in NativeArray<Color32> readbackDatas, in int maxMipLevel, in int pageSize, in int tileNum, in FLruCache lruCache, in NativeList<FPageRequestInfo> pageRequests)
        {
            for (int i = 0; i < readbackDatas.Length; ++i)
            {
                Color32 readbackData = readbackDatas[i];
                FVirtualTextureUtility.ActivatePage(readbackData.r, readbackData.g, readbackData.b, maxMipLevel, Time.frameCount, pageSize, tileNum, lruCache, pageTables, pageRequests);
            }
        }

        public void InvalidatePage(in int2 mapKey)
        {
            if (!activePageMap.TryGetValue(mapKey, out int3 index)) { return; }

            FPageTable pageTable = pageTables[index.z];
            ref FPage page = ref pageTable.GetPage(index.x, index.y);

            page.payload.ResetTileIndex();
            activePageMap.Remove(mapKey);
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
                        InvalidatePage(page.payload.tileIndex);
                    }
                }
            }
            activePageMap.Clear();
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
