using System;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>
    /// 页请求通路：回读数据到达后调度 解码 → 去重 → 解析 三个 Job。
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

            JobHandle handle = decode.Schedule(m_Pixels.Length, 256);
            handle = collect.Schedule(handle);
            m_Handle = resolve.Schedule(handle);
            m_Scheduled = true;
        }

        /// <summary>这一轮已经跑完时交出结果，否则返回 false 且不阻塞。</summary>
        public bool TryTakeResult(NativeList<BakeOrder> bakes, out bool residencyChanged)
        {
            residencyChanged = false;
            if (!m_Scheduled || !m_Handle.IsCompleted) { return false; }

            m_Handle.Complete();
            m_Scheduled = false;
            bakes.CopyFrom(m_Bakes);
            residencyChanged = m_ResidencyChanged.Value;
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

            directory->Dispose();
            UnsafeUtility.Free(directory, Allocator.Persistent);
            directory = null;
            pool->Dispose();
            UnsafeUtility.Free(pool, Allocator.Persistent);
            pool = null;
        }
    }
}
