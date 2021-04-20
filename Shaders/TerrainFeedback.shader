Shader "Landscape/TerrainFeedback"
{
    Properties
    {
		[HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {} 
        [ToggleUI] _EnableInstancedPerPixelNormal("Enable Instanced per-pixel normal", Float) = 1.0
    }

	HLSLINCLUDE
	    #pragma multi_compile __ _ALPHATEST_ON
	ENDHLSL 
	
    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "False"}

		Pass
		{
            Name "Feedback"
			Tags { "LightMode" = "VTFeedback" }

			HLSLPROGRAM
            #pragma target 4.5
			#pragma multi_compile_instancing
			#pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

			#include "FeedbackInclude.hlsl"	
			#pragma vertex FeedbackVert
			#pragma fragment FeedbackFrag
			ENDHLSL
		}
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
