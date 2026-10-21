#ifndef TERRAIN_VT_FEEDBACK_INCLUDED
#define TERRAIN_VT_FEEDBACK_INCLUDED

// 地形的反馈 pass：顶点按地形实例化展开高度图，片元把世界坐标换成虚拟 uv 后交给底层编码。

#include "TerrainGeometry.hlsl"
#include "../Core/VirtualTextureCommon.hlsl"

float4 _VTVolumeRect;

struct Varyings
{
    float4 vertexCS : SV_POSITION;
    float2 virtualUV : TEXCOORD0;
    float2 terrainUV : TEXCOORD1;
};

Varyings FeedbackVert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);

    TerrainInstancing(input);

    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
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
