#ifndef VIRTUAL_TEXTURE_FEEDBACK_INCLUDED
#define VIRTUAL_TEXTURE_FEEDBACK_INCLUDED

//#include "VirtualTextureCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define UNITY_INSTANCING_ENABLED

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)  // float4(xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

#ifdef UNITY_INSTANCING_ENABLED
    float4 _TerrainHeightmapScale;       // float4(hmScale.x, hmScale.y / (float)(kMaxHeight), hmScale.z, 0)
    float4 _TerrainHeightmapRecipSize;   // float4(1.0f/width, 1.0f/height, 1.0f/(width-1), 1.0f/(height-1))
    TEXTURE2D(_TerrainHeightmapTexture);
    SAMPLER(sampler_TerrainNormalmapTexture);
#endif

struct Attributes
{
    float4 vertexOS : POSITION;
    float2 texcoord0 : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 vertexCS : SV_POSITION;
    float2 texcoord0 : TEXCOORD0;
    float2 texcoord1 : TEXCOORD1;
};

float4 _VTVolumeRect;

// xy: page count
// z:  max mipmap level
float4 _VTPageParams;

// x: page size
// y: vertual texture size
// z: max mipmap level
// w: mipmap level bias
float4 _VTFeedbackParams;

// x: padding size
// y: center size
// zw: 1 / tile count
float4 _VTPageTileParams;

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
        float2 patchVertex = input.vertexOS.xy;
        float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);

        float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z; // (xy + float2(xBase,yBase)) * skipScale
        float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

        input.vertexOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
        input.vertexOS.y = height * _TerrainHeightmapScale.y;
        
        input.texcoord0 = sampleCoords * _TerrainHeightmapRecipSize.zw;
    #endif
    
    VertexPositionInputs Attributes = GetVertexPositionInputs(input.vertexOS.xyz);
    output.vertexCS = Attributes.positionCS;
    float2 vertexWS = Attributes.positionWS.xz;
    //output.texcoord0 = (vertexWS + 256) * rcp(256);
    //output.texcoord0 = (vertexWS + 512) * rcp(1024);
    output.texcoord1 = sampleCoords * _TerrainHeightmapRecipSize.xy;
    output.texcoord0 = (vertexWS - _VTVolumeRect.xy) * rcp(_VTVolumeRect.zw);
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

float BoxMask(float2 A, float2 B, float2 Size)
{
    return 1 - saturate(ceil(length(max(0, abs(A - B) - (Size * 0.5)))));
}

float4 FeedbackFrag(Varyings input) : SV_Target
{
    #ifdef _ALPHATEST_ON
        ClipHoles(input.texcoord1);
    #endif

    float ComputedLevel = MipLevel(input.texcoord0 * _VTFeedbackParams.y) + _VTFeedbackParams.w;
    ComputedLevel = clamp(floor(ComputedLevel), 0, 8);
	return float4(floor(input.texcoord0 * _VTFeedbackParams.x)/255, ComputedLevel/255, 1);
    
    /*float ComputedLevel = floor(MipLevel(input.texcoord0 * 4224));//4224
    ComputedLevel = clamp(ComputedLevel, 0, 8);
    return float4(floor(input.texcoord0 * 256) / 255, ComputedLevel / 255, 1) * BoxMask(input.texcoord0, 0.5, 1);*/
    //return float4(floor(input.texcoord0 * 1024) / (255 * 1), ComputedLevel / 255, 1) * BoxMask(input.texcoord0, 0.5, 1);

    //return float4(input.texcoord1, 0, 1);
}

#endif