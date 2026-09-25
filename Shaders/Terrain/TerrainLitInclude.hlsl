#ifndef LANDSCAPE_TERRAIN_VT_INCLUDE
#define LANDSCAPE_TERRAIN_VT_INCLUDE

#define SplatmapFragment StandardSplatmapFragment
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
#undef SplatmapFragment

float4 _VTVolumeRect;
float4 _VTPageParams;
float4 _VTPageTileParams;
int _VTReady;

TEXTURE2D(_PhyscisAlbedo);
TEXTURE2D(_PhyscisNormal);
TEXTURE2D(_PageTableTexture);
SAMPLER(sampler_PhyscisAlbedo);
SAMPLER(sampler_PhyscisNormal);
SAMPLER(sampler_PageTableTexture);

void SplatmapFragment(Varyings IN, out half4 outColor : SV_Target0)
{
    if (_VTReady != 0 && _VTVolumeRect.z > 0.0 && _VTVolumeRect.w > 0.0)
    {
        float2 uv = (IN.positionWS.xz - _VTVolumeRect.xy) / _VTVolumeRect.zw;
        if (all(uv >= 0.0) && all(uv < 1.0))
        {
            float2 pageUV = uv - frac(uv * _VTPageParams.x) * _VTPageParams.y;
            float4 pageTable = SAMPLE_TEXTURE2D_LOD(_PageTableTexture, sampler_PageTableTexture, pageUV, 0);
            if (pageTable.a >= 0.5)
            {
#ifdef _ALPHATEST_ON
                ClipHoles(IN.uvMainAndLM.xy);
#endif
                pageTable *= 255.0;
                float2 pageOffset = frac(pageUV * exp2(_VTPageParams.z - pageTable.b));
                float2 physicalUV = (pageTable.rg * (_VTPageTileParams.y + _VTPageTileParams.x * 2.0)
                    + pageOffset * _VTPageTileParams.y + _VTPageTileParams.x) / _VTPageTileParams.zw;
                half3 albedo = SAMPLE_TEXTURE2D_LOD(_PhyscisAlbedo, sampler_PhyscisAlbedo, physicalUV, 0).rgb;
                half3 normalTS = SAMPLE_TEXTURE2D_LOD(_PhyscisNormal, sampler_PhyscisNormal, physicalUV, 0).xyz;
                InputData inputData;
                InitializeInputData(IN, normalTS, inputData);
                InitializeBakedGIData(IN, inputData);
                half4 color = UniversalFragmentPBR(inputData, albedo, 0, half3(0, 0, 0), 0, 1, half3(0, 0, 0), 1);
                SplatmapFinalColor(color, inputData.fogCoord);
                outColor = half4(color.rgb, 1);
                return;
            }
        }
    }
    StandardSplatmapFragment(IN, outColor);
}

#endif
