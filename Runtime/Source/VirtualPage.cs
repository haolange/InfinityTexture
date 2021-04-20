using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    internal struct FPage
    {
        internal RectInt Rect;
        internal int mipLevel;
        internal bool isNull;
        internal FPagePayload pagePayload;


        public FPage(int x, int y, int width, int height, int mipLevel, bool isNull = false)
        {
            this.Rect = new RectInt(x, y, width, height);
            this.mipLevel = mipLevel;
            this.isNull = isNull;
            this.pagePayload = new FPagePayload();
            pagePayload.tileIndex = new int2(-1, -1);
            pagePayload.pageRequestInfo = new FPageRequestInfo(0, 0, 0, true);
        }

        public bool Equals(in FPage Target)
        {
            return Rect.Equals(Target.Rect) && pagePayload.Equals(Target.pagePayload) && mipLevel.Equals(Target.mipLevel) && isNull.Equals(Target.isNull);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPage)obj);
        }

        public override int GetHashCode()
        {
            return Rect.GetHashCode() + pagePayload.GetHashCode() + mipLevel.GetHashCode() + (isNull ? 0 : 1);
        }
    }

    internal struct FPagePayload
    {
        internal int2 tileIndex;
        internal int activeFrame;
        internal FPageRequestInfo pageRequestInfo;
        private static int2 s_InvalidTileIndex = new int2(-1, -1);
        internal bool IsReady { get { return (!tileIndex.Equals(s_InvalidTileIndex)); } }


        public void ResetTileIndex()
        {
            tileIndex = s_InvalidTileIndex;
        }

        public bool Equals(in FPagePayload target)
        {
            return IsReady.Equals(target.IsReady) && activeFrame.Equals(target.activeFrame) && tileIndex.Equals(target.tileIndex) && pageRequestInfo.Equals(target.pageRequestInfo);
        }

        public override bool Equals(object target)
        {
            return Equals((FPagePayload)target);
        }

        public override int GetHashCode()
        {
            return tileIndex.GetHashCode() + activeFrame.GetHashCode() + pageRequestInfo.GetHashCode() + (IsReady ? 0 : 1);
        }
    }

    internal struct FPageRequestInfo
    {
        internal int pageX;

        internal int pageY;

        internal int mipLevel;

        internal bool isNull;


        public FPageRequestInfo(in int pageX, in int pageY, in int mipLevel, in bool isNull = false)
        {
            this.pageX = pageX;
            this.pageY = pageY;
            this.isNull = isNull;
            this.mipLevel = mipLevel;
        }

        public bool Equals(in FPageRequestInfo obj)
        {
            return obj.pageX == pageX && obj.pageY == pageY && obj.mipLevel == mipLevel && obj.isNull == isNull;
        }

        public bool NotEquals(FPageRequestInfo obj)
        {
            return obj.pageX != pageX || obj.pageY != pageY || obj.mipLevel != mipLevel && obj.isNull != isNull;
        }

        public override bool Equals(object obj)
        {
            return Equals((FPageRequestInfo)obj);
        }

        public override int GetHashCode()
        {
            return pageX.GetHashCode() + pageY.GetHashCode() + mipLevel.GetHashCode() + (isNull ? 0 : 1);
        }
    }

    internal unsafe struct FPageTable : IDisposable
    {
        public int mipLevel;
        public int cellSize;
        public int cellCount;
        public FPage* pageBuffer;


        public FPageTable(in int mipLevel, in int tableSize)
        {
            this.mipLevel = mipLevel;
            this.cellSize = (int)math.pow(2, mipLevel);
            this.cellCount = tableSize / cellSize;
            this.pageBuffer = (FPage*)UnsafeUtility.Malloc(cellCount * cellCount, 64, Allocator.Persistent);

            for (int i = 0; i < cellCount; i++)
            {
                for (int j = 0; j < cellCount; j++)
                {
                    this.pageBuffer[i * cellCount + j] = new FPage(i * cellSize, j * cellSize, cellSize, cellSize, mipLevel);
                }
            }
        }

        public ref FPage GetPage(in int x, in int y)
        {
            int2 uv = new int2((x / cellSize) % cellCount, (y / cellSize) % cellCount);
            return ref pageBuffer[uv.x * cellCount + uv.y];
        }

        public void Dispose()
        {
            UnsafeUtility.Free((void*)pageBuffer, Allocator.Persistent);
        }
    }
}
