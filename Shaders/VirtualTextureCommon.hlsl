#ifndef VIRTUAL_TEXTURE_INCLUDED
#define VIRTUAL_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct VTAppdata {
	float4 vertex : POSITION;
	float2 texcoord : TEXCOORD0;
};

struct VTV2f
{
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
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

#endif