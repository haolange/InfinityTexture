Shader "VirtualTexture/DrawPageTable"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}

		Cull Front
		ZTest Always

		Pass
		{
			Tags { "LightMode" = "Default" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			//#pragma enable_d3d11_debug_symbols

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
				float2 uv           : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

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
				Varyings OUT;
				UNITY_SETUP_INSTANCE_ID(IN);
				float4x4 mat = UNITY_MATRIX_M;

				mat = UNITY_ACCESS_INSTANCED_PROP(InstanceProp, _Matrix_MVP);
				float2 pos = saturate(mul(mat, IN.positionOS).xy);
				pos.y = 1 - pos.y;

				OUT.positionHCS = float4(2.0 * pos - 1,0.5,1);
				OUT.color = UNITY_ACCESS_INSTANCED_PROP(InstanceProp, _PageInfo);
				return OUT;
			}

			float4 frag(Varyings IN) : SV_Target
			{
				return IN.color;
			}
			ENDHLSL
		}

		Pass
		{
			Tags { "LightMode" = "Custom" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 texCoord0           : TEXCOORD0;
				float4 vertexOS   : POSITION;
			};

			struct Varyings
			{
				float4 texCoord0           : TEXCOORD0;
				float4 vertexCS	   : SV_POSITION;
			};

			struct FPageTableInfo
			{
				float4 pageData;
				float4x4 matrix_M;
			};

			StructuredBuffer<FPageTableInfo> _PageTableBuffer;

			Varyings vert(Attributes input)
			{
				Varyings output;
				FPageTableInfo PageTableInfo = _PageTableBuffer[input.InstanceId];

				float2 pos = saturate(mul(PageTableInfo.matrix_M, input.vertexOS).xy);
				pos.y = 1 - pos.y;

				output.texCoord0 = PageTableInfo.pageData;
				output.vertexCS = float4(pos * 2 - 1, 0.5, 1);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				return input.texCoord0;
			}
			ENDHLSL
		}
	}
}
