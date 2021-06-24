#ifndef PageCommon
#define PageCommon

#include "../Common/StochasticSampling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

float4 _SplatTileOffset;
float4 _SurfaceTileOffset;
float4x4 _Matrix_MVP;

TEXTURE2D(_SplatTexture);
TEXTURE2D(_AlbedoTexture1);
TEXTURE2D(_AlbedoTexture2);
TEXTURE2D(_AlbedoTexture3);
TEXTURE2D(_AlbedoTexture4);
TEXTURE2D(_NormalTexture1);
TEXTURE2D(_NormalTexture2);
TEXTURE2D(_NormalTexture3);
TEXTURE2D(_NormalTexture4);

SAMPLER(sampler_SplatTexture);
SAMPLER(Global_trilinear_repeat_sampler);

struct Attributes
{
    float2 uv0 : TEXCOORD0;
    float4 vertexOS : POSITION;
};

struct Varyings
{
    float2 uv0 : TEXCOORD0;
    float4 vertexCS : SV_POSITION;
};

Varyings vert(Attributes input)
{
    Varyings output;
    output.uv0 = input.uv0;
    output.vertexCS = mul(_Matrix_MVP, input.vertexOS);
    return output;
}

Varyings vertTriangle(Attributes input)
{
    Varyings output;
	output.vertexCS = float4(input.vertexOS.x, -input.vertexOS.y, 0, 1);
	output.uv0 = (input.vertexOS.xy + 1) * 0.5;
    return output;
}

void frag(Varyings input, out float4 ColorBuffer : SV_Target0, out float4 NormalBuffer : SV_Target1)
{
    float4 splatMap = saturate(_SplatTexture.Sample(sampler_SplatTexture, input.uv0 * _SplatTileOffset.xy + _SplatTileOffset.zw));
    
#ifdef TERRAIN_SPLAT_ADDPASS
    clip(splatMap.x + splatMap.y + splatMap.z + splatMap.w <= 0.005h ? -1.0h : 1.0h);
#endif
    
    float2 transUv = input.uv0 * _SurfaceTileOffset.xy + _SurfaceTileOffset.zw;

    /*float4 Diffuse1 = _AlbedoTexture1.Sample(Global_trilinear_repeat_sampler, transUv);
    float3 Normal1 = UnpackNormalScale(_NormalTexture1.Sample(Global_trilinear_repeat_sampler, transUv), 1);

    float4 Diffuse2 = _AlbedoTexture2.Sample(Global_trilinear_repeat_sampler, transUv);
    float3 Normal2 = UnpackNormalScale(_NormalTexture2.Sample(Global_trilinear_repeat_sampler, transUv), 1);

    float4 Diffuse3 = _AlbedoTexture3.Sample(Global_trilinear_repeat_sampler, transUv);
    float3 Normal3 = UnpackNormalScale(_NormalTexture3.Sample(Global_trilinear_repeat_sampler, transUv), 1);

    float4 Diffuse4 = _AlbedoTexture4.Sample(Global_trilinear_repeat_sampler, transUv);
    float3 Normal4 = UnpackNormalScale(_NormalTexture4.Sample(Global_trilinear_repeat_sampler, transUv), 1);*/

    float3 cw5 = 0;
    float2 uv15 = 0;
    float2 uv25 = 0;
    float2 uv35 = 0;
    float2 dx5 = 0;
    float2 dy5 = 0;

    float3 cw8 = 0;
    float2 uv18 = 0;
    float2 uv28 = 0;
    float2 uv38 = 0;
    float2 dx8 = 0;
    float2 dy8 = 0;

    float StochasticScale = 0.5;

    float4 Diffuse1 = StochasticSample2DWeightsR(_AlbedoTexture1, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal1 = StochasticSample2DWeightsLum(_NormalTexture1, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);
    Normal1.xyz = UnpackNormalScale(Normal1, 1);

    float4 Diffuse2 = StochasticSample2DWeightsR(_AlbedoTexture2, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal2 = StochasticSample2DWeightsLum(_NormalTexture2, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);
    Normal2.xyz = UnpackNormalScale(Normal2, 1);

    float4 Diffuse3 = StochasticSample2DWeightsR(_AlbedoTexture3, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal3 = StochasticSample2DWeightsLum(_NormalTexture3, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);
    Normal3.xyz = UnpackNormalScale(Normal3, 1);

    float4 Diffuse4 = StochasticSample2DWeightsR(_AlbedoTexture4, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal4 = StochasticSample2DWeightsLum(_NormalTexture4, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);
    Normal4.xyz = UnpackNormalScale(Normal4, 1);

    /*float4 Diffuse1 = StochasticSample2D(_AlbedoTexture1, Global_trilinear_repeat_sampler, transUv);
    float4 Normal1 = StochasticSample2D(_NormalTexture1, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse2 = StochasticSample2D(_AlbedoTexture2, Global_trilinear_repeat_sampler, transUv);
    float4 Normal2 = StochasticSample2D(_NormalTexture2, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse3 = StochasticSample2D(_AlbedoTexture3, Global_trilinear_repeat_sampler, transUv);
    float4 Normal3 = StochasticSample2D(_NormalTexture3, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse4 = StochasticSample2D(_AlbedoTexture4, Global_trilinear_repeat_sampler, transUv);
    float4 Normal4 = StochasticSample2D(_NormalTexture4, Global_trilinear_repeat_sampler, transUv);*/
    
    ColorBuffer = 0;
    ColorBuffer += splatMap.r * Diffuse1;
    ColorBuffer += splatMap.g * Diffuse2;
    ColorBuffer += splatMap.b * Diffuse3;
    ColorBuffer += splatMap.a * Diffuse4;

    NormalBuffer = 0;
    NormalBuffer.rgb += splatMap.r * Normal1;
    NormalBuffer.rgb += splatMap.g * Normal2;
    NormalBuffer.rgb += splatMap.b * Normal3;
    NormalBuffer.rgb += splatMap.a * Normal4;
    NormalBuffer = normalize(NormalBuffer);
}

#endif