using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 物理 tile（槽）从最久未用排到刚用过；调用方给出编号，所以这里不查找。
    /// Touch 只把一块移到刚用过的一端，不改变其它 tile 的相对顺序；TakeOldest 拿到的是最久未用。
    /// </summary>
    internal unsafe struct LruOrder : IDisposable
    {
        public const int None = -1;

        struct Link
        {
            public int older;
            public int newer;
        }

        public readonly int capacity;
        public int oldest => m_Oldest;
        public int newest => m_Newest;

        [NativeDisableUnsafePtrRestriction]
        Link* m_Links;
        int m_Oldest;
        int m_Newest;

        public LruOrder(int capacity)
        {
            this.capacity = capacity;
            m_Links = (Link*)UnsafeUtility.Malloc(sizeof(Link) * capacity, UnsafeUtility.AlignOf<Link>(), Allocator.Persistent);

            for (int i = 0; i < capacity; ++i)
            {
                m_Links[i] = new Link
                {
                    older = i - 1,
                    newer = i + 1 < capacity ? i + 1 : None
                };
            }
            m_Oldest = 0;
            m_Newest = capacity - 1;
        }

        public int Older(int slot) => m_Links[slot].older;

        public int Newer(int slot) => m_Links[slot].newer;

        public void Touch(int slot)
        {
            if (slot == m_Newest) { return; }

            ref Link link = ref m_Links[slot];
            if (link.older == None) { m_Oldest = link.newer; } else { m_Links[link.older].newer = link.newer; }
            m_Links[link.newer].older = link.older;

            link.older = m_Newest;
            link.newer = None;
            m_Links[m_Newest].newer = slot;
            m_Newest = slot;
        }

        public int TakeOldest()
        {
            int slot = m_Oldest;
            Touch(slot);
            return slot;
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Links, Allocator.Persistent);
            m_Links = null;
        }
    }
}
