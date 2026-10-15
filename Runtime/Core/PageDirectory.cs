using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    internal enum EPageState : byte
    {
        Free,
        Queued,
        Resident
    }

    /// <summary>页目录里的一格。slot 只在 Resident 时有意义。</summary>
    internal struct PageEntry
    {
        public EPageState state;
        public ushort slot;
    }

    /// <summary>
    /// CPU 侧每级页目录：记录每个虚拟页处于空闲、排队还是已驻留，驻留时在哪个物理槽。
    /// 所有级平铺在一块原生内存里，第 k 级有 (pageCount >> k)^2 格。
    /// </summary>
    internal unsafe struct PageDirectory : IDisposable
    {
        public readonly int pageCount;
        public readonly int mipCount;
        public int maxMip => mipCount - 1;

        [NativeDisableUnsafePtrRestriction]
        PageEntry* m_Entries;
        fixed int m_LevelOffsets[PageId.MaxMipCount + 1];

        /// <param name="pageCount">第 0 级每边页数，必须是 2 的幂。</param>
        public PageDirectory(int pageCount)
        {
            this.pageCount = pageCount;
            this.mipCount = math.tzcnt(pageCount) + 1;

            int total = 0;
            for (int mip = 0; mip < mipCount; ++mip)
            {
                m_LevelOffsets[mip] = total;
                int side = pageCount >> mip;
                total += side * side;
            }
            m_LevelOffsets[mipCount] = total;

            m_Entries = (PageEntry*)UnsafeUtility.Malloc(sizeof(PageEntry) * total, UnsafeUtility.AlignOf<PageEntry>(), Allocator.Persistent);
            UnsafeUtility.MemClear(m_Entries, sizeof(PageEntry) * total);
        }

        public int entryCount => m_LevelOffsets[mipCount];

        public int PagesAtMip(int mip) => pageCount >> mip;

        public bool Contains(in PageId page)
        {
            if (!page.isValid || page.mip >= mipCount) { return false; }
            int side = PagesAtMip(page.mip);
            return page.x < side && page.y < side;
        }

        public ref PageEntry Get(in PageId page)
        {
            int side = PagesAtMip(page.mip);
            return ref m_Entries[m_LevelOffsets[page.mip] + page.y * side + page.x];
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Entries, Allocator.Persistent);
            m_Entries = null;
        }
    }
}
