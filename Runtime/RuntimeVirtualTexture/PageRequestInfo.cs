using System;

namespace Landscape.ProceduralVirtualTexture
{
    public struct FPageRequestInfo : IComparable<FPageRequestInfo>
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

        public bool Equals(in FPageRequestInfo target)
        {
            return target.PageX == PageX && target.PageY == PageY && target.MipLevel == MipLevel && target.bNull == bNull;
        }

        public bool NotEquals(in FPageRequestInfo target)
        {
            return target.PageX != PageX || target.PageY != PageY || target.MipLevel != MipLevel && target.bNull != bNull;
        }

        public override bool Equals(object target)
        {
            return Equals((FPageRequestInfo)target);
        }

        public override int GetHashCode()
        {
            return PageX.GetHashCode() + PageY.GetHashCode() + MipLevel.GetHashCode() + (bNull ? 0 : 1);
        }

        public int CompareTo(FPageRequestInfo other)
        {
            return MipLevel.CompareTo(other.MipLevel);
        }
    }
}