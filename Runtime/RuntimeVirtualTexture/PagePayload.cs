using UnityEngine;

namespace Landscape.ProceduralVirtualTexture
{
    public struct FPagePayload
    {
		private static Vector2Int s_InvalidTileIndex = new Vector2Int(-1, -1);

		public Vector2Int TileIndex;

        public int ActiveFrame;

		public FPageRequestInfo pageRequestInfo;

		public bool IsReady { get { return (TileIndex != s_InvalidTileIndex); } }


        public void ResetTileIndex()
        {
			TileIndex = s_InvalidTileIndex;
        }

        public bool Equals(in FPagePayload Target)
        {
            return IsReady.Equals(Target.IsReady) && ActiveFrame.Equals(Target.ActiveFrame) && TileIndex.Equals(Target.TileIndex) && pageRequestInfo.Equals(Target.pageRequestInfo);
        }

        public override bool Equals(object obj)
        {
            return Equals((FPagePayload)obj);
        }

        public override int GetHashCode()
        {
            return TileIndex.GetHashCode() + ActiveFrame.GetHashCode() + pageRequestInfo.GetHashCode() + (IsReady ? 0 : 1);
        }
    }
}
