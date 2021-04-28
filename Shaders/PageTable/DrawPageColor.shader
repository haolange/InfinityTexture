Shader "VirtualTexture/DrawPageColor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
		_Diffuse1("Diffuse1", 2D) = "white" {}
		_Diffuse2("Diffuse2", 2D) = "white" {}
		_Diffuse3("Diffuse3", 2D) = "white" {}
		_Diffuse4("Diffuse4", 2D) = "white" {}

		_Normal1("Normal1", 2D) = "white" {}
		_Normal2("Normal2", 2D) = "white" {}
		_Normal3("Normal3", 2D) = "white" {}
		_Normal4("Normal4", 2D) = "white" {}
		_Blend("Blend", 2D) = "white" {}
		_TileOffset1("TileOffset1",Vector) = (1,1,0,0)
		_TileOffset2("TileOffset2",Vector) = (1,1,0,0)
		_TileOffset3("TileOffset3",Vector) = (1,1,0,0)
		_TileOffset4("TileOffset4",Vector) = (1,1,0,0)
		_BlendTile("Blend Tile",Vector) = (0,0,100,100)
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
			#pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag
			#pragma enable_d3d11_debug_symbols
            
			#include "DrwaPageCommon.hlsl"
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
			#pragma enable_d3d11_debug_symbols
			
			#include "DrwaPageCommon.hlsl"
			ENDHLSL
		}

		Pass
        {
            HLSLPROGRAM
			#pragma target 4.5

            #pragma vertex vertTriangle
            #pragma fragment frag
			#pragma enable_d3d11_debug_symbols
            
			#include "DrwaPageCommon.hlsl"
            ENDHLSL
        }

		Pass
		{
			Blend One One
			HLSLPROGRAM
			#pragma target 4.5

			#pragma vertex vertTriangle
			#pragma fragment frag
			#define TERRAIN_SPLAT_ADDPASS
			#pragma enable_d3d11_debug_symbols
			
			#include "DrwaPageCommon.hlsl"
			ENDHLSL
		}
    }
}
