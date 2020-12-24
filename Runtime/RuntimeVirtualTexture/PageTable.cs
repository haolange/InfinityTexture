using UnityEngine;

namespace Landscape.ProceduralVirtualTexture
{
    public class FPage
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
    }

    public class PageTable
    {
        public FPage[,] PageBuffer { get; set; }

        public int MipLevel { get; }

        public Vector2Int pageOffset;
        public int NodeCellCount;
        public int PerCellSize;

        public PageTable(int mip, int tableSize)
        {
            pageOffset = Vector2Int.zero;
            MipLevel = mip;
            PerCellSize = (int)Mathf.Pow(2, mip);
            NodeCellCount = tableSize / PerCellSize;
            PageBuffer = new FPage[NodeCellCount, NodeCellCount];
            for (int i = 0; i < NodeCellCount; i++)
            {
                for(int j = 0; j < NodeCellCount; j++)
                {
                    PageBuffer[i,j] = new FPage(i * PerCellSize, j * PerCellSize, PerCellSize, PerCellSize, MipLevel);
                }
            }
        }

        // 取x/y/mip完全一致的node，没有就返回null
        public FPage GetPage(int x, int y)
        {
            x /= PerCellSize;
            y /= PerCellSize;

            x = (x + pageOffset.x) % NodeCellCount;
            y = (y + pageOffset.y) % NodeCellCount;

            return PageBuffer[x, y];
        }
    }
}
