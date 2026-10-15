#ifndef BC_COMPRESS_INCLUDED
#define BC_COMPRESS_INCLUDED

// BC3（DXT5）块编码：RGB 用 BC1 式的 565 端点 + 2 位索引，A 用 8 位端点 + 3 位索引。输入为 0..1。

// Convert float3 color to 565 uint using 'ceil' arithmetic
// Color parameter is inout and is modified to match the converted value
uint Float3ToUint565_Ceil(inout float3 Color)
{
    float3 Scale = float3(31.f, 63.f, 31.f);
    float3 ColorScaled = ceil(saturate(Color) * Scale);
    uint ColorPacked = ((uint) ColorScaled.r << 11) | ((uint) ColorScaled.g << 5) | (uint) ColorScaled.b;
    Color = ColorScaled / Scale;

    return ColorPacked;
}

// Convert float3 color to 565 uint using 'floor' arithmetic
// Color parameter is inout and is modified to match the converted value
uint Float3ToUint565_Floor(inout float3 Color)
{
    float3 Scale = float3(31.f, 63.f, 31.f);
    float3 ColorScaled = floor(saturate(Color) * Scale);
    uint ColorPacked = ((uint) ColorScaled.r << 11) | ((uint) ColorScaled.g << 5) | (uint) ColorScaled.b;
    Color = ColorScaled / Scale;

    return ColorPacked;
}

// Get min and max values in a single channel block
void GetMinMax(in float Block[16], out float OutMin, out float OutMax)
{
    OutMin = Block[0];
    OutMax = Block[0];

    for (int i = 1; i < 16; ++i)
    {
        OutMin = min(OutMin, Block[i]);
        OutMax = max(OutMax, Block[i]);
    }
}

// Get min and max values in a single three channel block
void GetMinMax(in float3 Block[16], out float3 OutMin, out float3 OutMax)
{
    OutMin = Block[0];
    OutMax = Block[0];

    for (int i = 1; i < 16; ++i)
    {
        OutMin = min(OutMin, Block[i]);
        OutMax = max(OutMax, Block[i]);
    }
}

// Calculate the final packed indices for a color block
uint GetPackedColorBlockIndices(in float3 Block[16], in float3 MinColor, in float3 MaxColor)
{
    uint PackedIndices = 0;

	// Project onto max->min color vector and segment into range [0,3]
    float3 Range = MinColor - MaxColor;
    float Scale = 3.f / dot(Range, Range);
    float3 ScaledRange = Range * Scale;
    float Bias = (dot(MaxColor, MaxColor) - dot(MaxColor, MinColor)) * Scale;
	
    for (int i = 15; i >= 0; --i)
    {
		// Compute the distance index for this element
        uint Index = round(dot(Block[i], ScaledRange) + Bias);
		// Convert distance index into the BC index
        Index += (Index > 0) - (3 * (Index == 3));
		// OR into the final PackedIndices
        PackedIndices = (PackedIndices << 2) | Index;
    }

    return PackedIndices;
}

// Calculate the final packed indices for an alpha block
// The results are in their final location of the uint2 indices and will need ORing with the min and max alpha
uint2 GetPackedAlphaBlockIndices(in float Block[16], in float MinAlpha, in float MaxAlpha)
{
    uint2 PackedIndices = 0;

	// Segment alpha max->min into range [0,7]
    float Range = MinAlpha - MaxAlpha;
    float Scale = 7.f / Range;
    float Bias = -Scale * MaxAlpha;

    uint i = 0;
	// The first 5 elements of the block will go into the top 16 bits of the x component
    for (i = 0; i < 5; ++i)
    {
		// Compute the distance index for this element
        uint Index = round(Block[i] * Scale + Bias);
		// Convert distance index into the BC index
        Index += (Index > 0) - (7 * (Index == 7));
		// OR into the final PackedIndices
        PackedIndices.x |= (Index << (i * 3 + 16));
    }

	// The 6th element is split across the x and y components
	{
        i = 5;
        uint Index = round(Block[i] * Scale + Bias);
        Index += (Index > 0) - (7 * (Index == 7));
        PackedIndices.x |= (Index << 31);
        PackedIndices.y |= (Index >> 1);
    }

	// The rest of the elements fill the y component
    for (i = 6; i < 16; ++i)
    {
        uint Index = round(Block[i] * Scale + Bias);
        Index += (Index > 0) - (7 * (Index == 7 ? 1 : 0));
        PackedIndices.y |= (Index << (i * 3 - 16));
    }

    return PackedIndices;
}

// Compress a BC3 block
uint4 CompressBC3Block(in float3 BlockRGB[16], in float BlockA[16])
{
    float3 MinColor, MaxColor;
    GetMinMax(BlockRGB, MinColor, MaxColor);

    uint MinColor565 = Float3ToUint565_Floor(MinColor);
    uint MaxColor565 = Float3ToUint565_Ceil(MaxColor);

    float MinAlpha, MaxAlpha;
    GetMinMax(BlockA, MinAlpha, MaxAlpha);

    uint MinAlphaUint = (uint) round(MinAlpha * 255.f);
    uint MaxAlphaUint = (uint) round(MaxAlpha * 255.f);

    uint ColorIndices = 0;
    if (MinColor565 < MaxColor565)
    {
        ColorIndices = GetPackedColorBlockIndices(BlockRGB, MinColor, MaxColor);
    }

    uint2 AlphaIndices = 0;
    if (MinAlphaUint < MaxAlphaUint)
    {
        AlphaIndices = GetPackedAlphaBlockIndices(BlockA, MinAlpha, MaxAlpha);
    }

    return uint4((MinAlphaUint << 8) | MaxAlphaUint | AlphaIndices.x, AlphaIndices.y, (MinColor565 << 16) | MaxColor565, ColorIndices);
}

#endif
