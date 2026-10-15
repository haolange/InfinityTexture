using System;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 虚拟页地址：第 mip 级上的页坐标 (x, y)。
    /// 打包成一个 uint，反馈图逐像素直接写这个值；0 表示"没有请求"。
    /// 位布局：[31] 有效位 | [30:26] mip | [25:13] y | [12:0] x
    /// </summary>
    internal readonly struct PageId : IEquatable<PageId>
    {
        public const int MaxMipCount = 16;
        public const int MaxPageCount = 1 << 13;

        const uint ValidBit = 1u << 31;
        const uint CoordMask = MaxPageCount - 1;

        public readonly uint packed;

        public PageId(uint packed) { this.packed = packed; }

        public PageId(int x, int y, int mip)
        {
            packed = ValidBit | (uint)mip << 26 | (uint)y << 13 | (uint)x;
        }

        public static PageId Invalid => default;

        public bool isValid => (packed & ValidBit) != 0;
        public int x => (int)(packed & CoordMask);
        public int y => (int)((packed >> 13) & CoordMask);
        public int mip => (int)((packed >> 26) & 0x1F);

        /// <summary>上一级（更粗）覆盖本页的那一页。</summary>
        public PageId parent => new PageId(x >> 1, y >> 1, mip + 1);

        public bool Equals(PageId other) => packed == other.packed;
        public override bool Equals(object obj) => obj is PageId other && Equals(other);
        public override int GetHashCode() => (int)packed;
        public override string ToString() => isValid ? $"(mip {mip}: {x}, {y})" : "(invalid)";
    }
}
