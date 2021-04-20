Shader "VirtualTexture/DrawPageTable"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}
		ZTest Always
		Cull Front
		Pass
		{
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float2 uv           : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
//
			struct Varyings
			{
				float4 color           : TEXCOORD0;
				float4 positionHCS	   : SV_POSITION;
			};
			
			float4 _tempInfo;
			UNITY_INSTANCING_BUFFER_START(InstanceProp)
			UNITY_DEFINE_INSTANCED_PROP(float4, _PageInfo)
			UNITY_DEFINE_INSTANCED_PROP(float4x4, _Matrix_MVP)
			UNITY_INSTANCING_BUFFER_END(InstanceProp)

			Varyings vert(Attributes IN)
			{
				Varyings OUT ;
				UNITY_SETUP_INSTANCE_ID(IN);
				float4x4 mat = UNITY_MATRIX_M;
				
				mat = UNITY_ACCESS_INSTANCED_PROP(InstanceProp, _Matrix_MVP);
				float2 pos = saturate(mul(mat, IN.positionOS).xy);
				pos.y = 1 - pos.y;

				OUT.positionHCS = float4(2.0 * pos  - 1,0.5,1);
				OUT.color = UNITY_ACCESS_INSTANCED_PROP(InstanceProp, _PageInfo);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				return IN.color;
			}
			ENDHLSL
		}
	}
}
