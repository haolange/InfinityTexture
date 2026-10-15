#ifndef LANDSCAPE_TERRAIN_VT_INCLUDE
#define LANDSCAPE_TERRAIN_VT_INCLUDE

// 地形前向着色：体积内且间接表有效时读虚拟纹理并走完整 PBR，否则退回 URP 原生的 splat 混合。

#define SplatmapFragment StandardSplatmapFragment
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
#undef SplatmapFragment
#include "../Core/VirtualTextureCommon.hlsl"
#include "../Core/VirtualTextureLighting.hlsl"

// 地形生产者的地址：xy 体积左下角世界坐标，zw 体积尺寸
float4 _VTVolumeRect;
int _VTReady;

void SplatmapFragment(Varyings IN, out half4 outColor : SV_Target0)
{
    float2 uv = (IN.positionWS.xz - _VTVolumeRect.xy) / _VTVolumeRect.zw;
    float2 texels = uv * VT_TEXEL_SCALE;
    float2 dx = ddx(texels);
    float2 dy = ddy(texels);

    VTSurface surface;
    if (_VTReady != 0 && all(uv >= 0.0) && all(uv < 1.0) && VTSampleSurface(uv, dx, dy, surface))
    {
#ifdef _ALPHATEST_ON
        ClipHoles(IN.uvMainAndLM.xy);
#endif
        InputData inputData;
        InitializeInputData(IN, surface.normalTS, inputData);
        InitializeBakedGIData(IN, inputData);
        half4 color = VTFragmentPBR(inputData, surface);
        SplatmapFinalColor(color, inputData.fogCoord);
        outColor = half4(color.rgb, 1);
        return;
    }
    StandardSplatmapFragment(IN, outColor);
}

#endif
