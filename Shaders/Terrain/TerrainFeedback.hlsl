#ifndef TERRAIN_VT_FEEDBACK_INCLUDED
#define TERRAIN_VT_FEEDBACK_INCLUDED

// 地形的反馈 pass：顶点按地形实例化展开高度图，片元把世界坐标换成虚拟 uv 后交给底层编码。

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "../Core/VirtualTextureCommon.hlsl"

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // xBase, yBase, skipScale, ~
UNITY_INSTANCING_BUFFER_END(Terrain)

float4 _TerrainHeightmapScale;       // hmScale.x, hmScale.y / kMaxHeight, hmScale.z, 0
float4 _TerrainHeightmapRecipSize;   // 1/width, 1/height, 1/(width-1), 1/(height-1)
TEXTURE2D(_TerrainHeightmapTexture);

// 地形生产者的地址：xy 体积左下角世界坐标，zw 体积尺寸
float4 _VTVolumeRect;

struct Attributes
{
    float4 vertexOS : POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 vertexCS : SV_POSITION;
    float2 virtualUV : TEXCOORD0;
    float2 terrainUV : TEXCOORD1;
};

#ifdef _ALPHATEST_ON
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);

void ClipHoles(float2 uv)
{
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, uv).r;
    clip(hole == 0.0f ? -1 : 1);
}
#endif

Varyings FeedbackVert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);

#if defined(UNITY_INSTANCING_ENABLED)
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (input.vertexOS.xy + instanceData.xy) * instanceData.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));
    input.vertexOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    input.vertexOS.y = height * _TerrainHeightmapScale.y;
    input.texcoord = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif

    VertexPositionInputs positions = GetVertexPositionInputs(input.vertexOS.xyz);
    output.vertexCS = positions.positionCS;
    output.virtualUV = (positions.positionWS.xz - _VTVolumeRect.xy) / _VTVolumeRect.zw;
    output.terrainUV = input.texcoord;
    return output;
}

uint FeedbackFrag(Varyings input) : SV_Target
{
    #ifdef _ALPHATEST_ON
        ClipHoles(input.terrainUV);
    #endif
    return VTFeedback(input.virtualUV);
}

#endif
