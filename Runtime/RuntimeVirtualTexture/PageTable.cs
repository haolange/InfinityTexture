using Unity.Mathematics;
using UnityEngine;

namespace Landscape.ProceduralVirtualTexture
{
    public struct FPage
    {
        public RectInt Rect;

        public FPagePayload Payload;

        public int MipLevel;

        public bool bNull;


        public FPage(int x, int y, int width, int height, int mip, bool IsNull = false)
        {
            Rect = new RectInt(x, y, width, height);
            MipLevel = mip;

            Payload = new FPagePayload();
            Payload.TileIndex = new Vector2Int(-1, -1);
            Payload.pageRequestInfo = new FPageRequestInfo(0, 0, 0, true);

            bNull = IsNull;
        }

        public bool Equals(in FPage Target)
        {
            return Rect.Equals(Target.Rect) && Payload.Equals(Target.Payload) && MipLevel.Equals(Target.MipLevel) && bNull.Equals(Target.bNull);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPage)obj);
        }

        public override int GetHashCode()
        {
            return Rect.GetHashCode() + Payload.GetHashCode() + MipLevel.GetHashCode() + (bNull ? 0 : 1);
        }
    }

    public class PageTable
    {
        public int mipLevel;
        public int cellSize;
        public int cellCount;
        public FPage[] pageBuffer;


        public PageTable(int mipLevel, int tableSize)
        {
            this.mipLevel = mipLevel;
            this.cellSize = (int)Mathf.Pow(2, mipLevel);
            this.cellCount = tableSize / cellSize;
            this.pageBuffer = new FPage[cellCount * cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                for(int j = 0; j < cellCount; j++)
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
    }
}
