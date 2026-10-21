using System;

namespace Landscape.RuntimeVirtualTexture
{
    /// <summary>ASTC 的块尺寸，决定码率：4x4 = 8 bpp，6x6 ≈ 3.56 bpp，8x8 = 2 bpp。</summary>
    public enum EAstcBlock
    {
        X4 = 4,
        X6 = 6,
        X8 = 8
    }

    /// <summary>ISE（integer sequence encoding）的一档取值范围：levels = (1 或 3 或 5) × 2^bits。</summary>
    internal readonly struct IseRange
    {
        public readonly int levels;
        public readonly int trits;
        public readonly int quints;
        public readonly int bits;

        public IseRange(int levels, int trits, int quints, int bits)
        {
            this.levels = levels;
            this.trits = trits;
            this.quints = quints;
            this.bits = bits;
        }

        /// <summary>编码 count 个值需要的位数（Khronos 规范 Data Size Determination）。</summary>
        public int BitCount(int count) => (count * 8 * trits + 4) / 5 + (count * 7 * quints + 2) / 3 + count * bits;

        /// <summary>着色器里区分编码方式：0 纯位，1 trit，2 quint。</summary>
        public int kind => trits > 0 ? 1 : quints > 0 ? 2 : 0;
    }

    /// <summary>
    /// ASTC 编码的固定配置与查找表，C# 是唯一来源：着色器只读这里生成的参数和表。
    /// 都用单分区、CEM 12（LDR RGBA 直接端点），权重网格可以小于块（由解码端插值）。
    /// 颜色层用单平面；数据层（法线 x 在 A、粗糙度在 A）用双平面，A 通道单独一套权重，与 BC3 的独立 alpha 对等。
    /// 位布局、ISE 和反量化公式均按 Khronos Data Format 规范的 ASTC 章节。
    /// </summary>
    internal static class AstcLayout
    {
        public const int ColorEndpointMode = 12;
        public const int EndpointValues = 8;     // RGBA 两个端点
        public const int AlphaComponent = 3;     // 双平面时第二平面服务的通道（color component selector）

        /// <summary>一档 ASTC 配置。</summary>
        internal readonly struct Config
        {
            public readonly int block;
            public readonly int gridWidth;
            public readonly int gridHeight;
            public readonly bool dualPlane;
            public readonly IseRange weightRange;
            public readonly IseRange endpointRange;

            public Config(int block, int gridWidth, int gridHeight, bool dualPlane, IseRange weightRange)
            {
                this.block = block;
                this.gridWidth = gridWidth;
                this.gridHeight = gridHeight;
                this.dualPlane = dualPlane;
                this.weightRange = weightRange;
                endpointRange = default;
                endpointRange = EndpointRangeFor(128 - configBits - weightBits);
            }

            /// <summary>11 位块模式 + 2 位分区数 + 4 位 CEM，双平面再加 2 位通道选择。</summary>
            public int configBits => dualPlane ? 19 : 17;
            /// <summary>权重个数；双平面时每个网格点两个。</summary>
            public int weightCount => gridWidth * gridHeight * (dualPlane ? 2 : 1);
            public int weightBits => weightRange.BitCount(weightCount);
            public int endpointBits => endpointRange.BitCount(EndpointValues);
            public int usedBits => configBits + weightBits + endpointBits;
            public int blockMode => EncodeBlockMode(gridWidth, gridHeight, dualPlane, weightRange);
        }

        /// <summary>颜色端点可用的范围，从大到小。</summary>
        public static readonly IseRange[] ColorRanges =
        {
            new IseRange(256, 0, 0, 8), new IseRange(192, 1, 0, 6), new IseRange(160, 0, 1, 5), new IseRange(128, 0, 0, 7),
            new IseRange(96, 1, 0, 5), new IseRange(80, 0, 1, 4), new IseRange(64, 0, 0, 6), new IseRange(48, 1, 0, 4),
            new IseRange(40, 0, 1, 3), new IseRange(32, 0, 0, 5), new IseRange(24, 1, 0, 3), new IseRange(20, 0, 1, 2),
            new IseRange(16, 0, 0, 4), new IseRange(12, 1, 0, 2), new IseRange(10, 0, 1, 1), new IseRange(8, 0, 0, 3),
            new IseRange(6, 1, 0, 1)
        };

        /// <summary>权重可用的范围，下标即块模式里的 (P, ρ) 编号：前 6 个 P = 0，后 6 个 P = 1，ρ 从 2 到 7。</summary>
        public static readonly IseRange[] WeightRanges =
        {
            new IseRange(2, 0, 0, 1), new IseRange(3, 1, 0, 0), new IseRange(4, 0, 0, 2), new IseRange(5, 0, 1, 0),
            new IseRange(6, 1, 0, 1), new IseRange(8, 0, 0, 3), new IseRange(10, 0, 1, 1), new IseRange(12, 1, 0, 2),
            new IseRange(16, 0, 0, 4), new IseRange(20, 0, 1, 2), new IseRange(24, 1, 0, 3), new IseRange(32, 0, 0, 5)
        };

        public static IseRange WeightRange(int levels) => Array.Find(WeightRanges, range => range.levels == levels);

        /// <summary>
        /// 每档的固定配置。颜色层单平面：4x4 用 4x4 网格 8 级权重，6x6 用 5x5 网格 5 级，8x8 用 5x5 网格 4 级。
        /// 数据层双平面：都用 4x4 网格 4 级权重。
        /// 端点范围不单独选择：规范规定解码端取剩余位数能放下的最大范围。
        /// </summary>
        public static Config Of(EAstcBlock block, bool isColor)
        {
            int size = (int)block;
            if (!isColor) { return new Config(size, 4, 4, true, WeightRange(4)); }
            switch (block)
            {
                case EAstcBlock.X4: return new Config(size, 4, 4, false, WeightRange(8));
                case EAstcBlock.X6: return new Config(size, 5, 5, false, WeightRange(5));
                default: return new Config(size, 5, 5, false, WeightRange(4));
            }
        }

        /// <summary>规范的端点范围选择：剩余位数内能编码 8 个值的最大范围。</summary>
        public static IseRange EndpointRangeFor(int remainingBits)
        {
            foreach (IseRange range in ColorRanges)
            {
                if (range.BitCount(EndpointValues) <= remainingBits) { return range; }
            }
            throw new ArgumentException($"{remainingBits} bits cannot hold any endpoint range");
        }

        /// <summary>
        /// 2D 块模式（规范表 "2D block mode layout" 第一行，网格宽 4..7、高 2..5）：
        /// [10] 双平面 | [9] P | [8:7] 宽-4 | [6:5] 高-2 | [4] ρ0 | [3:2] 00 | [1] ρ2 | [0] ρ1
        /// </summary>
        public static int EncodeBlockMode(int gridWidth, int gridHeight, bool dualPlane, IseRange weightRange)
        {
            int index = Array.FindIndex(WeightRanges, range => range.levels == weightRange.levels);
            int precision = index / 6;
            int rho = index % 6 + 2;
            if (gridWidth < 4 || gridWidth > 7 || gridHeight < 2 || gridHeight > 5) { throw new ArgumentOutOfRangeException(nameof(gridWidth)); }
            return (dualPlane ? 1 : 0) << 10 | precision << 9 | (gridWidth - 4) << 7 | (gridHeight - 2) << 5 | (rho & 1) << 4 | (rho >> 2 & 1) << 1 | (rho >> 1 & 1);
        }

        /// <summary>规范的 trit 解码：8 位 T → 5 个 trit。</summary>
        public static void DecodeTrits(int packed, int[] trits)
        {
            int Bit(int v, int i) => (v >> i) & 1;
            int Bits(int v, int hi, int lo) => (v >> lo) & ((1 << (hi - lo + 1)) - 1);

            int c;
            if (Bits(packed, 4, 2) == 7)
            {
                c = Bits(packed, 7, 5) << 2 | Bits(packed, 1, 0);
                trits[4] = 2; trits[3] = 2;
            }
            else
            {
                c = Bits(packed, 4, 0);
                if (Bits(packed, 6, 5) == 3) { trits[4] = 2; trits[3] = Bit(packed, 7); }
                else { trits[4] = Bit(packed, 7); trits[3] = Bits(packed, 6, 5); }
            }

            if (Bits(c, 1, 0) == 3) { trits[2] = 2; trits[1] = Bit(c, 4); trits[0] = Bit(c, 3) << 1 | (Bit(c, 2) & ~Bit(c, 3) & 1); }
            else if (Bits(c, 3, 2) == 3) { trits[2] = 2; trits[1] = 2; trits[0] = Bits(c, 1, 0); }
            else { trits[2] = Bit(c, 4); trits[1] = Bits(c, 3, 2); trits[0] = Bit(c, 1) << 1 | (Bit(c, 0) & ~Bit(c, 1) & 1); }
        }

        /// <summary>规范的 quint 解码：7 位 Q → 3 个 quint。</summary>
        public static void DecodeQuints(int packed, int[] quints)
        {
            int Bit(int v, int i) => (v >> i) & 1;
            int Bits(int v, int hi, int lo) => (v >> lo) & ((1 << (hi - lo + 1)) - 1);

            if (Bits(packed, 2, 1) == 3 && Bits(packed, 6, 5) == 0)
            {
                int q0 = Bit(packed, 0);
                quints[2] = q0 << 2 | (Bit(packed, 4) & ~q0 & 1) << 1 | (Bit(packed, 3) & ~q0 & 1);
                quints[1] = 4; quints[0] = 4;
                return;
            }

            int c;
            if (Bits(packed, 2, 1) == 3)
            {
                quints[2] = 4;
                c = Bits(packed, 4, 3) << 3 | (~Bits(packed, 6, 5) & 3) << 1 | Bit(packed, 0);
            }
            else
            {
                quints[2] = Bits(packed, 6, 5);
                c = Bits(packed, 4, 0);
            }

            if (Bits(c, 2, 0) == 5) { quints[1] = 4; quints[0] = Bits(c, 4, 3); }
            else { quints[1] = Bits(c, 4, 3); quints[0] = Bits(c, 2, 0); }
        }

        /// <summary>trit 编码表：下标 t0 + 3t1 + 9t2 + 27t3 + 81t4，取解码到该组合的最小 T（末尾为 0 的组合高位也为 0）。</summary>
        public static int[] TritEncodeTable()
        {
            var table = new int[243];
            var seen = new bool[243];
            var trits = new int[5];
            for (int packed = 0; packed < 256; ++packed)
            {
                DecodeTrits(packed, trits);
                int index = trits[0] + 3 * trits[1] + 9 * trits[2] + 27 * trits[3] + 81 * trits[4];
                if (!seen[index]) { seen[index] = true; table[index] = packed; }
            }
            return table;
        }

        /// <summary>quint 编码表：下标 q0 + 5q1 + 25q2，取解码到该组合的最小 Q。</summary>
        public static int[] QuintEncodeTable()
        {
            var table = new int[125];
            var seen = new bool[125];
            var quints = new int[3];
            for (int packed = 0; packed < 128; ++packed)
            {
                DecodeQuints(packed, quints);
                int index = quints[0] + 5 * quints[1] + 25 * quints[2];
                if (!seen[index]) { seen[index] = true; table[index] = packed; }
            }
            return table;
        }

        // 规范 "color unquantization parameters" 与 "weight unquantization parameters" 表：B 的位模式（高位在前）与 C。
        static readonly (int levels, string b, int c)[] s_ColorUnquant =
        {
            (6, "000000000", 204), (10, "000000000", 113), (12, "b000b0bb0", 93), (20, "b0000bb00", 54),
            (24, "cb000cbcb", 44), (40, "cb0000cbc", 26), (48, "dcb000dcb", 22), (80, "dcb0000dc", 13),
            (96, "edcb000ed", 11), (160, "edcb0000e", 6), (192, "fedcb000f", 5)
        };

        static readonly (int levels, string b, int c)[] s_WeightUnquant =
        {
            (6, "0000000", 50), (10, "0000000", 28), (12, "b000b0b", 23), (20, "b0000b0", 13), (24, "cb000cb", 11)
        };

        /// <summary>端点值反量化到 0..255。</summary>
        public static int UnquantizeColor(IseRange range, int value)
        {
            if (range.trits == 0 && range.quints == 0) { return Replicate(value, range.bits, 8); }
            var row = Array.Find(s_ColorUnquant, entry => entry.levels == range.levels);
            return UnquantizeTritQuint(value, range.bits, row.b, row.c, 9, 0x80);
        }

        /// <summary>权重值反量化到 0..64。</summary>
        public static int UnquantizeWeight(IseRange range, int value)
        {
            int result;
            if (range.trits == 0 && range.quints == 0) { result = Replicate(value, range.bits, 6); }
            else if (range.bits == 0) { result = range.levels == 3 ? new[] { 0, 32, 63 }[value] : new[] { 0, 16, 32, 47, 63 }[value]; }
            else
            {
                var row = Array.Find(s_WeightUnquant, entry => entry.levels == range.levels);
                result = UnquantizeTritQuint(value, range.bits, row.b, row.c, 7, 0x20);
            }
            return result > 32 ? result + 1 : result;
        }

        static int UnquantizeTritQuint(int value, int bits, string pattern, int c, int width, int topBit)
        {
            int d = value >> bits;
            int m = value & ((1 << bits) - 1);
            int a = (m & 1) != 0 ? (1 << width) - 1 : 0;
            int b = 0;
            foreach (char symbol in pattern)
            {
                b = b << 1 | (symbol == '0' ? 0 : (m >> (symbol - 'a')) & 1);
            }
            int unq = (d * c + b) ^ a;
            return (a & topBit) | (unq >> 2);
        }

        static int Replicate(int value, int bits, int width)
        {
            int result = 0;
            for (int filled = 0; filled < width; filled += bits)
            {
                int shift = width - filled - bits;
                result |= shift >= 0 ? value << shift : value >> -shift;
            }
            return result;
        }

        /// <summary>value（0..maxValue）→ 反量化后最接近它的编码值。</summary>
        static int[] QuantizeTable(int maxValue, int levels, Func<int, int> unquantize)
        {
            var table = new int[maxValue + 1];
            for (int target = 0; target <= maxValue; ++target)
            {
                int best = 0;
                for (int value = 1; value < levels; ++value)
                {
                    if (Math.Abs(unquantize(value) - target) < Math.Abs(unquantize(best) - target)) { best = value; }
                }
                table[target] = best;
            }
            return table;
        }

        // 上传给着色器的表在一个 uint 缓冲里的位置，与 ASTCCompress.hlsl 的 ASTC_*_OFFSET 一致。
        public const int TritOffset = 0;
        public const int QuintOffset = TritOffset + 243;
        public const int ColorQuantOffset = QuintOffset + 125;
        public const int ColorUnquantOffset = ColorQuantOffset + 256;
        public const int WeightQuantOffset = ColorUnquantOffset + 256;
        public const int WeightUnquantOffset = WeightQuantOffset + 65;
        public const int InfillOffset = WeightUnquantOffset + 32;
        public const int GridNormOffset = InfillOffset + 64 * 2;
        public const int TableSize = GridNormOffset + 36;

        /// <summary>一档配置的全部查找表：ISE 编码、端点 0..255 与权重 0..64 的量化 / 反量化。</summary>
        public static uint[] BuildTables(Config config)
        {
            var tables = new uint[TableSize];
            void Copy(int[] source, int offset) { for (int i = 0; i < source.Length; ++i) { tables[offset + i] = (uint)source[i]; } }

            IseRange endpoint = config.endpointRange;
            IseRange weight = config.weightRange;
            Copy(TritEncodeTable(), TritOffset);
            Copy(QuintEncodeTable(), QuintOffset);
            Copy(QuantizeTable(255, endpoint.levels, value => UnquantizeColor(endpoint, value)), ColorQuantOffset);
            for (int i = 0; i < endpoint.levels; ++i) { tables[ColorUnquantOffset + i] = (uint)UnquantizeColor(endpoint, i); }
            Copy(QuantizeTable(64, weight.levels, value => UnquantizeWeight(weight, value)), WeightQuantOffset);
            for (int i = 0; i < weight.levels; ++i) { tables[WeightUnquantOffset + i] = (uint)UnquantizeWeight(weight, i); }
            int scale = (1024 + config.block / 2) / (config.block - 1);
            for (int y = 0; y < config.block; ++y)
            {
                int gy = (scale * y * (config.gridHeight - 1) + 32) >> 6;
                int jy = gy >> 4, fy = gy & 15;
                for (int x = 0; x < config.block; ++x)
                {
                    int gx = (scale * x * (config.gridWidth - 1) + 32) >> 6;
                    int jx = gx >> 4, fx = gx & 15;
                    int w11 = (fx * fy + 8) >> 4;
                    int[] corners = { jx + jy * config.gridWidth, jx + 1 + jy * config.gridWidth, jx + (jy + 1) * config.gridWidth, jx + 1 + (jy + 1) * config.gridWidth };
                    int[] weights = { 16 - fx - fy + w11, fx - w11, fy - w11, w11 };
                    int offset = InfillOffset + (y * config.block + x) * 2;
                    for (int i = 0; i < 4; ++i)
                    {
                        // 零权重角点可在网格外，着色器跳过它。
                        tables[offset] |= (uint)corners[i] << (i * 8);
                        tables[offset + 1] |= (uint)weights[i] << (i * 8);
                        if (weights[i] != 0) { tables[GridNormOffset + corners[i]] += (uint)weights[i]; }
                    }
                }
            }
            return tables;
        }
    }
}
