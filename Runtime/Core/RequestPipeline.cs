using System;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>渲染侧持有的一轮结果：烘焙单，以及驻留变化后的间接表写入。</summary>
    internal class RoundResult : IDisposable
    {
        public NativeList<BakeOrder> bakes = new NativeList<BakeOrder>(64, Allocator.Persistent);
        public NativeList<IndirectWrite> indirectWrites = new NativeList<IndirectWrite>(1024, Allocator.Persistent);
        public NativeArray<int2> levelRanges = new NativeArray<int2>(PageId.MaxMipCount, Allocator.Persistent);
        public bool residencyChanged;

        /// <summary>换了一套虚拟纹理时清空，避免把旧结果画进新的间接表。</summary>
        public void Clear()
        {
            bakes.Clear();
            indirectWrites.Clear();
            for (int i = 0; i < levelRanges.Length; ++i) { levelRanges[i] = int2.zero; }
            residencyChanged = false;
        }

        public void Dispose()
        {
            bakes.Dispose();
            indirectWrites.Dispose();
            levelRanges.Dispose();
        }
    }

    /// <summary>
    /// 页请求通路：回读数据到达后调度 解码 → 去重 → 解析 → 间接表写入 四个 Job。
    /// 页目录和物理池归这里所有，只有解析 Job 会写它们；同一时间最多一轮在途。
    /// 渲染侧只通过 TryTakeResult 拿已完成的结果。
    /// </summary>
    internal unsafe class RequestPipeline : IDisposable
    {
        public PageDirectory* directory { get; private set; }
        public PhysicalPool* pool { get; private set; }

        /// <summary>有一轮正在 Job 里跑，或已完成但还没被渲染侧取走。</summary>
        public bool isBusy => m_Scheduled;

        readonly int m_BakeBudget;
        readonly int m_QueueCapacity;
        readonly int m_QueueTimeout;

        NativeArray<uint> m_Pixels;
        NativeArray<uint> m_Pages;
        NativeList<PageRequest> m_Requests;
        NativeParallelHashMap<PageId, int> m_Weights;
        NativeList<PageRequest> m_Candidates;
        NativeList<QueuedPage> m_Queue;
        NativeList<BakeOrder> m_Bakes;
        NativeReference<bool> m_ResidencyChanged;
        NativeList<ResidentPage> m_Resident;
        NativeList<IndirectWrite> m_IndirectWrites;
        NativeArray<int2> m_LevelRanges;

        JobHandle m_Handle;
        bool m_Scheduled;
        uint m_Round;

        public RequestPipeline(int pageCount, int poolSize, int bakeBudget, int queueCapacity, int queueTimeout)
        {
            directory = (PageDirectory*)UnsafeUtility.Malloc(sizeof(PageDirectory), UnsafeUtility.AlignOf<PageDirectory>(), Allocator.Persistent);
            *directory = new PageDirectory(pageCount);
            pool = (PhysicalPool*)UnsafeUtility.Malloc(sizeof(PhysicalPool), UnsafeUtility.AlignOf<PhysicalPool>(), Allocator.Persistent);
            *pool = new PhysicalPool(poolSize);

            m_BakeBudget = bakeBudget;
            m_QueueCapacity = queueCapacity;
            m_QueueTimeout = queueTimeout;

            int expandedCapacity = 4096;
            m_Requests = new NativeList<PageRequest>(expandedCapacity, Allocator.Persistent);
            m_Weights = new NativeParallelHashMap<PageId, int>(expandedCapacity, Allocator.Persistent);
            m_Candidates = new NativeList<PageRequest>(expandedCapacity, Allocator.Persistent);
            m_Queue = new NativeList<QueuedPage>(queueCapacity, Allocator.Persistent);
            m_Bakes = new NativeList<BakeOrder>(bakeBudget, Allocator.Persistent);
            m_ResidencyChanged = new NativeReference<bool>(Allocator.Persistent);
            m_Resident = new NativeList<ResidentPage>(pool->capacity, Allocator.Persistent);
            m_IndirectWrites = new NativeList<IndirectWrite>(pool->capacity * directory->mipCount, Allocator.Persistent);
            m_LevelRanges = new NativeArray<int2>(PageId.MaxMipCount, Allocator.Persistent);
        }

        /// <summary>回读到达时调用：拷贝反馈像素并调度这一轮。</summary>
        public void Schedule(NativeArray<uint> feedbackPixels)
        {
            if (m_Scheduled) { return; }

            if (!m_Pixels.IsCreated || m_Pixels.Length != feedbackPixels.Length)
            {
                if (m_Pixels.IsCreated) { m_Pixels.Dispose(); m_Pages.Dispose(); }
                m_Pixels = new NativeArray<uint>(feedbackPixels.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_Pages = new NativeArray<uint>(feedbackPixels.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            m_Pixels.CopyFrom(feedbackPixels);

            var decode = new FeedbackDecodeJob
            {
                pageCount = directory->pageCount,
                mipCount = directory->mipCount,
                pixels = m_Pixels,
                pages = m_Pages
            };
            var collect = new RequestCollectJob
            {
                pages = m_Pages,
                requests = m_Requests
            };
            var resolve = new RequestResolveJob
            {
                settings = new ResolveSettings
                {
                    round = ++m_Round,
                    bakeBudget = m_BakeBudget,
                    queueCapacity = m_QueueCapacity,
                    queueTimeout = m_QueueTimeout
                },
                directory = directory,
                pool = pool,
                requests = m_Requests,
                weights = m_Weights,
                candidates = m_Candidates,
                queue = m_Queue,
                bakes = m_Bakes,
                residencyChanged = m_ResidencyChanged
            };

            var indirect = new IndirectBuildJob
            {
                pool = pool,
                mipCount = directory->mipCount,
                residencyChanged = m_ResidencyChanged,
                resident = m_Resident,
                writes = m_IndirectWrites,
                levelRanges = m_LevelRanges
            };

            JobHandle handle = decode.Schedule(m_Pixels.Length, 256);
            handle = collect.Schedule(handle);
            handle = resolve.Schedule(handle);
            m_Handle = indirect.Schedule(handle);
            m_Scheduled = true;
        }

        /// <summary>这一轮已经跑完时交出结果，否则返回 false 且不阻塞。</summary>
        public bool TryTakeResult(RoundResult result)
        {
            if (!m_Scheduled || !m_Handle.IsCompleted) { return false; }

            m_Handle.Complete();
            m_Scheduled = false;
            result.bakes.CopyFrom(m_Bakes);
            result.residencyChanged = m_ResidencyChanged.Value;
            if (result.residencyChanged)
            {
                result.indirectWrites.CopyFrom(m_IndirectWrites);
                result.levelRanges.CopyFrom(m_LevelRanges);
            }
            return true;
        }

        public void Dispose()
        {
            m_Handle.Complete();
            m_Scheduled = false;

            if (m_Pixels.IsCreated) { m_Pixels.Dispose(); m_Pages.Dispose(); }
            m_Requests.Dispose();
            m_Weights.Dispose();
            m_Candidates.Dispose();
            m_Queue.Dispose();
            m_Bakes.Dispose();
            m_ResidencyChanged.Dispose();
            m_Resident.Dispose();
            m_IndirectWrites.Dispose();
            m_LevelRanges.Dispose();

            directory->Dispose();
            UnsafeUtility.Free(directory, Allocator.Persistent);
            directory = null;
            pool->Dispose();
            UnsafeUtility.Free(pool, Allocator.Persistent);
            pool = null;
        }
    }
}
