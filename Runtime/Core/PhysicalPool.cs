using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    internal unsafe struct PhysicalPool : IDisposable
    {
        public readonly int poolSize;
        public int capacity => poolSize * poolSize;

        LruOrder m_Order;

        [NativeDisableUnsafePtrRestriction]
        PageId* m_Owners;

        public PhysicalPool(int poolSize)
        {
            this.poolSize = poolSize;
            int count = poolSize * poolSize;
            m_Order = new LruOrder(count);
            m_Owners = (PageId*)UnsafeUtility.Malloc(sizeof(PageId) * count, UnsafeUtility.AlignOf<PageId>(), Allocator.Persistent);
            UnsafeUtility.MemClear(m_Owners, sizeof(PageId) * count);
        }

        public int2 SlotCoord(int slot) => new int2(slot % poolSize, slot / poolSize);

        public PageId SlotOwner(int slot) => m_Owners[slot];

        public void Touch(int slot) => m_Order.Touch(slot);

        public int Allocate(ref PageDirectory directory, in PageId page, out PageId evicted)
        {
            int slot = m_Order.TakeOldest();
            evicted = m_Owners[slot];
            m_Owners[slot] = page;

            if (evicted.isValid) { directory.Get(evicted) = new PageEntry { state = EPageState.Free, slot = 0 }; }
            directory.Get(page) = new PageEntry { state = EPageState.Resident, slot = (ushort)slot };
            return slot;
        }

        public void Dispose()
        {
            m_Order.Dispose();
            UnsafeUtility.Free(m_Owners, Allocator.Persistent);
            m_Owners = null;
        }
    }
}
