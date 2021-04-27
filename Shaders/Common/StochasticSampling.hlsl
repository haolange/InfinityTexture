#ifndef StochasticSampling
#define StochasticSampling

#ifdef UNITY_COLORSPACE_GAMMA
  #define stochastic_ColorSpaceLuminance half4(0.22, 0.707, 0.071, 0.0) // Legacy: alpha is set to 0.0 to specify gamma mode
#else
  #define stochastic_ColorSpaceLuminance half4(0.0396819152, 0.458021790, 0.00609653955, 1.0) // Legacy: alpha is set to 1.0 to specify linear mode
#endif

inline half StochasticLuminance(half3 rgb)
{
    return dot(rgb, stochastic_ColorSpaceLuminance.rgb);
}


// Compute local triangle barycentric coordinates and vertex IDs
void StochasticTriangleGrid(float2 uv,
   out float w1, out float w2, out float w3,
   out int2 vertex1, out int2 vertex2, out int2 vertex3, float scale)
{
   // Scaling of the input
   uv *= 3.464 * scale; // 2 * sqrt(3)

   // Skew input space into simplex triangle grid
   const float2x2 gridToSkewedGrid = float2x2(1.0, 0.0, -0.57735027, 1.15470054);
   float2 skewedCoord = mul(gridToSkewedGrid, uv);

   // Compute local triangle vertex IDs and local barycentric coordinates
   int2 baseId = int2(floor(skewedCoord));
   float3 temp = float3(frac(skewedCoord), 0);
   temp.z = 1.0 - temp.x - temp.y;
   if (temp.z > 0.0)
   {
      w1 = temp.z;
      w2 = temp.y;
      w3 = temp.x;
      vertex1 = baseId;
      vertex2 = baseId + int2(0, 1);
      vertex3 = baseId + int2(1, 0);
   }
   else
   {
      w1 = -temp.z;
      w2 = 1.0 - temp.y;
      w3 = 1.0 - temp.x;
      vertex1 = baseId + int2(1, 1);
      vertex2 = baseId + int2(1, 0);
      vertex3 = baseId + int2(0, 1);
   }
}

float2 StochasticSimpleHash2(float2 p)
{
   return frac(sin(mul(float2x2(127.1, 311.7, 269.5, 183.3), p)) * 43758.5453);
}


half3 StochasticBaryWeightBlend(half3 iWeights, half tex0, half tex1, half tex2, half contrast)
{
    // compute weight with height map
    const half epsilon = 1.0f / 1024.0f;
    half3 weights = half3(iWeights.x * (tex0 + epsilon), 
                             iWeights.y * (tex1 + epsilon),
                             iWeights.z * (tex2 + epsilon));

    // Contrast weights
    half maxWeight = max(weights.x, max(weights.y, weights.z));
    half transition = contrast * maxWeight;
    half threshold = maxWeight - transition;
    half scale = 1.0f / transition;
    weights = saturate((weights - threshold) * scale);
    // Normalize weights.
    half weightScale = 1.0f / (weights.x + weights.y + weights.z);
    weights *= weightScale;
    return weights;
}

void StochasticPrepareStochasticUVs(float2 uv, out float2 uv1, out float2 uv2, out float2 uv3, out half3 weights, float scale)
{
   // Get triangle info
   float w1, w2, w3;
   int2 vertex1, vertex2, vertex3;
   StochasticTriangleGrid(uv, w1, w2, w3, vertex1, vertex2, vertex3, scale);

   // Assign random offset to each triangle vertex
   uv1 = uv;
   uv2 = uv;
   uv3 = uv;
   
   uv1.xy += StochasticSimpleHash2(vertex1);
   uv2.xy += StochasticSimpleHash2(vertex2);
   uv3.xy += StochasticSimpleHash2(vertex3);
   weights = half3(w1, w2, w3);
   
}



half4 StochasticSample2DWeightsR(Texture2D Tex, SamplerState TexSampler, float2 uv, out half3 cw, out float2 uv1, out float2 uv2, out float2 uv3, out float2 dx, out float2 dy, float scale, float contrast)
{
   half3 w;
   StochasticPrepareStochasticUVs(uv, uv1, uv2, uv3, w, scale);

   dx = ddx(uv);
   dy = ddy(uv);

   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   
   
   cw.xyz = StochasticBaryWeightBlend(w, G1.r, G2.r, G3.r, contrast);

   
    return G1 * cw.x + G2 * cw.y + G3 * cw.z;

}

half4 StochasticSample2DWeightsG(Texture2D Tex, SamplerState TexSampler, float2 uv, out half3 cw, out float2 uv1, out float2 uv2, out float2 uv3, out float2 dx, out float2 dy, float scale, float contrast)
{
   half3 w;
   StochasticPrepareStochasticUVs(uv, uv1, uv2, uv3, w, scale);

   dx = ddx(uv);
   dy = ddy(uv);

   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   
   
   cw.xyz = StochasticBaryWeightBlend(w, G1.g, G2.g, G3.g, contrast);
   
   return G1 * cw.x + G2 * cw.y + G3 * cw.z;

}

half4 StochasticSample2DWeightsB(Texture2D Tex, SamplerState TexSampler, float2 uv, out half3 cw, out float2 uv1, out float2 uv2, out float2 uv3, out float2 dx, out float2 dy, float scale, float contrast)
{
   half3 w;
   StochasticPrepareStochasticUVs(uv, uv1, uv2, uv3, w, scale);

   dx = ddx(uv);
   dy = ddy(uv);

   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   
   
   cw.xyz = StochasticBaryWeightBlend(w, G1.b, G2.b, G3.b, contrast);

   
   return G1 * cw.x + G2 * cw.y + G3 * cw.z;

}

half4 StochasticSample2DWeightsA(Texture2D Tex, SamplerState TexSampler, float2 uv, out half3 cw, out float2 uv1, out float2 uv2, out float2 uv3, out float2 dx, out float2 dy, float scale, float contrast)
{
   half3 w;
   StochasticPrepareStochasticUVs(uv, uv1, uv2, uv3, w, scale);

   dx = ddx(uv);
   dy = ddy(uv);

   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   
   
   cw.xyz = StochasticBaryWeightBlend(w, G1.a, G2.a, G3.a, contrast);

   
   return G1 * cw.x + G2 * cw.y + G3 * cw.z;

}

half4 StochasticSample2DWeightsLum(Texture2D Tex, SamplerState TexSampler, float2 uv, out half3 cw, out float2 uv1, out float2 uv2, out float2 uv3, out float2 dx, out float2 dy, float scale, float contrast)
{
   half3 w;
   StochasticPrepareStochasticUVs(uv, uv1, uv2, uv3, w, scale);

   dx = ddx(uv);
   dy = ddy(uv);

   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   
   cw.xyz = StochasticBaryWeightBlend(w, StochasticLuminance(G1.rgb), StochasticLuminance(G2.rgb), StochasticLuminance(G3.rgb), contrast);
   
   return G1 * cw.x + G2 * cw.y + G3 * cw.z;
}

half4 StochasticSample2D(Texture2D Tex, SamplerState TexSampler, half3 cw, float2 uv1, float2 uv2, float2 uv3, float2 dx, float2 dy)
{
   float4 G1 = Tex.SampleGrad(TexSampler, uv1, dx, dy);
   float4 G2 = Tex.SampleGrad(TexSampler, uv2, dx, dy);
   float4 G3 = Tex.SampleGrad(TexSampler, uv3, dx, dy);
   return G1 * cw.x + G2 * cw.y + G3 * cw.z;
}

float2 hash2D2D (float2 s)
{
	return frac(sin(fmod(float2(dot(s, float2(127.1,311.7)), dot(s, float2(269.5,183.3))), 3.14159))*43758.5453);
}

float4 StochasticSample2D(Texture2D Tex, SamplerState TexSampler, float2 UV)
{
	//triangle vertices and blend weights
	//BW_vx[0...2].xyz = triangle verts
	//BW_vx[3].xy = blend weights (z is unused)
	float4x3 BW_vx;

	//uv transformed into triangular grid space with UV scaled by approximation of 2*sqrt(3)
	float2 skewUV = mul(float2x2 (1.0 , 0.0 , -0.57735027 , 1.15470054), UV * 3.464);

	//vertex IDs and barycentric coords
	float2 vxID = float2 (floor(skewUV));
	float3 barry = float3 (frac(skewUV), 0);
	barry.z = 1.0-barry.x-barry.y;

	BW_vx = ((barry.z>0) ? 
		float4x3(float3(vxID, 0), float3(vxID + float2(0, 1), 0), float3(vxID + float2(1, 0), 0), barry.zyx) :
		float4x3(float3(vxID + float2 (1, 1), 0), float3(vxID + float2 (1, 0), 0), float3(vxID + float2 (0, 1), 0), float3(-barry.z, 1.0-barry.y, 1.0-barry.x)));

	//calculate derivatives to avoid triangular grid artifacts
	float2 dx = ddx(UV);
	float2 dy = ddy(UV);

	//blend samples with calculated weights
	return mul(Tex.Sample(TexSampler, UV + hash2D2D(BW_vx[0].xy) + dx + dy), BW_vx[3].x) + 
			mul(Tex.Sample(TexSampler, UV + hash2D2D(BW_vx[1].xy) + dx + dy), BW_vx[3].y) + 
			mul(Tex.Sample(TexSampler, UV + hash2D2D(BW_vx[2].xy) + dx + dy), BW_vx[3].z);
}

#endif



