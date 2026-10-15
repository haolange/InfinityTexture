using NUnit.Framework;
using Unity.Jobs;
using Unity.Collections;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    /// <summary>用内存中的反馈像素驱动 解码 → 去重 → 解析 整条页请求通路。</summary>
    public unsafe class RequestTests
    {
        const int PageCount = 16; // mip 0..4

        RequestPipeline m_Pipeline;
        RoundResult m_Round;
        NativeList<BakeOrder> m_Bakes => m_Round.bakes;

        void Create(int poolSize, int bakeBudget = 8, int queueCapacity = 64, int queueTimeout = 2)
        {
            m_Pipeline = new RequestPipeline(PageCount, poolSize, bakeBudget, queueCapacity, queueTimeout);
            m_Round = new RoundResult();
        }

        [TearDown]
        public void TearDown()
        {
            m_Pipeline?.Dispose();
            m_Pipeline = null;
            m_Round?.Dispose();
            m_Round = null;
        }

        [Test]
        public void Decode_RejectsEmptyAndOutOfRangePixels()
        {
            var pixels = new NativeArray<uint>(new[]
            {
                0u,
                new PageId(3, 2, 1).packed,
                new PageId(8, 0, 1).packed,   // mip 1 每边只有 8 页
                new PageId(0, 0, 5).packed,   // 16 页只有 mip 0..4
            }, Allocator.TempJob);
            var pages = new NativeArray<uint>(pixels.Length, Allocator.TempJob);
            try
            {
                new FeedbackDecodeJob { pageCount = PageCount, mipCount = 5, pixels = pixels, pages = pages }.Run(pixels.Length);
                CollectionAssert.AreEqual(new[] { 0u, new PageId(3, 2, 1).packed, 0u, 0u }, pages.ToArray());
            }
            finally { pixels.Dispose(); pages.Dispose(); }
        }

        [Test]
        public void Collect_MergesDuplicatesAndCountsWeight()
        {
            uint a = new PageId(1, 1, 0).packed;
            uint b = new PageId(0, 0, 2).packed;
            var pages = new NativeArray<uint>(new[] { 0u, a, b, a, 0u, a, b }, Allocator.TempJob);
            var requests = new NativeList<PageRequest>(4, Allocator.TempJob);
            try
            {
                new RequestCollectJob { pages = pages, requests = requests }.Run();
                Assert.AreEqual(2, requests.Length);
                Assert.AreEqual(a, requests[0].page.packed);
                Assert.AreEqual(3, requests[0].weight);
                Assert.AreEqual(b, requests[1].page.packed);
                Assert.AreEqual(2, requests[1].weight);
            }
            finally { pages.Dispose(); requests.Dispose(); }
        }

        [Test]
        public void Resolve_QueuesMissingAncestorsAndBakesCoarseFirst()
        {
            Create(poolSize: 2);
            var page = new PageId(3, 5, 0);

            RunRound(Repeat(page, 10));

            AssertBakes(new PageId(0, 0, 4), new PageId(0, 0, 3), new PageId(0, 1, 2), new PageId(1, 2, 1));
            for (int i = 0; i < m_Bakes.Length; ++i)
            {
                ref PageEntry entry = ref m_Pipeline.directory->Get(m_Bakes[i].page);
                Assert.AreEqual(EPageState.Resident, entry.state);
                Assert.AreEqual(m_Bakes[i].slot, entry.slot);
                Assert.AreEqual(m_Bakes[i].page, (*m_Pipeline.pool)[entry.slot].owner);
            }
            Assert.AreEqual(EPageState.Queued, State(page));
        }

        [Test]
        public void Resolve_NeverEvictsPagesTouchedThisRound()
        {
            Create(poolSize: 2);
            var page = new PageId(3, 5, 0);
            RunRound(Repeat(page, 10));

            // 池已满且整条祖先链本轮都被触碰：预算为 0，叶子页继续排队。
            RunRound(Repeat(page, 10));
            AssertBakes();
            Assert.AreEqual(EPageState.Queued, State(page));
            Assert.AreEqual(EPageState.Resident, State(page.parent));
        }

        [Test]
        public void Resolve_HigherWeightFirstThenCoarserFirst()
        {
            Create(poolSize: 2);
            var light = new PageId(0, 0, 3);
            var heavy = new PageId(2, 0, 2); // 父页 (1, 0, 3)

            RunRound(Concat(Repeat(light, 1), Repeat(heavy, 4)));

            // 权重：根 5，(1,0,3) 4，(2,0,2) 4，(0,0,3) 1。同权重时粗页在前。
            AssertBakes(new PageId(0, 0, 4), heavy.parent, heavy, light);
        }

        [Test]
        public void Resolve_EvictsOldestUntouchedSlot()
        {
            Create(poolSize: 2);
            var a = new PageId(0, 0, 2);
            var b = new PageId(1, 1, 2);
            var c = new PageId(1, 0, 2);

            RunRound(Repeat(a, 1)); // 根、(0,0,3)、a
            RunRound(Repeat(b, 1)); // b 进入第 4 个槽
            AssertBakes(b);

            RunRound(Repeat(c, 1)); // 根与 (0,0,3) 被触碰，最旧的未触碰槽是 a 的
            Assert.AreEqual(1, m_Bakes.Length);
            Assert.AreEqual(c, m_Bakes[0].page);
            Assert.AreEqual(EPageState.Free, State(a));
            Assert.AreEqual(EPageState.Resident, State(b));
            Assert.AreEqual(c, (*m_Pipeline.pool)[m_Bakes[0].slot].owner);
        }

        [Test]
        public void Resolve_QueueIsCapped()
        {
            Create(poolSize: 1, queueCapacity: 2);
            var page = new PageId(5, 5, 0);

            RunRound(Repeat(page, 1));

            // 入队按优先级：根与 mip3 进队，根随即被烘焙出队。
            AssertBakes(new PageId(0, 0, 4));
            Assert.AreEqual(EPageState.Queued, State(new PageId(0, 0, 3)));
            Assert.AreEqual(EPageState.Free, State(new PageId(1, 1, 2)));
            Assert.AreEqual(EPageState.Free, State(new PageId(2, 2, 1)));
            Assert.AreEqual(EPageState.Free, State(page));
        }

        [Test]
        public void Resolve_StaleQueuedPagesReturnToFree()
        {
            Create(poolSize: 1, queueTimeout: 2);
            var page = new PageId(5, 5, 0);
            RunRound(Repeat(page, 1));
            Assert.AreEqual(EPageState.Queued, State(page));

            RunRound();
            RunRound();
            Assert.AreEqual(EPageState.Queued, State(page), "未超时前保持排队");

            RunRound();
            Assert.AreEqual(EPageState.Free, State(page));
            Assert.AreEqual(EPageState.Free, State(new PageId(0, 0, 3)));
            Assert.AreEqual(EPageState.Resident, State(new PageId(0, 0, 4)));
        }

        void RunRound(params uint[] pixels)
        {
            var feedback = new NativeArray<uint>(pixels, Allocator.Temp);
            m_Pipeline.Schedule(feedback);
            feedback.Dispose();
            while (!m_Pipeline.TryTakeResult(m_Round)) { JobHandle.ScheduleBatchedJobs(); }
        }

        EPageState State(PageId page) => m_Pipeline.directory->Get(page).state;

        void AssertBakes(params PageId[] expected)
        {
            var actual = new PageId[m_Bakes.Length];
            for (int i = 0; i < actual.Length; ++i) { actual[i] = m_Bakes[i].page; }
            CollectionAssert.AreEqual(expected, actual);
        }

        static uint[] Repeat(PageId page, int count)
        {
            var pixels = new uint[count];
            for (int i = 0; i < count; ++i) { pixels[i] = page.packed; }
            return pixels;
        }

        static uint[] Concat(uint[] a, uint[] b)
        {
            var result = new uint[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }
    }
}
