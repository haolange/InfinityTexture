#ifndef PageCommon
#define PageCommon

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

SAMPLER(Global_trilinear_repeat_sampler);


struct PixelOutput
{
    float4 ColorBuffer : SV_Target0;
    float4 NormalBuffer : SV_Target1;
};

struct Attributes
{
    float2 uv : TEXCOORD0;
    float4 vertex : POSITION;
};

struct Varyings
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

Varyings vert(Attributes Input)
{
    Varyings Out;
    Out.uv = Input.uv;
    Out.pos = mul(_Matrix_MVP, Input.vertex);

    return Out;
}

PixelOutput frag(const Varyings In)
{
    float4 blend = _SplatTexture.Sample(Global_trilinear_repeat_sampler, In.uv * _SplatTileOffset.xy + _SplatTileOffset.zw);
    
#ifdef TERRAIN_SPLAT_ADDPASS
    clip(blend.x + blend.y + blend.z + blend.w <= 0.005h ? -1.0h : 1.0h);
#endif
    
    float2 transUv = In.uv * _SurfaceTileOffset.xy + _SurfaceTileOffset.zw;
    float4 Diffuse1 = _AlbedoTexture1.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal1 = _NormalTexture1.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse2 = _AlbedoTexture2.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal2 = _NormalTexture2.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse3 = _AlbedoTexture3.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal3 = _NormalTexture3.Sample(Global_trilinear_repeat_sampler, transUv);

    float4 Diffuse4 = _AlbedoTexture4.Sample(Global_trilinear_repeat_sampler, transUv);
    float4 Normal4 = _NormalTexture4.Sample(Global_trilinear_repeat_sampler, transUv);

    PixelOutput Output;
    Output.ColorBuffer = blend.r * Diffuse1 + blend.g * Diffuse2 + blend.b * Diffuse3 + blend.a * Diffuse4;
    Output.NormalBuffer = blend.r * Normal1 + blend.g * Normal2 + blend.b * Normal3 + blend.a * Normal4;

    return Output;
}

#endif