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
        public void Order_StartsInSlotOrder()
        {
            var order = new LruOrder(4);
            try
            {
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, OldestToNewest(ref order));
                AssertLinksConsistent(ref order);
            }
            finally { order.Dispose(); }
        }

        [Test]
        public void Order_TouchMovesSlotToNewestEnd()
        {
            var order = new LruOrder(4);
            try
            {
                order.Touch(0);
                order.Touch(2);
                order.Touch(2);
                CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, OldestToNewest(ref order));
                AssertLinksConsistent(ref order);

                order.Touch(3);
                CollectionAssert.AreEqual(new[] { 1, 0, 2, 3 }, OldestToNewest(ref order));
                AssertLinksConsistent(ref order);
            }
            finally { order.Dispose(); }
        }

        [Test]
        public void Pool_AllocateTakesOldestAndReportsEvictedOwner()
        {
            var pool = new PhysicalPool(2);
            var directory = new PageDirectory(16);
            try
            {
                var a = new PageId(0, 0, 0);
                var b = new PageId(1, 0, 0);
                var pages = new[] { a, new PageId(1, 1, 0), new PageId(2, 1, 0), new PageId(3, 1, 0) };

                PageId evicted = PageId.Invalid;
                for (int i = 0; i < pages.Length; ++i)
                {
                    Assert.AreEqual(i, pool.Allocate(ref directory, pages[i], out evicted));
                    Assert.IsFalse(evicted.isValid);
                    Assert.AreEqual(pages[i], pool.SlotOwner(i));
                    Assert.AreEqual(EPageState.Resident, directory.Get(pages[i]).state);
                    Assert.AreEqual(i, directory.Get(pages[i]).slot);
                }

                // 先触碰 0，再分配：应该拿到 1，而不是刚触碰过的 0。
                pool.Touch(0);
                Assert.AreEqual(1, pool.Allocate(ref directory, b, out evicted));
                Assert.AreEqual(pages[1], evicted);
                Assert.AreEqual(pages[0], pool.SlotOwner(0));
                Assert.AreEqual(b, pool.SlotOwner(1));
                Assert.AreEqual(EPageState.Resident, directory.Get(b).state);
                Assert.AreEqual(1, directory.Get(b).slot);
                Assert.AreEqual(EPageState.Free, directory.Get(pages[1]).state);
                Assert.AreEqual(0, directory.Get(pages[1]).slot);
            }
            finally
            {
                pool.Dispose();
                directory.Dispose();
            }
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

        static int[] OldestToNewest(ref LruOrder order)
        {
            var slots = new List<int>();
            for (int slot = order.oldest; slot != LruOrder.None; slot = order.Newer(slot)) { slots.Add(slot); }
            return slots.ToArray();
        }

        /// <summary>正向遍历与反向遍历必须互为逆序，且覆盖全部槽。</summary>
        static void AssertLinksConsistent(ref LruOrder order)
        {
            var forward = OldestToNewest(ref order);
            var backward = new List<int>();
            for (int slot = order.newest; slot != LruOrder.None; slot = order.Older(slot)) { backward.Add(slot); }
            backward.Reverse();

            Assert.AreEqual(order.capacity, forward.Length);
            CollectionAssert.AreEqual(forward, backward);
            CollectionAssert.AreEquivalent(System.Linq.Enumerable.Range(0, order.capacity), forward);
        }
    }
}
