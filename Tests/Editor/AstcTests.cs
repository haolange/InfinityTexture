using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;

namespace Landscape.RuntimeVirtualTexture.Tests
{
    /// <summary>ASTC 的打包尺寸与查找表，全部对照 Khronos 规范的 ASTC 章节。</summary>
    public class AstcTests
    {
        [TestCase(EAstcBlock.X4, true, 4, 4, false, 8, 192, 126)]
        [TestCase(EAstcBlock.X6, true, 5, 5, false, 5, 80, 127)]
        [TestCase(EAstcBlock.X8, true, 5, 5, false, 4, 192, 128)]
        [TestCase(EAstcBlock.X4, false, 4, 4, true, 4, 48, 128)]
        [TestCase(EAstcBlock.X6, false, 4, 4, true, 4, 48, 128)]
        [TestCase(EAstcBlock.X8, false, 4, 4, true, 4, 48, 128)]
        public void ConfigFitsIn128Bits(EAstcBlock block, bool isColor, int gridWidth, int gridHeight, bool dualPlane, int weightLevels, int endpointLevels, int usedBits)
        {
            AstcLayout.Config config = AstcLayout.Of(block, isColor);
            Assert.AreEqual((int)block, config.block);
            Assert.AreEqual(gridWidth, config.gridWidth);
            Assert.AreEqual(gridHeight, config.gridHeight);
            Assert.AreEqual(dualPlane, config.dualPlane);
            Assert.AreEqual(weightLevels, config.weightRange.levels);
            Assert.AreEqual(endpointLevels, config.endpointRange.levels);
            Assert.AreEqual(usedBits, config.usedBits);
            Assert.LessOrEqual(config.usedBits, 128);
            Assert.LessOrEqual(config.gridWidth, config.block);
            Assert.That(config.weightBits, Is.InRange(24, 96), "规范要求权重位数在 24..96");
        }

        /// <summary>端点范围是剩余位数内能放下 8 个值的最大范围：选中的放得下，更大一档放不下。</summary>
        [Test]
        public void EndpointRangeIsTheLargestThatFits()
        {
            foreach (EAstcBlock block in Enum.GetValues(typeof(EAstcBlock)))
            {
                foreach (bool isColor in new[] { true, false })
                {
                    AstcLayout.Config config = AstcLayout.Of(block, isColor);
                    int remaining = 128 - config.configBits - config.weightBits;
                    int index = Array.FindIndex(AstcLayout.ColorRanges, range => range.levels == config.endpointRange.levels);
                    Assert.LessOrEqual(config.endpointBits, remaining);
                    if (index > 0) { Assert.Greater(AstcLayout.ColorRanges[index - 1].BitCount(8), remaining); }
                }
            }
        }

        [Test]
        public void BlockModeMatchesSpecLayout()
        {
            Assert.AreEqual(0x53, AstcLayout.Of(EAstcBlock.X4, true).blockMode);
            Assert.AreEqual(0xF2, AstcLayout.Of(EAstcBlock.X6, true).blockMode);
            Assert.AreEqual(0xE2, AstcLayout.Of(EAstcBlock.X8, true).blockMode);
            Assert.AreEqual(0x442, AstcLayout.Of(EAstcBlock.X4, false).blockMode);

            foreach (EAstcBlock block in Enum.GetValues(typeof(EAstcBlock)))
            {
                foreach (bool isColor in new[] { true, false })
                {
                    AstcLayout.Config config = AstcLayout.Of(block, isColor);
                    DecodeBlockMode(config.blockMode, out int width, out int height, out int levels, out bool dual);
                    Assert.AreEqual(config.gridWidth, width);
                    Assert.AreEqual(config.gridHeight, height);
                    Assert.AreEqual(config.weightRange.levels, levels);
                    Assert.AreEqual(config.dualPlane, dual);
                }
            }
        }

        [Test]
        public void TritAndQuintTablesRoundTrip()
        {
            int[] trits = new int[5];
            int[] tritTable = AstcLayout.TritEncodeTable();
            for (int index = 0; index < 243; ++index)
            {
                AstcLayout.DecodeTrits(tritTable[index], trits);
                Assert.AreEqual(index, trits[0] + 3 * trits[1] + 9 * trits[2] + 27 * trits[3] + 81 * trits[4]);
            }

            int[] quints = new int[3];
            int[] quintTable = AstcLayout.QuintEncodeTable();
            for (int index = 0; index < 125; ++index)
            {
                AstcLayout.DecodeQuints(quintTable[index], quints);
                Assert.AreEqual(index, quints[0] + 5 * quints[1] + 25 * quints[2]);
            }
        }

        /// <summary>序列末组不满时补 0：补出的值对应的高位必须为 0，否则解码端读到的截断流会解错。</summary>
        [Test]
        public void TrailingZeroGroupsLeaveHighBitsClear()
        {
            int[] tritTable = AstcLayout.TritEncodeTable();
            int[] tritBitsUsed = { 0, 2, 4, 5, 7, 8 };
            for (int index = 0; index < 243; ++index)
            {
                int values = 5;
                while (values > 0 && index / (int)Math.Pow(3, values - 1) % 3 == 0) { --values; }
                Assert.AreEqual(0, tritTable[index] >> tritBitsUsed[values], $"trit tuple {index}");
            }

            int[] quintTable = AstcLayout.QuintEncodeTable();
            int[] quintBitsUsed = { 0, 3, 5, 7 };
            for (int index = 0; index < 125; ++index)
            {
                int values = 3;
                while (values > 0 && index / (int)Math.Pow(5, values - 1) % 5 == 0) { --values; }
                Assert.AreEqual(0, quintTable[index] >> quintBitsUsed[values], $"quint tuple {index}");
            }
        }

        [Test]
        public void UnquantizationMatchesSpecValues()
        {
            var color6 = new IseRange(6, 1, 0, 1);
            int[] expected = { 0, 255, 51, 204, 102, 153 };
            for (int i = 0; i < 6; ++i) { Assert.AreEqual(expected[i], AstcLayout.UnquantizeColor(color6, i)); }

            int[] weights3 = { 0, 32, 64 };
            for (int i = 0; i < 3; ++i) { Assert.AreEqual(weights3[i], AstcLayout.UnquantizeWeight(AstcLayout.WeightRange(3), i)); }
            int[] weights5 = { 0, 16, 32, 48, 64 };
            for (int i = 0; i < 5; ++i) { Assert.AreEqual(weights5[i], AstcLayout.UnquantizeWeight(AstcLayout.WeightRange(5), i)); }

            var weights12 = new int[12];
            for (int i = 0; i < 12; ++i) { weights12[i] = AstcLayout.UnquantizeWeight(AstcLayout.WeightRange(12), i); }
            Array.Sort(weights12);
            CollectionAssert.AreEqual(new[] { 0, 5, 11, 17, 23, 28, 36, 41, 47, 53, 59, 64 }, weights12);
        }

        [Test]
        public void EveryColorRangeCoversZeroTo255WithDistinctValues()
        {
            foreach (IseRange range in AstcLayout.ColorRanges)
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < range.levels; ++i) { seen.Add(AstcLayout.UnquantizeColor(range, i)); }
                Assert.AreEqual(range.levels, seen.Count, $"range {range.levels}");
                Assert.IsTrue(seen.Contains(0) && seen.Contains(255), $"range {range.levels}");
            }
        }

        /// <summary>量化表给出的编码值，反量化后与目标的误差最小。</summary>
        [Test]
        public void QuantizationTablesPickTheNearestValue()
        {
            foreach (EAstcBlock block in Enum.GetValues(typeof(EAstcBlock)))
            {
                foreach (bool isColor in new[] { true, false })
                {
                    AstcLayout.Config config = AstcLayout.Of(block, isColor);
                    uint[] tables = AstcLayout.BuildTables(config);
                    for (int target = 0; target <= 255; ++target)
                    {
                        int chosen = AstcLayout.UnquantizeColor(config.endpointRange, (int)tables[AstcLayout.ColorQuantOffset + target]);
                        for (int v = 0; v < config.endpointRange.levels; ++v)
                        {
                            Assert.LessOrEqual(Math.Abs(chosen - target), Math.Abs(AstcLayout.UnquantizeColor(config.endpointRange, v) - target));
                        }
                    }
                    for (int target = 0; target <= 64; ++target)
                    {
                        int chosen = AstcLayout.UnquantizeWeight(config.weightRange, (int)tables[AstcLayout.WeightQuantOffset + target]);
                        for (int v = 0; v < config.weightRange.levels; ++v)
                        {
                            Assert.LessOrEqual(Math.Abs(chosen - target), Math.Abs(AstcLayout.UnquantizeWeight(config.weightRange, v) - target));
                        }
                    }
                }
            }
        }

        [Test]
        public void PageFormatUsesBlockSizeAndSrgbForColor()
        {
            PageFormat x6 = PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X6);
            Assert.AreEqual(new int2(6, 6), x6.blockSize);
            Assert.AreEqual(GraphicsFormat.RGBA_ASTC6X6_SRGB, x6.PhysicalFormat(true));
            Assert.AreEqual(GraphicsFormat.RGBA_ASTC6X6_UNorm, x6.PhysicalFormat(false));
            Assert.AreEqual(GraphicsFormat.RGBA_ASTC4X4_UNorm, PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X4).PhysicalFormat(false));
            Assert.AreEqual(GraphicsFormat.RGBA_ASTC8X8_SRGB, PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X8).PhysicalFormat(true));

            // 264 = 256 + 2 × 4 可被 4、6、8 整除。
            const int slot = 264;
            Assert.IsTrue(PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X4).IsAligned(slot));
            Assert.IsTrue(x6.IsAligned(slot));
            Assert.IsTrue(PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X8).IsAligned(slot));
            Assert.IsFalse(PageFormat.Of(EPageCompression.ASTC, EAstcBlock.X8).IsAligned(258));
            Assert.AreEqual(44 * 44 * 16, x6.SlotBytes(slot));
        }

        /// <summary>按规范 "2D block mode layout" 表独立解码（只覆盖本库用到的第 1 行及双平面位）。</summary>
        static void DecodeBlockMode(int mode, out int width, out int height, out int levels, out bool dual)
        {
            Assert.AreNotEqual(0, mode & 3, "低两位为 0 的布局本库不使用");
            Assert.AreEqual(0, (mode >> 2) & 3, "本库只使用第 1 行布局");
            int rho = (mode >> 4 & 1) | (mode & 3) << 1;
            int precision = mode >> 9 & 1;
            width = (mode >> 7 & 3) + 4;
            height = (mode >> 5 & 3) + 2;
            dual = (mode >> 10 & 1) != 0;
            levels = (precision == 0 ? new[] { 2, 3, 4, 5, 6, 8 } : new[] { 10, 12, 16, 20, 24, 32 })[rho - 2];
        }
    }
}
