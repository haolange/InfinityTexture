using NUnit.Framework;
using Unity.Jobs;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    public unsafe class IndirectTests
    {
        const int PageCount = 16; // mip 0..4
        const int MipCount = 5;

        [Test]
        public void Footprint_ScalesPageToLevel()
        {
            var page = new PageId(3, 5, 2);
            Assert.AreEqual(new int4(12, 20, 4, 4), VirtualTextureMath.Footprint(page, 0));
            Assert.AreEqual(new int4(6, 10, 2, 2), VirtualTextureMath.Footprint(page, 1));
            Assert.AreEqual(new int4(3, 5, 1, 1), VirtualTextureMath.Footprint(page, 2));
        }

        /// <summary>
        /// 把 IndirectBuildJob 的写入按顺序画进内存中的各级，
        /// 每个格子都必须等于"自身或最近的驻留祖先"，没有祖先时为 0。
        /// </summary>
        [Test]
        public void EveryLevelResolvesToNearestResidentAncestor()
        {
            var resident = new[]
            {
                new PageId(0, 0, 4),
                new PageId(0, 0, 3), new PageId(1, 1, 3),
                new PageId(2, 2, 2),
                new PageId(5, 4, 1),
                new PageId(11, 9, 0),
                new PageId(12, 12, 0), // 父页 (6,6,1) 与 (3,3,2) 不驻留，应退到 (1,1,3)
            };
            AssertLevels(resident);
        }

        [Test]
        public void WithoutRootUncoveredTexelsStayInvalid()
        {
            AssertLevels(new[] { new PageId(1, 0, 3), new PageId(3, 1, 1) });
        }

        [Test]
        public void PhysicalTexel_UsesUnalignedUvAtThePagesOwnMip()
        {
            const int texels = 256, border = 4, padded = texels + 2 * border;
            var slot = new int2(2, 1);

            // 第 1 级的页 (5,4) 覆盖 uv [5/8, 6/8) x [4/8, 5/8)，取其中心。
            float2 uv = new float2(5.5f, 4.5f) / 8f;
            float2 texel = VirtualTextureMath.PhysicalTexel(uv, slot, 1, PageCount, texels, border);
            Assert.AreEqual(2 * padded + border + 128f, texel.x, 1e-3f);
            Assert.AreEqual(1 * padded + border + 128f, texel.y, 1e-3f);

            // 同一个 uv 若落到祖先 (1,1,3) 上，偏移按第 3 级计算：frac(uv * 2)。
            texel = VirtualTextureMath.PhysicalTexel(uv, slot, 3, PageCount, texels, border);
            Assert.AreEqual(2 * padded + border + math.frac(uv.x * 2) * texels, texel.x, 1e-3f);
            Assert.AreEqual(1 * padded + border + math.frac(uv.y * 2) * texels, texel.y, 1e-3f);

            // 结果始终落在槽的有效区内，border 只留给这一次双线性。
            Assert.GreaterOrEqual(texel.x, 2 * padded + border);
            Assert.Less(texel.x, 2 * padded + border + texels);
        }

        [Test]
        public void PageUVRect_IncludesBorderOnBothSides()
        {
            float4 rect = VirtualTextureMath.PageUVRect(new PageId(1, 0, 3), PageCount, 256, 4);
            float b = 0.5f * 4 / 256;
            Assert.AreEqual(0.5f - b, rect.x, 1e-6f);
            Assert.AreEqual(-b, rect.y, 1e-6f);
            Assert.AreEqual(0.5f + 2 * b, rect.z, 1e-6f);
            Assert.AreEqual(0.5f + 2 * b, rect.w, 1e-6f);
        }

        static void AssertLevels(PageId[] resident)
        {
            var pool = new PhysicalPool(4);
            var changed = new NativeReference<bool>(true, Allocator.TempJob);
            var scratch = new NativeList<ResidentPage>(16, Allocator.TempJob);
            var writes = new NativeList<IndirectWrite>(64, Allocator.TempJob);
            var ranges = new NativeArray<int2>(PageId.MaxMipCount, Allocator.TempJob);
            try
            {
                var slots = new Dictionary<PageId, int>();
                foreach (PageId page in resident) { slots[page] = pool.Allocate(page, out _); }

                new IndirectBuildJob
                {
                    pool = &pool,
                    mipCount = MipCount,
                    residencyChanged = changed,
                    resident = scratch,
                    writes = writes,
                    levelRanges = ranges
                }.Run();

                for (int level = 0; level < MipCount; ++level)
                {
                    int side = PageCount >> level;
                    var texels = new uint[side * side];
                    int2 range = ranges[level];
                    for (int i = range.x; i < range.x + range.y; ++i)
                    {
                        int4 r = writes[i].rect;
                        for (int y = r.y; y < r.y + r.w; ++y)
                        {
                            for (int x = r.x; x < r.x + r.z; ++x) { texels[y * side + x] = writes[i].value; }
                        }
                    }

                    for (int y = 0; y < side; ++y)
                    {
                        for (int x = 0; x < side; ++x)
                        {
                            uint expected = 0;
                            for (var page = new PageId(x, y, level); page.mip < MipCount; page = page.parent)
                            {
                                if (slots.TryGetValue(page, out int slot))
                                {
                                    expected = VirtualTextureMath.IndirectValue(pool.SlotCoord(slot), page.mip);
                                    break;
                                }
                            }
                            Assert.AreEqual(expected, texels[y * side + x], $"level {level} texel ({x}, {y})");
                        }
                    }
                }
            }
            finally
            {
                pool.Dispose();
                changed.Dispose();
                scratch.Dispose();
                writes.Dispose();
                ranges.Dispose();
            }
        }
    }
}
