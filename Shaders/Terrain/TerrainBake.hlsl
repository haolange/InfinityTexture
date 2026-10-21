#ifndef TERRAIN_BAKE_INCLUDED
#define TERRAIN_BAKE_INCLUDED

// 地形烘焙：一张 splat 控制图的 4 个地形层按权重混合，按 EPageLayer 的通道布局写出各层。
// 所有输出都是"权重 × 值"，多张 splat 用 Blend One One 叠加后权重和为 1。

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#define DECLARE_TERRAIN_LAYER(i) \
    Texture2D _AlbedoTexture##i; \
    Texture2D _NormalTexture##i; \
    Texture2D _MaskTexture##i; \
    float4 _LayerParams##i; \
    float4 _LayerTileOffset##i; \
    float4 _MaskRemapScale##i; \
    float4 _MaskRemapOffset##i;

// _LayerParams: metallic, smoothness, hasMask, normalScale
// _LayerTileOffset: 从 scratch 的 0..1 到该层贴图 uv 的缩放与偏移
DECLARE_TERRAIN_LAYER(1)
DECLARE_TERRAIN_LAYER(2)
DECLARE_TERRAIN_LAYER(3)
DECLARE_TERRAIN_LAYER(4)

float4 _SplatTileOffset;
float4x4 _BakeMatrix;

TEXTURE2D(_SplatTexture);
SAMPLER(sampler_SplatTexture);
SAMPLER(Global_trilinear_repeat_sampler);

// 地形图层没有逐层高光等级，写默认值 0.5（F0 = 0.04）。
#define DEFAULT_SPECULAR 0.5

struct Varyings
{
    float2 uv0 : TEXCOORD0;
    float4 vertexCS : SV_POSITION;
};

struct BakeOutput
{
    float4 baseColor : SV_Target0;
#if defined(_PAGE_NORMAL) || defined(_PAGE_NORMAL_MASK)
    float4 normal : SV_Target1;
#endif
#if defined(_PAGE_NORMAL_MASK)
    float4 mask : SV_Target2;
#endif
};

struct LayerSample
{
    float3 baseColor;
    float2 normalXY;
    float4 mask;     // metallic, ao, specular, roughness
};

// 单位矩形由 SV_VertexID 生成，_BakeMatrix 把它放到 scratch 上与地形相交的区域。
Varyings vert(uint vertexID : SV_VertexID)
{
    float2 corner = float2(vertexID == 1 || vertexID == 2 || vertexID == 4, vertexID == 2 || vertexID == 4 || vertexID == 5);
    Varyings output;
    output.uv0 = corner;
    output.vertexCS = mul(_BakeMatrix, float4(corner, 0.1, 1.0));
    return output;
}

// 在连续图层 UV 上建立随机三角网格；页和 border 共用同一组坐标。
void StochasticUVs(float2 uv, out float2 uv0, out float2 uv1, out float2 uv2, out float3 weights)
{
    float2 grid = mul(float2x2(1, 0, -0.57735027, 1.15470054), uv * (3.464 * 0.5));
    float2 cell = floor(grid);
    float2 f = frac(grid);
    float z = 1 - f.x - f.y;
    float2 v0, v1, v2;
    if (z > 0)
    {
        weights = float3(z, f.y, f.x);
        v0 = cell; v1 = cell + float2(0, 1); v2 = cell + float2(1, 0);
    }
    else
    {
        weights = float3(-z, 1 - f.y, 1 - f.x);
        v0 = cell + 1; v1 = cell + float2(1, 0); v2 = cell + float2(0, 1);
    }
    uv0 = uv + frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), v0)) * 43758.5453);
    uv1 = uv + frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), v1)) * 43758.5453);
    uv2 = uv + frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), v2)) * 43758.5453);
}

// 与 URP 地形一致：有 mask 图时按 remap 读 (metallic, ao, height, smoothness)，
// 否则用图层的 metallic，光滑度取 albedo.a × smoothness，AO 为 1。
LayerSample SampleLayer(Texture2D albedoTexture, Texture2D normalTexture, Texture2D maskTexture, float4 params, float4 remapScale, float4 remapOffset, float4 tileOffset, float2 scratchUV)
{
    float2 uv = scratchUV * tileOffset.xy + tileOffset.zw;
    float2 dx = ddx(uv), dy = ddy(uv);
    float2 uv0, uv1, uv2;
    float3 weights;
    StochasticUVs(uv, uv0, uv1, uv2, weights);
    // 随机偏移在网格边界跳变，所有采样均使用偏移前的连续 UV 梯度。
    float4 a0 = albedoTexture.SampleGrad(Global_trilinear_repeat_sampler, uv0, dx, dy);
    float4 a1 = albedoTexture.SampleGrad(Global_trilinear_repeat_sampler, uv1, dx, dy);
    float4 a2 = albedoTexture.SampleGrad(Global_trilinear_repeat_sampler, uv2, dx, dy);
    weights *= float3(a0.r, a1.r, a2.r) + 1.0 / 1024.0;
    float maxWeight = max(weights.x, max(weights.y, weights.z));
    float transition = 0.15 * maxWeight;
    weights = saturate((weights - (maxWeight - transition)) / transition);
    weights /= dot(weights, 1.0);
    float4 albedo = a0 * weights.x + a1 * weights.y + a2 * weights.z;

    float3 n0 = UnpackNormalScale(normalTexture.SampleGrad(Global_trilinear_repeat_sampler, uv0, dx, dy), params.w);
    float3 n1 = UnpackNormalScale(normalTexture.SampleGrad(Global_trilinear_repeat_sampler, uv1, dx, dy), params.w);
    float3 n2 = UnpackNormalScale(normalTexture.SampleGrad(Global_trilinear_repeat_sampler, uv2, dx, dy), params.w);
    float2 normalXY = (n0 * weights.x + n1 * weights.y + n2 * weights.z).xy;
    float4 mask = maskTexture.SampleGrad(Global_trilinear_repeat_sampler, uv0, dx, dy) * weights.x
        + maskTexture.SampleGrad(Global_trilinear_repeat_sampler, uv1, dx, dy) * weights.y
        + maskTexture.SampleGrad(Global_trilinear_repeat_sampler, uv2, dx, dy) * weights.z;
    mask = mask * remapScale + remapOffset;

    bool hasMask = params.z > 0.5;
    float metallic = hasMask ? mask.r : params.x;
    float occlusion = hasMask ? mask.g : 1.0;
    float smoothness = hasMask ? mask.a : albedo.a * params.y;

    LayerSample result;
    result.baseColor = albedo.rgb;
    result.normalXY = normalXY;
    result.mask = float4(metallic, occlusion, DEFAULT_SPECULAR, 1.0 - smoothness);
    return result;
}

#define SAMPLE_TERRAIN_LAYER(i, uv) SampleLayer(_AlbedoTexture##i, _NormalTexture##i, _MaskTexture##i, _LayerParams##i, _MaskRemapScale##i, _MaskRemapOffset##i, _LayerTileOffset##i, uv)

void Accumulate(inout BakeOutput output, float weight, LayerSample layer)
{
    output.baseColor += weight * float4(layer.baseColor, 1.0);
#if defined(_PAGE_NORMAL) || defined(_PAGE_NORMAL_MASK)
    // 先编码到 0..1 再乘权重，叠加后仍是 0.5 × n + 0.5。
    float2 encoded = layer.normalXY * 0.5 + 0.5;
    output.normal += weight * float4(0.0, encoded.y, 0.0, encoded.x);
#endif
#if defined(_PAGE_NORMAL_MASK)
    output.mask += weight * layer.mask;
#endif
}

BakeOutput frag(Varyings input)
{
    uint width, height;
    _SplatTexture.GetDimensions(width, height);
    float2 size = float2(width, height);
    float2 terrainUV = input.uv0 * _SplatTileOffset.xy + _SplatTileOffset.zw;
    float2 controlUV = (terrainUV * (size - 1.0) + 0.5) / size;
    float4 weights = saturate(SAMPLE_TEXTURE2D(_SplatTexture, sampler_SplatTexture, controlUV));
#ifdef TERRAIN_SPLAT_ADDPASS
    clip(dot(weights, 1.0) <= 0.005 ? -1.0 : 1.0);
#endif

    BakeOutput output = (BakeOutput)0;
    Accumulate(output, weights.r, SAMPLE_TERRAIN_LAYER(1, input.uv0));
    Accumulate(output, weights.g, SAMPLE_TERRAIN_LAYER(2, input.uv0));
    Accumulate(output, weights.b, SAMPLE_TERRAIN_LAYER(3, input.uv0));
    Accumulate(output, weights.a, SAMPLE_TERRAIN_LAYER(4, input.uv0));
    return output;
}

#endif
