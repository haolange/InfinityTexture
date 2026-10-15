using NUnit.Framework;
using System.Collections.Generic;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public class ResidencyTests
    {
        [Test]
        public void PageId_PacksAndUnpacks()
        {
            var page = new PageId(4095, 17, 3);
            Assert.IsTrue(page.isValid);
            Assert.AreEqual(4095, page.x);
            Assert.AreEqual(17, page.y);
            Assert.AreEqual(3, page.mip);
            Assert.AreEqual(new PageId(2047, 8, 4), page.parent);
            Assert.IsFalse(PageId.Invalid.isValid);
        }

        [Test]
        public void Directory_LevelsAreDisjointAndStartFree()
        {
            var directory = new PageDirectory(16);
            try
            {
                Assert.AreEqual(5, directory.mipCount);
                Assert.AreEqual(256 + 64 + 16 + 4 + 1, directory.entryCount);

                int written = 0;
                foreach (PageId page in AllPages(directory))
                {
                    ref PageEntry entry = ref directory.Get(page);
                    Assert.AreEqual(EPageState.Free, entry.state);
                    entry.slot = (ushort)written++;
                }
                written = 0;
                foreach (PageId page in AllPages(directory))
                {
                    Assert.AreEqual(written++, directory.Get(page).slot, page.ToString());
                }

                Assert.IsTrue(directory.Contains(new PageId(3, 3, 2)));
                Assert.IsFalse(directory.Contains(new PageId(4, 0, 2)));
                Assert.IsFalse(directory.Contains(new PageId(0, 0, 5)));
                Assert.IsFalse(directory.Contains(PageId.Invalid));
            }
            finally { directory.Dispose(); }
        }

        [Test]
        public void Pool_StartsInSlotOrder()
        {
            var pool = new PhysicalPool(2);
            try
            {
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, OldestToNewest(ref pool));
                AssertLinksConsistent(ref pool);
            }
            finally { pool.Dispose(); }
        }

        [Test]
        public void Pool_TouchMovesSlotToNewestEnd()
        {
            var pool = new PhysicalPool(2);
            try
            {
                pool.Touch(0);
                pool.Touch(2);
                pool.Touch(2);
                CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, OldestToNewest(ref pool));
                AssertLinksConsistent(ref pool);

                pool.Touch(3);
                CollectionAssert.AreEqual(new[] { 1, 0, 2, 3 }, OldestToNewest(ref pool));
                AssertLinksConsistent(ref pool);
            }
            finally { pool.Dispose(); }
        }

        [Test]
        public void Pool_AllocateTakesOldestAndReportsEvictedOwner()
        {
            var pool = new PhysicalPool(2);
            try
            {
                var a = new PageId(0, 0, 0);
                var b = new PageId(1, 0, 0);

                Assert.AreEqual(0, pool.Allocate(a, out PageId evicted));
                Assert.IsFalse(evicted.isValid);
                Assert.AreEqual(a, pool[0].owner);
                Assert.AreEqual(0, pool.newest);

                // 1、2、3 依次被占满后，最旧的又是 0。
                for (int i = 1; i < 4; ++i) { pool.Allocate(new PageId(i, 1, 0), out _); }
                Assert.AreEqual(0, pool.oldest);

                // 先触碰 0，再分配：应该拿到 1，而不是刚触碰过的 0。
                pool.Touch(0);
                Assert.AreEqual(1, pool.Allocate(b, out evicted));
                Assert.AreEqual(new PageId(1, 1, 0), evicted);
                Assert.AreEqual(b, pool[1].owner);
                AssertLinksConsistent(ref pool);
            }
            finally { pool.Dispose(); }
        }

        static IEnumerable<PageId> AllPages(PageDirectory directory)
        {
            for (int mip = 0; mip < directory.mipCount; ++mip)
            {
                int side = directory.PagesAtMip(mip);
                for (int y = 0; y < side; ++y)
                {
                    for (int x = 0; x < side; ++x) { yield return new PageId(x, y, mip); }
                }
            }
        }

        static int[] OldestToNewest(ref PhysicalPool pool)
        {
            var order = new List<int>();
            for (int slot = pool.oldest; slot != PhysicalPool.None; slot = pool[slot].newer) { order.Add(slot); }
            return order.ToArray();
        }

        /// <summary>正向遍历与反向遍历必须互为逆序，且覆盖全部槽。</summary>
        static void AssertLinksConsistent(ref PhysicalPool pool)
        {
            var forward = OldestToNewest(ref pool);
            var backward = new List<int>();
            for (int slot = pool.newest; slot != PhysicalPool.None; slot = pool[slot].older) { backward.Add(slot); }
            backward.Reverse();

            Assert.AreEqual(pool.capacity, forward.Length);
            CollectionAssert.AreEqual(forward, backward);
            CollectionAssert.AreEquivalent(System.Linq.Enumerable.Range(0, pool.capacity), forward);
        }
    }
}
