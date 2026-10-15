Shader "VirtualTexture/DrawPageTable"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}

		Cull Front
		ZTest Always

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
