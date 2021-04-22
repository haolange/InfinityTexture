#ifndef _FEEDBACKINCLUDE
#define _FEEDBACKINCLUDE

#define UNITY_INSTANCING_ENABLED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // float4(xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

#ifdef UNITY_INSTANCING_ENABLED
    TEXTURE2D(_TerrainHeightmapTexture);
    SAMPLER(sampler_TerrainNormalmapTexture);
    float4 _TerrainHeightmapRecipSize;   // float4(1.0f/width, 1.0f/height, 1.0f/(width-1), 1.0f/(height-1))
    float4 _TerrainHeightmapScale;       // float4(hmScale.x, hmScale.y / (float)(kMaxHeight), hmScale.z, 0.0f)
#endif

struct Attributes
{
    float2 uv0 : TEXCOORD0;
    float4 vertexOS : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv0 : TEXCOORD0;
    float3 vertexWS : TEXCOORD1;
    float4 vertexCS : SV_POSITION;
};

Varyings FeedbackVert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    
    #if defined(UNITY_INSTANCING_ENABLED)
        float2 patchVertex = input.vertexOS.xy;
        float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

        float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z; // (xy + float2(xBase,yBase)) * skipScale
        float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

        input.vertexOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
        input.vertexOS.y = height * _TerrainHeightmapScale.y;
        
        input.uv0 = sampleCoords * _TerrainHeightmapRecipSize.zw;
    #endif
    
    VertexPositionInputs Attributes = GetVertexPositionInputs(input.vertexOS.xyz);

    output.uv0 = (Attributes.positionWS.xz + 512) * rcp(1024);
    output.vertexWS = Attributes.positionWS;
    output.vertexCS = Attributes.positionCS;
    return output;
}

float MipLevel(float2 UV)
{
    float2 DX = ddx(UV);
    float2 DY = ddy(UV);
    float MaxSqr = max(dot(DX, DX), dot(DY, DY));
    float MipLevel = 0.5 * log2(MaxSqr);
    return max(0, MipLevel);
}

float MipLevelAniso2D(float2 UV, float MaxAnisoLog2)
{
    float2 DX = ddx(UV);
    float2 DY = ddy(UV);
	float PX = dot(DX, DX);
	float PY = dot(DY, DY);
	float MinLevel = 0.5f * log2(min(PX, PY));
	float MaxLevel = 0.5f * log2(max(PX, PY));
	float AnisoBias = min(MaxLevel - MinLevel, MaxAnisoLog2);
	return MaxLevel - AnisoBias;
}

float BoxMask(float2 A, float2 B, float2 Size)
{
    return 1 - saturate(ceil(length(max(0, abs(A - B) - (Size * 0.5)))));
}

float4 FeedbackFrag(Varyings input) : SV_Target
{
    float ComputedLevel = floor(MipLevel(input.uv0 * 4096) /* 0.5 - 0.25*/);
    ComputedLevel = clamp(ComputedLevel, 0, 8);
    ComputedLevel /= 255;

	return float4(ComputedLevel, input.uv0.y, input.uv0.x, 1) * BoxMask(input.uv0, 0.5, 1);
}

#endif
