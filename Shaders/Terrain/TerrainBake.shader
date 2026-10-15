Shader "Hidden/VirtualTexture/TerrainBake"
{
    // 地形生产者：把一张 splat 控制图对应的 4 个地形层混合后画进 scratch。
    // Pass 0 写第一张 splat，Pass 1 叠加后续的 splat。
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
            #define TERRAIN_SPLAT_ADDPASS
            #include "TerrainBake.hlsl"
            ENDHLSL
        }
    }
}
