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

uint2 CompressBC1Block(in float3 block[16])
{
    float3 low, high;
    GetMinMax(block, low, high);
    uint low565 = Float3ToUint565_Floor(low);
    uint high565 = Float3ToUint565_Ceil(high);
    uint indices = low565 < high565 ? GetPackedColorBlockIndices(block, low, high) : 0;
    return uint2((low565 << 16) | high565, indices);
}

uint2 CompressBC4Block(in float block[16])
{
    float low, high;
    GetMinMax(block, low, high);
    uint low8 = (uint)round(low * 255.0);
    uint high8 = (uint)round(high * 255.0);
    uint2 indices = 0;
    if (low8 < high8) { indices = GetPackedAlphaBlockIndices(block, low, high); }
    return uint2((low8 << 8) | high8 | indices.x, indices.y);
}

uint4 CompressBC3Block(in float3 blockRGB[16], in float blockA[16])
{
    return uint4(CompressBC4Block(blockA), CompressBC1Block(blockRGB));
}

#endif
