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

    internal struct PageEntry
    {
        public EPageState state;
        public ushort slot;
    }

    internal unsafe struct PageDirectory : IDisposable
    {
        public readonly int pageCount;
        public readonly int mipCount;
        public int maxMip => mipCount - 1;

        [NativeDisableUnsafePtrRestriction]
        PageEntry* m_Entries;
        fixed int m_LevelStart[PageId.MaxMipCount + 1];

        public PageDirectory(int pageCount)
        {
            this.pageCount = pageCount;
            this.mipCount = math.tzcnt(pageCount) + 1;

            int total = 0;
            for (int mip = 0; mip < mipCount; ++mip)
            {
                m_LevelStart[mip] = total;
                int side = pageCount >> mip;
                total += side * side;
            }
            m_LevelStart[mipCount] = total;

            m_Entries = (PageEntry*)UnsafeUtility.Malloc(sizeof(PageEntry) * total, UnsafeUtility.AlignOf<PageEntry>(), Allocator.Persistent);
            UnsafeUtility.MemClear(m_Entries, sizeof(PageEntry) * total);
        }

        public int entryCount => m_LevelStart[mipCount];

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
            return ref m_Entries[m_LevelStart[page.mip] + page.y * side + page.x];
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Entries, Allocator.Persistent);
            m_Entries = null;
        }
    }
}
