#ifndef ASTC_COMPRESS_INCLUDED
#define ASTC_COMPRESS_INCLUDED

// ASTC LDR 块编码：单分区、CEM 12（RGBA 直接端点），权重网格可小于块。
// 单平面：RGBA 共用一套权重。双平面：RGB 一套、A 一套（通道选择为 3），数据层用它让 A 独立变化。
// 固定配置与全部查找表来自 C# AstcLayout（唯一来源），这里只做：
// 主轴求端点 → 端点量化（避开 blue-contraction）→ 逐像素理想权重 → 按解码端 infill 反推网格权重 → ISE 打包。

// _AstcTables 里各表的起点，与 C# AstcLayout.*Offset 一致。
#define ASTC_TRIT_OFFSET            0
#define ASTC_QUINT_OFFSET           243
#define ASTC_COLOR_QUANT_OFFSET     368
#define ASTC_COLOR_UNQUANT_OFFSET   624
#define ASTC_WEIGHT_QUANT_OFFSET    880
#define ASTC_WEIGHT_UNQUANT_OFFSET  945

#define ASTC_ISE_BITS   0
#define ASTC_ISE_TRIT   1
#define ASTC_ISE_QUINT  2

StructuredBuffer<uint> _AstcTables;

struct AstcConfig
{
    uint blockSize;      // 块边长：4 / 6 / 8
    uint gridWidth;
    uint gridHeight;
    uint blockMode;      // 11 位块模式
    uint dualPlane;      // 1：A 通道单独一个权重平面
    uint weightStreamBits; // 权重流总位数，双平面时通道选择紧贴在它下面
    uint weightKind;     // ASTC_ISE_*
    uint weightBits;
    uint endpointKind;
    uint endpointBits;
};

// 一块的像素（0..255，行优先），由调用方填好。
static float4 s_AstcTexels[64];
// 待打包的整数序列。
static uint s_AstcValues[64];

void AstcPutBits(inout uint stream[4], inout uint position, uint value, uint count)
{
    if (count == 0) { return; }
    value &= count == 32 ? 0xFFFFFFFFu : (1u << count) - 1u;
    uint word = position >> 5;
    uint shift = position & 31;
    stream[word] |= value << shift;
    if (shift + count > 32) { stream[word + 1] |= value >> (32 - shift); }
    position += count;
}

// 规范的 integer sequence encoding：trit 每 5 个值一组、quint 每 3 个值一组，与低位交织。
// 末组不足时补 0，编码表保证补出的高位也为 0。
void AstcPutSequence(inout uint stream[4], inout uint position, uint count, uint kind, uint bits)
{
    if (kind == ASTC_ISE_BITS)
    {
        for (uint i = 0; i < count; ++i) { AstcPutBits(stream, position, s_AstcValues[i], bits); }
    }
    else if (kind == ASTC_ISE_TRIT)
    {
        for (uint g = 0; g < count; g += 5)
        {
            uint m[5]; uint index = 0; uint scale = 1;
            for (uint j = 0; j < 5; ++j)
            {
                uint v = g + j < count ? s_AstcValues[g + j] : 0;
                m[j] = v & ((1u << bits) - 1u);
                index += (v >> bits) * scale;
                scale *= 3;
            }
            uint t = _AstcTables[ASTC_TRIT_OFFSET + index];
            AstcPutBits(stream, position, m[0], bits); AstcPutBits(stream, position, t, 2);
            AstcPutBits(stream, position, m[1], bits); AstcPutBits(stream, position, t >> 2, 2);
            AstcPutBits(stream, position, m[2], bits); AstcPutBits(stream, position, t >> 4, 1);
            AstcPutBits(stream, position, m[3], bits); AstcPutBits(stream, position, t >> 5, 2);
            AstcPutBits(stream, position, m[4], bits); AstcPutBits(stream, position, t >> 7, 1);
        }
    }
    else
    {
        for (uint g = 0; g < count; g += 3)
        {
            uint m[3]; uint index = 0; uint scale = 1;
            for (uint j = 0; j < 3; ++j)
            {
                uint v = g + j < count ? s_AstcValues[g + j] : 0;
                m[j] = v & ((1u << bits) - 1u);
                index += (v >> bits) * scale;
                scale *= 5;
            }
            uint q = _AstcTables[ASTC_QUINT_OFFSET + index];
            AstcPutBits(stream, position, m[0], bits); AstcPutBits(stream, position, q, 3);
            AstcPutBits(stream, position, m[1], bits); AstcPutBits(stream, position, q >> 3, 2);
            AstcPutBits(stream, position, m[2], bits); AstcPutBits(stream, position, q >> 5, 2);
        }
    }
}

uint4 AstcQuantizeColor(float4 color)
{
    uint4 v = (uint4)round(clamp(color, 0.0, 255.0));
    return uint4(_AstcTables[ASTC_COLOR_QUANT_OFFSET + v.r], _AstcTables[ASTC_COLOR_QUANT_OFFSET + v.g],
                 _AstcTables[ASTC_COLOR_QUANT_OFFSET + v.b], _AstcTables[ASTC_COLOR_QUANT_OFFSET + v.a]);
}

float4 AstcUnquantizeColor(uint4 q)
{
    return float4(_AstcTables[ASTC_COLOR_UNQUANT_OFFSET + q.r], _AstcTables[ASTC_COLOR_UNQUANT_OFFSET + q.g],
                  _AstcTables[ASTC_COLOR_UNQUANT_OFFSET + q.b], _AstcTables[ASTC_COLOR_UNQUANT_OFFSET + q.a]);
}

// 主轴：在 mask 选中的通道上对协方差矩阵做幂迭代，再取像素在主轴上的投影范围作为两个端点。
void AstcPrincipalEndpoints(uint count, float4 mask, out float4 e0, out float4 e1)
{
    float4 mean = 0;
    for (uint meanIndex = 0; meanIndex < count; ++meanIndex) { mean += s_AstcTexels[meanIndex]; }
    mean /= count;

    float4x4 covariance = 0;
    for (uint covarianceIndex = 0; covarianceIndex < count; ++covarianceIndex)
    {
        float4 d = (s_AstcTexels[covarianceIndex] - mean) * mask;
        covariance[0] += d * d.x;
        covariance[1] += d * d.y;
        covariance[2] += d * d.z;
        covariance[3] += d * d.w;
    }

    float4 axis = mask;
    for (uint k = 0; k < 8; ++k)
    {
        axis = mul(covariance, axis);
        float scale = max(max(abs(axis.x), abs(axis.y)), max(abs(axis.z), abs(axis.w)));
        axis = scale > 1e-6 ? axis / scale : mask;
    }
    axis = normalize(axis);

    float tMin = 1e9, tMax = -1e9;
    for (uint projectionIndex = 0; projectionIndex < count; ++projectionIndex)
    {
        float t = dot((s_AstcTexels[projectionIndex] - mean) * mask, axis);
        tMin = min(tMin, t);
        tMax = max(tMax, t);
    }
    e0 = clamp(mean + axis * tMin, 0.0, 255.0);
    e1 = clamp(mean + axis * tMax, 0.0, 255.0);
}

uint4 CompressBlock_ASTC(AstcConfig config)
{
    uint size = config.blockSize;
    uint count = size * size;

    // 端点：双平面时主轴只看 RGB，A 端点取最小 / 最大值。
    // 量化后若 e1 的 RGB 和小于 e0，解码端会交换并做 blue-contraction，这里提前交换避开。
    bool dual = config.dualPlane != 0;
    float4 planeMask = dual ? float4(1, 1, 1, 0) : float4(1, 1, 1, 1);
    float4 e0, e1;
    AstcPrincipalEndpoints(count, planeMask, e0, e1);
    if (dual)
    {
        float aMin = 255, aMax = 0;
        for (uint i = 0; i < count; ++i) { aMin = min(aMin, s_AstcTexels[i].a); aMax = max(aMax, s_AstcTexels[i].a); }
        e0.a = aMin;
        e1.a = aMax;
    }
    uint4 q0 = AstcQuantizeColor(e0);
    uint4 q1 = AstcQuantizeColor(e1);
    float4 u0 = AstcUnquantizeColor(q0);
    float4 u1 = AstcUnquantizeColor(q1);
    if (u1.r + u1.g + u1.b < u0.r + u0.g + u0.b)
    {
        uint4 q = q0; q0 = q1; q1 = q;
        float4 u = u0; u0 = u1; u1 = u;
    }

    // 网格权重：按解码端的 infill（规范 Weight Infill）把逐像素理想权重分摊回网格点再归一。
    float4 axis = (u1 - u0) * planeMask;
    float axisLength2 = dot(axis, axis);
    float alphaRange = u1.a - u0.a;
    float gridSum[36];
    float gridAlphaSum[36];
    float gridNorm[36];
    for (uint g = 0; g < 36; ++g) { gridSum[g] = 0; gridAlphaSum[g] = 0; gridNorm[g] = 0; }

    uint scaleFactor = (1024 + size / 2) / (size - 1);
    for (uint t = 0; t < size; ++t)
    {
        uint gt = (scaleFactor * t * (config.gridHeight - 1) + 32) >> 6;
        uint jt = gt >> 4, ft = gt & 15;
        for (uint s = 0; s < size; ++s)
        {
            uint gs = (scaleFactor * s * (config.gridWidth - 1) + 32) >> 6;
            uint js = gs >> 4, fs = gs & 15;

            float4 texel = s_AstcTexels[t * size + s];
            float ideal = axisLength2 > 0 ? saturate(dot((texel - u0) * planeMask, axis) / axisLength2) : 0;
            float idealAlpha = alphaRange != 0 ? saturate((texel.a - u0.a) / alphaRange) : 0;

            uint w11 = (fs * ft + 8) >> 4;
            uint w10 = ft - w11;
            uint w01 = fs - w11;
            uint w00 = 16 - fs - ft + w11;
            uint corners[4] = { js + jt * config.gridWidth, js + 1 + jt * config.gridWidth, js + (jt + 1) * config.gridWidth, js + 1 + (jt + 1) * config.gridWidth };
            uint cornerWeights[4] = { w00, w01, w10, w11 };
            for (uint c = 0; c < 4; ++c)
            {
                if (cornerWeights[c] == 0) { continue; }
                gridSum[corners[c]] += ideal * cornerWeights[c];
                gridAlphaSum[corners[c]] += idealAlpha * cornerWeights[c];
                gridNorm[corners[c]] += cornerWeights[c];
            }
        }
    }

    // 打包：低位是配置与端点，权重从最高位向下倒着写。
    uint block[4] = { 0, 0, 0, 0 };
    uint position = 0;
    AstcPutBits(block, position, config.blockMode, 11);
    AstcPutBits(block, position, 0, 2);   // 分区数 - 1
    AstcPutBits(block, position, 12, 4);  // CEM 12
    s_AstcValues[0] = q0.r; s_AstcValues[1] = q1.r;
    s_AstcValues[2] = q0.g; s_AstcValues[3] = q1.g;
    s_AstcValues[4] = q0.b; s_AstcValues[5] = q1.b;
    s_AstcValues[6] = q0.a; s_AstcValues[7] = q1.a;
    AstcPutSequence(block, position, 8, config.endpointKind, config.endpointBits);

    // 双平面时每个网格点依次写 RGB 平面、A 平面的权重。
    uint gridCount = config.gridWidth * config.gridHeight;
    uint planes = dual ? 2 : 1;
    for (uint w = 0; w < gridCount; ++w)
    {
        float weight = gridNorm[w] > 0 ? gridSum[w] / gridNorm[w] : 0;
        float alphaWeight = gridNorm[w] > 0 ? gridAlphaSum[w] / gridNorm[w] : 0;
        s_AstcValues[w * planes] = _AstcTables[ASTC_WEIGHT_QUANT_OFFSET + (uint)round(weight * 64.0)];
        if (dual) { s_AstcValues[w * planes + 1] = _AstcTables[ASTC_WEIGHT_QUANT_OFFSET + (uint)round(alphaWeight * 64.0)]; }
    }
    uint weights[4] = { 0, 0, 0, 0 };
    position = 0;
    AstcPutSequence(weights, position, gridCount * planes, config.weightKind, config.weightBits);

    // 通道选择紧贴在权重流下面。
    if (dual)
    {
        position = 128 - config.weightStreamBits - 2;
        AstcPutBits(block, position, 3, 2);
    }

    return uint4(block[0] | reversebits(weights[3]), block[1] | reversebits(weights[2]),
                 block[2] | reversebits(weights[1]), block[3] | reversebits(weights[0]));
}

#endif
