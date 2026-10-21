#ifndef ETC_COMPRESS_INCLUDED
#define ETC_COMPRESS_INCLUDED

// ETC2 RGBA8 块编码：RGB 用 ETC2（个别/差分模式），A 用 EAC。输入为 0..1。

#define BLOCK_MODE_INDIVIDUAL				0
#define BLOCK_MODE_DIFFERENTIAL				1
#define NUM_RGB_TABLES 8

// EAC 修正表（ETC2 规范）：下标 0..3 为负修正，4..7 为正修正。
static const int EAC_MODIFIERS[16][8] =
{
    { -3, -6,  -9, -15, 2, 5, 8, 14 },
    { -3, -7, -10, -13, 2, 6, 9, 12 },
    { -2, -5,  -8, -13, 1, 4, 7, 12 },
    { -2, -4,  -6, -13, 1, 3, 5, 12 },
    { -3, -6,  -8, -12, 2, 5, 7, 11 },
    { -3, -7,  -9, -11, 2, 6, 8, 10 },
    { -4, -7,  -8, -11, 3, 6, 7, 10 },
    { -3, -5,  -8, -11, 2, 4, 7, 10 },
    { -2, -6,  -8, -10, 1, 5, 7,  9 },
    { -2, -5,  -8, -10, 1, 4, 7,  9 },
    { -2, -4,  -8, -10, 1, 3, 7,  9 },
    { -2, -5,  -7, -10, 1, 4, 6,  9 },
    { -3, -4,  -7, -10, 2, 3, 6,  9 },
    { -1, -2,  -3, -10, 0, 1, 2,  9 },
    { -4, -6,  -8,  -9, 3, 5, 7,  8 },
    { -3, -5,  -7,  -9, 2, 4, 6,  8 }
};

static const float4 rgb_distance_tables[8] =
{
    float4(-8, -2, 2, 8),
    float4(-17, -5, 5, 17),
    float4(-29, -9, 9, 29),
    float4(-42, -13, 13, 42),
    float4(-60, -18, 18, 60),
    float4(-80, -24, 24, 80),
    float4(-106, -33, 33, 106),
    float4(-183, -47, 47, 183)
};

int3 FloatColorToUint555(in float3 FloatColor)
{
    float3 Scale = float3(31.f, 31.f, 31.f);
    float3 ColorScaled = round(saturate(FloatColor) * Scale);
    return int3((int) ColorScaled.r, (int) ColorScaled.g, (int) ColorScaled.b);
}

float3 ExpandColor444(in int3 Color444)
{
    int3 Color888 = (Color444 << 4) + Color444;
    return Color888 / 255.f;
}

float3 ExpandColor555(in int3 Color555)
{
    int3 Color888 = (Color555 << 3) + (Color555 >> 2);
    return Color888 / 255.f;
}

uint SwapEndian32(in uint x)
{
    return
		((x & 0x0000ff) << 24) |
		((x & 0x00ff00) << 8) |
		((x & 0xff0000) >> 8) |
		(x >> 24);
}

float Luminance(float3 LinearColor)
{
    return dot(LinearColor, float3(0.3, 0.59, 0.11));
}

float max3(float a, float b, float c)
{
    return max(a, max(b, c));
}

float min3(float a, float b, float c)
{
    return min(a, min(b, c));
}

uint SelectRGBTableIndex(float LuminanceR)
{
    uint Out = 7;
    float Range = (LuminanceR + LuminanceR * 0.1) * 255.0;
    
    if (Range < 8.0)
    {
        Out = 0;
    } else if (Range < 17.0) {
        Out = 1;
    } else if (Range < 29.0) {
        Out = 2;
    } else if (Range < 42.0) {
        Out = 3;
    } else if (Range < 60.0) {
        Out = 4;
    } else if (Range < 80.0) {
        Out = 5;
    } else if (Range < 106.0) {
        Out = 6;
    }
    return Out;
}


void FindPixelWeights(in float3 Block[16], in float3 BaseColor, in uint TableIdx, in int StartX, in int EndX, in int StartY, in int EndY, out uint SubBlockWeights)
{
    SubBlockWeights = 0;
	
    float TableRangeMax = rgb_distance_tables[TableIdx].w / 255.f;
    float BaseLum = Luminance(BaseColor);

    for (int Y = StartY; Y < EndY; ++Y)
    {
        for (int X = StartX; X < EndX; ++X)
        {
            float3 OrigColor = Block[4 * Y + X];
            float Diff = Luminance(OrigColor) - BaseLum;
            int EncIndex = 0;
            if (Diff < 0.f)
            {
                EncIndex = (-Diff * 1.58) > TableRangeMax ? 3 : 2;
            }
            else
            {
                EncIndex = (Diff * 1.58) > TableRangeMax ? 1 : 0;
            }
            int IndexInBlock = X * 4 + Y;
            SubBlockWeights |= ((EncIndex & 1) << IndexInBlock) | ((EncIndex >> 1) << (16 + IndexInBlock));
        }
    }
}

uint2 CompressBlock_ETC2_RGB(in float3 Block[16])
{
	// Always use side-by-side mode (flip bit set to 0).
    uint FlipBit = 0;
	
    float3 BaseColor1_Float = (Block[0] + Block[1] + Block[4] + Block[5] + Block[8] + Block[9] + Block[12] + Block[13]) * 0.125;
    float3 BaseColor2_Float = (Block[2] + Block[3] + Block[6] + Block[7] + Block[10] + Block[11] + Block[14] + Block[15]) * 0.125;
		
    int3 BaseColor1 = FloatColorToUint555(BaseColor1_Float);
    int3 BaseColor2 = FloatColorToUint555(BaseColor2_Float);
    int3 Diff = BaseColor2 - BaseColor1;

    uint ColorBits;
    float3 BaseColor1_Quant, BaseColor2_Quant;

    uint BlockMode;
    int3 MinDiff = { -4, -4, -4 }, MaxDiff = { 3, 3, 3 };
    if (all(Diff >= MinDiff) && all(Diff <= MaxDiff))
    {
		// We can use differential mode.
        BlockMode = BLOCK_MODE_DIFFERENTIAL;
        ColorBits = ((Diff.b & 7) << 16) | (BaseColor1.b << 19) | ((Diff.g & 7) << 8) | (BaseColor1.g << 11) | (Diff.r & 7) | (BaseColor1.r << 3);
        BaseColor1_Quant = ExpandColor555(BaseColor1);
        BaseColor2_Quant = ExpandColor555(BaseColor2);
    }
    else
    {
		// We must use the lower precision individual mode.
        BlockMode = BLOCK_MODE_INDIVIDUAL;
        BaseColor1 >>= 1;
        BaseColor2 >>= 1;
        ColorBits = (BaseColor1.b << 20) | (BaseColor2.b << 16) | (BaseColor1.g << 12) | (BaseColor2.g << 8) | (BaseColor1.r << 4) | BaseColor2.r;
        BaseColor1_Quant = ExpandColor444(BaseColor1);
        BaseColor2_Quant = ExpandColor444(BaseColor2);
    }

    float l00 = Luminance(Block[0]);
    float l08 = Luminance(Block[8]);
    float l13 = Luminance(Block[13]);
    float LuminanceR1 = (max3(l00, l08, l13) - min3(l00, l08, l13)) * 0.5;
    uint SubBlock1TableIdx = SelectRGBTableIndex(LuminanceR1);
    uint SubBlock1Weights = 0;
    FindPixelWeights(Block, BaseColor1_Quant, SubBlock1TableIdx, 0, 2, 0, 4, SubBlock1Weights);

    float l02 = Luminance(Block[2]);
    float l10 = Luminance(Block[10]);
    float l15 = Luminance(Block[15]);
    float LuminanceR2 = (max3(l02, l10, l15) - min3(l02, l10, l15)) * 0.5;
    uint SubBlock2TableIdx = SelectRGBTableIndex(LuminanceR2);
    uint SubBlock2Weights = 0;
    FindPixelWeights(Block, BaseColor2_Quant, SubBlock2TableIdx, 2, 4, 0, 4, SubBlock2Weights);
	
	// Both these values need to be big-endian. We can build ModeBits directly in big-endian layout, but for IndexBits
	// it's too hard, so we'll just swap here.
    uint ModeBits = (SubBlock1TableIdx << 29) | (SubBlock2TableIdx << 26) | (BlockMode << 25) | (FlipBit << 24) | ColorBits;
    uint IndexBits = SwapEndian32(SubBlock1Weights | SubBlock2Weights);

    return uint2(ModeBits, IndexBits);
}

// EAC alpha：解码值 = clamp(基准 + 修正表[索引] × 倍率)。逐表求能覆盖 [最小, 最大] 的倍率与基准，取总误差最小的表。
// 64 位按大端存放：基准 8 位 | 倍率 4 位 | 表号 4 位 | 16 个 3 位索引（像素按列优先）。
uint2 CompressBlock_ETC2_Alpha(in float BlockA[16])
{
    int alpha[16];
    int minAlpha = 255, maxAlpha = 0;
    for (int k = 0; k < 16; ++k)
    {
        alpha[k] = (int)round(saturate(BlockA[k]) * 255.0);
        minAlpha = min(minAlpha, alpha[k]);
        maxAlpha = max(maxAlpha, alpha[k]);
    }

    if (minAlpha == maxAlpha)
    {
        // 倍率 0 精确表示常量，索引无需搜索。
        return uint2(SwapEndian32((uint)minAlpha << 24), 0);
    }

    int bestTable = 0, bestMultiplier = 1, bestBase = minAlpha;
    int bestError = 0x7FFFFFFF;
    for (int table = 0; table < 16; ++table)
    {
        uint span = (uint)(EAC_MODIFIERS[table][7] - EAC_MODIFIERS[table][3]);
        int multiplier = (int)clamp(((uint)(maxAlpha - minAlpha) + (span >> 1)) / span, 1u, 15u);
        int base = min((int)((uint)max(0, minAlpha + maxAlpha - (EAC_MODIFIERS[table][7] + EAC_MODIFIERS[table][3]) * multiplier + 1) >> 1), 255);

        int decoded[8];
        [unroll] for (int i = 0; i < 8; ++i) { decoded[i] = clamp(base + EAC_MODIFIERS[table][i] * multiplier, 0, 255); }
        int error = 0;
        for (int p = 0; p < 16; ++p)
        {
            int best = 0x7FFFFFFF;
            for (int i = 0; i < 8; ++i)
            {
                int d = decoded[i] - alpha[p];
                best = min(best, d * d);
            }
            error += best;
            if (error >= bestError) { break; }
        }
        if (error < bestError) { bestError = error; bestTable = table; bestMultiplier = multiplier; bestBase = base; }
        if (bestError == 0) { break; }
    }

    uint indicesHigh = 0, indicesLow = 0;  // 48 位索引的高 16 位与低 32 位
    for (int p = 0; p < 16; ++p)
    {
        int selected = 0, best = 0x7FFFFFFF;
        for (int i = 0; i < 8; ++i)
        {
            int d = clamp(bestBase + EAC_MODIFIERS[bestTable][i] * bestMultiplier, 0, 255) - alpha[p];
            if (d * d < best) { best = d * d; selected = i; }
        }
        int column = p & 3, row = p >> 2;          // BlockA 按行优先
        int shift = 45 - 3 * (column * 4 + row);   // EAC 按列优先，第一个像素在最高位
        if (shift >= 32) { indicesHigh |= (uint)selected << (shift - 32); }
        else
        {
            indicesLow |= (uint)selected << shift;
            if (shift > 29) { indicesHigh |= (uint)selected >> (32 - shift); }
        }
    }

    uint high = (uint)bestBase << 24 | (uint)bestMultiplier << 20 | (uint)bestTable << 16 | indicesHigh;
    return uint2(SwapEndian32(high), SwapEndian32(indicesLow));
}

uint4 CompressBlock_ETC2_RGBA(in float3 BlockRGB[16], in float BlockA[16])
{
    uint2 CompressedRGB = CompressBlock_ETC2_RGB(BlockRGB);
    uint2 CompressedAlpha = CompressBlock_ETC2_Alpha(BlockA);
    return uint4(CompressedAlpha, CompressedRGB);
}

#endif
