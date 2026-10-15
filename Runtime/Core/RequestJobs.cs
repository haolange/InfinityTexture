using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>一页及它在本轮反馈里被看到的权重（像素数，祖先累加子页）。</summary>
    internal struct PageRequest
    {
        public PageId page;
        public int weight;

        public PageRequest(PageId page, int weight)
        {
            this.page = page;
            this.weight = weight;
        }
    }

    /// <summary>权重高的在前；同权重时粗页（mip 大）在前，所以祖先总排在子页前面。</summary>
    internal struct RequestPriority : IComparer<PageRequest>
    {
        public int Compare(PageRequest a, PageRequest b)
        {
            if (a.weight != b.weight) { return b.weight.CompareTo(a.weight); }
            if (a.page.mip != b.page.mip) { return b.page.mip.CompareTo(a.page.mip); }
            return a.page.packed.CompareTo(b.page.packed);
        }
    }

    internal struct QueuedPage
    {
        public PageId page;
        public uint lastRequestRound;
    }

    /// <summary>烘焙单：把 page 烘焙进物理槽 slot。</summary>
    internal struct BakeOrder
    {
        public PageId page;
        public int slot;
    }

    internal struct ResolveSettings
    {
        public uint round;
        public int bakeBudget;
        public int queueCapacity;
        public int queueTimeout;
    }

    /// <summary>反馈像素 → PageId。越界或空像素写 0。</summary>
    [BurstCompile]
    internal struct FeedbackDecodeJob : IJobParallelFor
    {
        public int pageCount;
        public int mipCount;

        [ReadOnly] public NativeArray<uint> pixels;
        [WriteOnly] public NativeArray<uint> pages;

        public void Execute(int index)
        {
            var page = new PageId(pixels[index]);
            bool inRange = page.isValid && page.mip < mipCount && page.x < (pageCount >> page.mip) && page.y < (pageCount >> page.mip);
            pages[index] = inRange ? page.packed : 0;
        }
    }

    /// <summary>排序后把相同的 PageId 合并成一条请求，权重为出现次数。</summary>
    [BurstCompile]
    internal struct RequestCollectJob : IJob
    {
        public NativeArray<uint> pages;
        public NativeList<PageRequest> requests;

        public void Execute()
        {
            requests.Clear();
            pages.Sort();

            int i = 0;
            while (i < pages.Length && pages[i] == 0) { ++i; }
            while (i < pages.Length)
            {
                uint packed = pages[i];
                int start = i;
                while (i < pages.Length && pages[i] == packed) { ++i; }
                requests.Add(new PageRequest(new PageId(packed), i - start));
            }
        }
    }

    /// <summary>
    /// 一轮解析：页目录和物理池的唯一写入者。
    /// 1. 每个请求沿祖先链向上展开，权重累加到每一级祖先。
    /// 2. 先触碰：已驻留的页全部接到 LRU 最新端。
    /// 3. 新出现的空闲页按优先级入队（队列有上限）；排队过久且本轮没被请求的退回空闲。
    /// 4. 再分配：从本轮被请求的排队页里按优先级取，最多 min(烘焙预算, 容量 - 本轮触碰数) 个，
    ///    因此本轮触碰过的页不会被淘汰。
    /// </summary>
    [BurstCompile]
    internal unsafe struct RequestResolveJob : IJob
    {
        public ResolveSettings settings;

        [NativeDisableUnsafePtrRestriction] public PageDirectory* directory;
        [NativeDisableUnsafePtrRestriction] public PhysicalPool* pool;

        [ReadOnly] public NativeList<PageRequest> requests;
        public NativeParallelHashMap<PageId, int> weights;
        public NativeList<PageRequest> candidates;
        public NativeList<QueuedPage> queue;

        public NativeList<BakeOrder> bakes;
        public NativeReference<bool> residencyChanged;

        public void Execute()
        {
            bakes.Clear();
            residencyChanged.Value = false;

            ExpandAncestors();
            int touched = TouchResidentAndCollectFree();
            AgeQueue();
            Enqueue();
            Allocate(math.min(settings.bakeBudget, pool->capacity - touched));
        }

        void ExpandAncestors()
        {
            weights.Clear();
            for (int i = 0; i < requests.Length; ++i)
            {
                PageId page = requests[i].page;
                int weight = requests[i].weight;
                while (true)
                {
                    if (weights.TryGetValue(page, out int sum)) { weights[page] = sum + weight; }
                    else { weights.Add(page, weight); }

                    if (page.mip == directory->maxMip) { break; }
                    page = page.parent;
                }
            }
        }

        int TouchResidentAndCollectFree()
        {
            candidates.Clear();
            int touched = 0;
            foreach (var pair in weights)
            {
                ref PageEntry entry = ref directory->Get(pair.Key);
                if (entry.state == EPageState.Resident)
                {
                    pool->Touch(entry.slot);
                    ++touched;
                }
                else if (entry.state == EPageState.Free)
                {
                    candidates.Add(new PageRequest(pair.Key, pair.Value));
                }
            }
            return touched;
        }

        void AgeQueue()
        {
            for (int i = queue.Length - 1; i >= 0; --i)
            {
                QueuedPage queued = queue[i];
                if (weights.ContainsKey(queued.page))
                {
                    queued.lastRequestRound = settings.round;
                    queue[i] = queued;
                }
                else if (settings.round - queued.lastRequestRound > (uint)settings.queueTimeout)
                {
                    directory->Get(queued.page).state = EPageState.Free;
                    queue.RemoveAtSwapBack(i);
                }
            }
        }

        void Enqueue()
        {
            candidates.Sort(new RequestPriority());
            for (int i = 0; i < candidates.Length && queue.Length < settings.queueCapacity; ++i)
            {
                PageId page = candidates[i].page;
                directory->Get(page).state = EPageState.Queued;
                queue.Add(new QueuedPage { page = page, lastRequestRound = settings.round });
            }
        }

        void Allocate(int budget)
        {
            candidates.Clear();
            for (int i = 0; i < queue.Length; ++i)
            {
                if (weights.TryGetValue(queue[i].page, out int weight))
                {
                    candidates.Add(new PageRequest(queue[i].page, weight));
                }
            }
            candidates.Sort(new RequestPriority());

            for (int i = 0; i < candidates.Length && bakes.Length < budget; ++i)
            {
                PageId page = candidates[i].page;

                // 祖先先烘焙：父页还没驻留时留在队列里等下一轮。
                if (page.mip < directory->maxMip && directory->Get(page.parent).state != EPageState.Resident) { continue; }

                int slot = pool->Allocate(page, out PageId evicted);
                if (evicted.isValid) { directory->Get(evicted).state = EPageState.Free; }

                ref PageEntry entry = ref directory->Get(page);
                entry.state = EPageState.Resident;
                entry.slot = (ushort)slot;

                RemoveFromQueue(page);
                bakes.Add(new BakeOrder { page = page, slot = slot });
                residencyChanged.Value = true;
            }
        }

        void RemoveFromQueue(in PageId page)
        {
            for (int i = 0; i < queue.Length; ++i)
            {
                if (queue[i].page.Equals(page)) { queue.RemoveAtSwapBack(i); return; }
            }
        }
    }
}
