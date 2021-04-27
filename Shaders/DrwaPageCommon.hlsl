#ifndef PageCommon
#define PageCommon

#include "StochasticSampling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"


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

SAMPLER(Global_bilinear_clamp_sampler);
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
    float4 blend = _SplatTexture.SampleLevel(Global_bilinear_clamp_sampler, input.uv0 * _SplatTileOffset.xy + _SplatTileOffset.zw, 0);
    
#ifdef TERRAIN_SPLAT_ADDPASS
    clip(blend.x + blend.y + blend.z + blend.w <= 0.005h ? -1.0h : 1.0h);
#endif
    
    float2 transUv = input.uv0 * _SurfaceTileOffset.xy + _SurfaceTileOffset.zw;

    /*float4 Diffuse1 = _AlbedoTexture1.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal1 = _NormalTexture1.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse2 = _AlbedoTexture2.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal2 = _NormalTexture2.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse3 = _AlbedoTexture3.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal3 = _NormalTexture3.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse4 = _AlbedoTexture4.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal4 = _NormalTexture4.Sample(Global_trilinear_repeat_sampler, transUv);*/

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

    float4 Diffuse2 = StochasticSample2DWeightsR(_AlbedoTexture2, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal2 = StochasticSample2DWeightsLum(_NormalTexture2, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);

    float4 Diffuse3 = StochasticSample2DWeightsR(_AlbedoTexture3, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal3 = StochasticSample2DWeightsLum(_NormalTexture3, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);

    float4 Diffuse4 = StochasticSample2DWeightsR(_AlbedoTexture4, Global_trilinear_repeat_sampler, transUv, cw5, uv15, uv25, uv35, dx5, dy5, StochasticScale, 0.15);
    float4 Normal4 = StochasticSample2DWeightsLum(_NormalTexture4, Global_trilinear_repeat_sampler, transUv, cw8, uv18, uv28, uv38, dx8, dy8, StochasticScale, 0.15);

    /*float4 Diffuse1 = StochasticSample2D(_AlbedoTexture1, Global_trilinear_repeat_sampler, transUv);
    float4 Normal1 = StochasticSample2D(_NormalTexture1, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse2 = StochasticSample2D(_AlbedoTexture2, Global_trilinear_repeat_sampler, transUv);
    float4 Normal2 = StochasticSample2D(_NormalTexture2, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse3 = StochasticSample2D(_AlbedoTexture3, Global_trilinear_repeat_sampler, transUv);
    float4 Normal3 = StochasticSample2D(_NormalTexture3, Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse4 = StochasticSample2D(_AlbedoTexture4, Global_trilinear_repeat_sampler, transUv);
    float4 Normal4 = StochasticSample2D(_NormalTexture4, Global_trilinear_repeat_sampler, transUv);*/

    ColorBuffer = blend.r * Diffuse1 + blend.g * Diffuse2 + blend.b * Diffuse3 + blend.a * Diffuse4;
    NormalBuffer = blend.r * Normal1 + blend.g * Normal2 + blend.b * Normal3 + blend.a * Normal4;
}

#endif