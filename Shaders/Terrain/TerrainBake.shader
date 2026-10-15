Shader "Hidden/VirtualTexture/TerrainBake"
{
    // 地形生产者：把一张 splat 控制图对应的 4 个地形层混合后画进 scratch。
    // Pass 0 写第一张 splat，Pass 1 叠加后续的 splat。关键字按 EPageLayers 决定输出几层。
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _PAGE_NORMAL _PAGE_NORMAL_MASK
            #include "TerrainBake.hlsl"
            ENDHLSL
        }

        Pass
        {
            Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _PAGE_NORMAL _PAGE_NORMAL_MASK
            #define TERRAIN_SPLAT_ADDPASS
            #include "TerrainBake.hlsl"
            ENDHLSL
        }
    }
}
