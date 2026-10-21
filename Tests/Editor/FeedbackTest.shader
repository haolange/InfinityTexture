Shader "Hidden/VirtualTexture/Tests/Feedback"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../../Shaders/Core/VirtualTextureCommon.hlsl"
            float4 Vert(uint id : SV_VertexID) : SV_POSITION { return GetFullScreenTriangleVertexPosition(id); }
            uint Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return VTPackPageId((uint2)positionCS.xy + uint2(8175, 8183), 7);
            }
            ENDHLSL
        }
    }
}
