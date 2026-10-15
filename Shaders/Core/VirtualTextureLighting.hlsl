#ifndef VIRTUAL_TEXTURE_LIGHTING_INCLUDED
#define VIRTUAL_TEXTURE_LIGHTING_INCLUDED

// 用 VTSurface 做完整 PBR：金属度 + 高光等级（F0 = 0.08 × 高光）+ 粗糙度 + AO。
// URP 的金属度流程把 F0 固定为 0.04，无法表达高光等级，所以这里自己组装 BRDFData，
// 光照部分（GI、主光源与阴影）沿用 URP 的函数。需先包含 URP Lighting.hlsl。

half4 VTFragmentPBR(InputData inputData, VTSurface surface)
{
    half3 diffuse = surface.baseColor * (1.0 - surface.metallic);
    half3 specular = lerp(0.08 * surface.specular, surface.baseColor, surface.metallic);
    half reflectivity = ReflectivitySpecular(specular);
    half alpha = 1.0;

    BRDFData brdfData;
    InitializeBRDFDataDirect(surface.baseColor, diffuse, specular, reflectivity, 1.0 - reflectivity, 1.0 - surface.roughness, alpha, brdfData);
    BRDFData noClearCoat = (BRDFData)0;

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, surface.occlusion);
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    half3 color = GlobalIllumination(brdfData, noClearCoat, 0.0, inputData.bakedGI, aoFactor.indirectAmbientOcclusion,
                                     inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    color += LightingPhysicallyBased(brdfData, noClearCoat, mainLight, inputData.normalWS, inputData.viewDirectionWS, 0.0, false);
    return half4(color, 1.0);
}

#endif
