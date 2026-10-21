#ifndef LANDSCAPE_TERRAIN_GEOMETRY_INCLUDED
#define LANDSCAPE_TERRAIN_GEOMETRY_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// 仅保留 Terrain 几何所需的高度、法线和洞，不声明任何 splat 图层资源。
float4 _TerrainHeightmapScale;
float4 _TerrainHeightmapRecipSize;
TYPED_TEXTURE2D(float4, _TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture);
SAMPLER(sampler_TerrainNormalmapTexture);

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
UNITY_INSTANCING_BUFFER_END(Terrain)

#if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
    #define ENABLE_TERRAIN_PERPIXEL_NORMAL
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

void TerrainInstancing(inout Attributes input)
{
#ifdef UNITY_INSTANCING_ENABLED
    float4 patch = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 coords = (input.positionOS.xy + patch.xy) * patch.z;
    input.positionOS.xz = coords * _TerrainHeightmapScale.xz;
    input.positionOS.y = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(coords, 0))) * _TerrainHeightmapScale.y;
    input.texcoord = coords * _TerrainHeightmapRecipSize.zw;
#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
    input.normalOS = float3(0, 1, 0);
#else
    input.normalOS = _TerrainNormalmapTexture.Load(int3(coords, 0)).rgb * 2 - 1;
#endif
#endif
}

#ifdef _ALPHATEST_ON
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);
#endif

void ClipHoles(float2 uv)
{
#ifdef _ALPHATEST_ON
    clip(SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r - 0.0005);
#endif
}

struct DepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 terrainUV : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

DepthVaryings DepthVert(Attributes input)
{
    DepthVaryings output = (DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    TerrainInstancing(input);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.terrainUV = input.texcoord;
    return output;
}

float3 _LightDirection;
float3 _LightPosition;

DepthVaryings ShadowVert(Attributes input)
{
    DepthVaryings output = (DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    TerrainInstancing(input);
    output.terrainUV = input.texcoord;
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirection = normalize(_LightPosition - positionWS);
#else
    float3 lightDirection = _LightDirection;
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}

#ifdef SCENESELECTIONPASS
int _ObjectId;
int _PassValue;
#endif

half4 DepthFrag(DepthVaryings input) : SV_TARGET
{
    ClipHoles(input.terrainUV);
#ifdef SCENESELECTIONPASS
    return half4(_ObjectId, _PassValue, 1, 1);
#else
    return input.positionCS.z;
#endif
}

#endif
