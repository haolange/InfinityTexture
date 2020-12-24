namespace Landscape.ProceduralVirtualTexture
{
    public struct FPageRequestInfo
    {
        public int PageX;

        public int PageY;

        public int MipLevel;

        public bool bNull;

        public FPageRequestInfo(int x, int y, int mip, bool IsNull = false)
        {
            PageX = x;
            PageY = y;
            MipLevel = mip;
            bNull = IsNull;
        }

        public bool Equals(FPageRequestInfo obj)
        {
            return obj.PageX == PageX && obj.PageY == PageY && obj.MipLevel == MipLevel;
        }

        public bool NotEquals(FPageRequestInfo obj)
        {
            return obj.PageX != PageX || obj.PageY != PageY || obj.MipLevel != MipLevel;
        }

        public override int GetHashCode()
        {
            return PageX.GetHashCode() + PageY.GetHashCode() + MipLevel.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Equals((FPageRequestInfo)obj);
        }
    }
}