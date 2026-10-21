#ifndef LANDSCAPE_TERRAIN_VT_INCLUDE
#define LANDSCAPE_TERRAIN_VT_INCLUDE

#include "TerrainGeometry.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "../Core/VirtualTextureCommon.hlsl"
#include "../Core/VirtualTextureLighting.hlsl"

float4 _VTVolumeRect;

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 terrainUV : TEXCOORD2;
    float2 lightmapUV : TEXCOORD3;
#ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicLightmapUV : TEXCOORD4;
#endif
    half fogFactor : TEXCOORD5;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings TerrainVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    TerrainInstancing(input);
    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positions.positionCS;
    output.positionWS = positions.positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.terrainUV = input.texcoord;
    output.lightmapUV = input.texcoord * unity_LightmapST.xy + unity_LightmapST.zw;
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.texcoord * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    output.fogFactor = ComputeFogFactor(positions.positionCS.z);
    return output;
}

half4 TerrainFrag(Varyings input) : SV_TARGET
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ClipHoles(input.terrainUV);
    float2 uv = (input.positionWS.xz - _VTVolumeRect.xy) / _VTVolumeRect.zw;
    float2 texels = uv * VT_TEXEL_SCALE;
    VTSurface surface;
    bool valid = VTSampleSurface(uv, ddx(texels), ddy(texels), surface);
    clip(valid ? 1 : -1);

    half3 normalWS = normalize(input.normalWS);
#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    float2 coords = (input.terrainUV / _TerrainHeightmapRecipSize.zw + .5) * _TerrainHeightmapRecipSize.xy;
    normalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, coords).rgb * 2 - 1));
#endif
    // 与 URP Terrain 的切线方向一致，VT 中的法线只替代图层法线。
    half3 tangentWS = cross(GetObjectToWorldMatrix()._13_23_33, normalWS);
    InputData data = (InputData)0;
    data.positionWS = input.positionWS;
    data.positionCS = input.positionCS;
    data.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surface.normalTS, half3x3(-tangentWS, cross(normalWS, tangentWS), normalWS)));
    data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    data.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), input.fogFactor);
#ifdef DYNAMICLIGHTMAP_ON
    data.bakedGI = SAMPLE_GI(input.lightmapUV, input.dynamicLightmapUV, SampleSH(data.normalWS), data.normalWS);
#else
    data.bakedGI = SAMPLE_GI(input.lightmapUV, SampleSH(data.normalWS), data.normalWS);
#endif
    data.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
    half4 color = VTFragmentPBR(data, surface);
    color.rgb = MixFog(color.rgb, data.fogCoord);
    return color;
}

#endif
