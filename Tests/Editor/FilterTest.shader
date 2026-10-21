Shader "Hidden/VirtualTexture/Tests/Filter"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../../Shaders/Core/VirtualTextureCommon.hlsl"
            float _TestLod;
            int _TestLayer;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id) * 1.04 - .02;
                return o;
            }
            float4 SelectLayer(VTTexels t) { return _TestLayer == 0 ? t.baseColor : (_TestLayer == 1 ? t.normal : t.mask); }
            float4 Frag(Varyings i) : SV_Target
            {
                bool valid = true, expectedValid = true;
                float level = clamp(_TestLod, 0.0, (float)VT_MAX_MIP);
                VTTexels expected = VTLerpTexels(VTBilinear(i.uv, (uint)floor(level), expectedValid), VTBilinear(i.uv, (uint)ceil(level), expectedValid), frac(level));
                VTTexels actual = VTTrilinear(i.uv, _TestLod, valid);
                return valid == expectedValid ? abs(SelectLayer(expected) - SelectLayer(actual)) : 1;
            }
            ENDHLSL
        }
    }
}
