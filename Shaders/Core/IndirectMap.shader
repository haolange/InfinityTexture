Shader "Hidden/VirtualTexture/IndirectMap"
{
    // 把 IndirectWrite 列表画进间接表的某一级：每个实例一个矩形，写入打包好的 RGBA8 值。
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            struct IndirectWrite
            {
                int4 rect;
                uint value;
            };

            StructuredBuffer<IndirectWrite> _IndirectWrites;
            uint _IndirectWriteBase;
            float _IndirectLevelSize;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float4 value : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                IndirectWrite write = _IndirectWrites[_IndirectWriteBase + instanceID];

                // 两个三角形拼成矩形：(0,0) (1,0) (1,1) / (0,0) (1,1) (0,1)
                float2 corner = float2(vertexID == 1 || vertexID == 2 || vertexID == 4, vertexID == 2 || vertexID == 4 || vertexID == 5);
                float2 uv = (write.rect.xy + corner * write.rect.zw) / _IndirectLevelSize;

                Varyings output;
            #if UNITY_UV_STARTS_AT_TOP
                output.positionCS = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.5, 1.0);
            #else
                output.positionCS = float4(uv.x * 2.0 - 1.0, uv.y * 2.0 - 1.0, 0.5, 1.0);
            #endif
                uint v = write.value;
                output.value = float4(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF, v >> 24) / 255.0;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return input.value;
            }
            ENDHLSL
        }
    }
}
