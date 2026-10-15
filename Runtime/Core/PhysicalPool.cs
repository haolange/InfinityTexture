using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>一个物理槽：LRU 双链的前后链接，以及当前住在这里的虚拟页（反向映射）。</summary>
    internal struct PhysicalSlot
    {
        public int older;
        public int newer;
        public PageId owner;
    }

    /// <summary>
    /// 物理页池：poolSize x poolSize 个槽，按 LRU 双链排列。
    /// 链头是最旧的槽，链尾是最新的槽。分配取最旧槽，触碰把槽接到最新端。
    /// </summary>
    internal unsafe struct PhysicalPool : IDisposable
    {
        public const int None = -1;

        public readonly int poolSize;
        public int capacity => poolSize * poolSize;
        public int oldest => m_Oldest;
        public int newest => m_Newest;

        [NativeDisableUnsafePtrRestriction]
        PhysicalSlot* m_Slots;
        int m_Oldest;
        int m_Newest;

        public PhysicalPool(int poolSize)
        {
            this.poolSize = poolSize;
            int count = poolSize * poolSize;
            m_Slots = (PhysicalSlot*)UnsafeUtility.Malloc(sizeof(PhysicalSlot) * count, UnsafeUtility.AlignOf<PhysicalSlot>(), Allocator.Persistent);

            for (int i = 0; i < count; ++i)
            {
                m_Slots[i] = new PhysicalSlot
                {
                    older = i - 1,
                    newer = i + 1 < count ? i + 1 : None,
                    owner = PageId.Invalid
                };
            }
            m_Oldest = 0;
            m_Newest = count - 1;
        }

        public ref PhysicalSlot this[int slot] => ref m_Slots[slot];

        /// <summary>槽在物理图上的格子坐标（以槽为单位）。</summary>
        public int2 SlotCoord(int slot) => new int2(slot % poolSize, slot / poolSize);

        /// <summary>命中：把槽移到最新端。</summary>
        public void Touch(int slot)
        {
            if (slot == m_Newest) { return; }
            Unlink(slot);
            LinkAsNewest(slot);
        }

        /// <summary>取最旧的槽给 page，返回槽号；原主人通过 evicted 交还给调用者处理。</summary>
        public int Allocate(in PageId page, out PageId evicted)
        {
            int slot = m_Oldest;
            evicted = m_Slots[slot].owner;
            m_Slots[slot].owner = page;
            Touch(slot);
            return slot;
        }

        void Unlink(int slot)
        {
            ref PhysicalSlot node = ref m_Slots[slot];
            if (node.older != None) { m_Slots[node.older].newer = node.newer; } else { m_Oldest = node.newer; }
            if (node.newer != None) { m_Slots[node.newer].older = node.older; } else { m_Newest = node.older; }
            node.older = None;
            node.newer = None;
        }

        void LinkAsNewest(int slot)
        {
            ref PhysicalSlot node = ref m_Slots[slot];
            node.older = m_Newest;
            node.newer = None;
            if (m_Newest != None) { m_Slots[m_Newest].newer = slot; } else { m_Oldest = slot; }
            m_Newest = slot;
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Slots, Allocator.Persistent);
            m_Slots = null;
        }
    }
}
