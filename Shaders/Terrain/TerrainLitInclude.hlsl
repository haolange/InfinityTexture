#ifndef LANDSCAPE_TERRAIN_VT_INCLUDE
#define LANDSCAPE_TERRAIN_VT_INCLUDE

// 地形前向着色：体积内且间接表有效时读虚拟纹理，否则退回 URP 原生的 splat 混合。

#define SplatmapFragment StandardSplatmapFragment
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
#undef SplatmapFragment
#include "../Core/VirtualTextureCommon.hlsl"

// 地形生产者的地址：xy 体积左下角世界坐标，zw 体积尺寸
float4 _VTVolumeRect;
int _VTReady;

TEXTURE2D(_PhyscisAlbedo);
TEXTURE2D(_PhyscisNormal);

void SplatmapFragment(Varyings IN, out half4 outColor : SV_Target0)
{
    float2 uv = (IN.positionWS.xz - _VTVolumeRect.xy) / _VTVolumeRect.zw;
    float2 texels = uv * (VT_PAGE_COUNT * VT_PAGE_TEXELS);
    uint mip = VTRequestMip(VTComputeLod(ddx(texels), ddy(texels)));

    if (_VTReady != 0 && all(uv >= 0.0) && all(uv < 1.0))
    {
        VTAddress address = VTLookup(uv, mip);
        if (address.valid)
        {
#ifdef _ALPHATEST_ON
            ClipHoles(IN.uvMainAndLM.xy);
#endif
            half3 albedo = SAMPLE_TEXTURE2D_LOD(_PhyscisAlbedo, sampler_VTLinearClamp, address.physicalUV, 0).rgb;
            half3 normalTS = SAMPLE_TEXTURE2D_LOD(_PhyscisNormal, sampler_VTLinearClamp, address.physicalUV, 0).xyz;
            InputData inputData;
            InitializeInputData(IN, normalTS, inputData);
            InitializeBakedGIData(IN, inputData);
            half4 color = UniversalFragmentPBR(inputData, albedo, 0, half3(0, 0, 0), 0, 1, half3(0, 0, 0), 1);
            SplatmapFinalColor(color, inputData.fogCoord);
            outColor = half4(color.rgb, 1);
            return;
        }
    }
    StandardSplatmapFragment(IN, outColor);
}

#endif
