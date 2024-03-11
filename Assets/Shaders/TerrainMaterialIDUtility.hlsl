#ifndef TERRAIN_MATERIAL_ID_UTILITY_INCLUDED
#define TERRAIN_MATERIAL_ID_UTILITY_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

#define MULTIPLE_TYPE_BLEND3(type) \
type Blend(type input0, type input1, type input2, type input3, float4 weight) { \
return input0*weight.x + input1*weight.y + input2*weight.z + input3*weight.w; \
}
MULTIPLE_TYPE_BLEND3(float)
MULTIPLE_TYPE_BLEND3(float2)
MULTIPLE_TYPE_BLEND3(float3)
MULTIPLE_TYPE_BLEND3(float4)
float4 CalculateBlendWeight(float4 weight, float4 height, float heightBlendBias = 0.1)
{
    weight *= height;
    const float maxWeight = max(max(max(max(weight.x, weight.y), weight.z), weight.w) - heightBlendBias, 0);
    weight = float4(max(weight.x - maxWeight, 0), max(weight.y - maxWeight, 0), max(weight.z - maxWeight, 0), max(weight.w - maxWeight, 0));
    return weight / (weight.x + weight.y + weight.z + weight.w);
}
///-----------------------
///           |           
///           |           
///     y     |     w     
///           |           
///           |           
///---------ba|se---------
///           |           
///           |           
///     x     |     z     
///           |           
///           |           
///-----------------------
float4 RectGrid(float2 uv, out float2 vertexPosX, out float2 vertexPosY, out float2 vertexPosZ, out float2 vertexPosW)
{
    const float2 base = floor(uv + 0.5);
    vertexPosW = base + 0.5;
    vertexPosX = base - 0.5;
    vertexPosY = float2(vertexPosW.x - 1.0, vertexPosW.y);
    vertexPosZ = float2(vertexPosW.x, vertexPosW.y - 1.0);

    // Calculate offset from start texel to sample location
    const float2 offset = uv + 0.5 - base;
    const float2 oneMinusOffset = 1.0 - offset;

    // calculates the weights
    return float4(oneMinusOffset.x * oneMinusOffset.y, oneMinusOffset.x * offset.y, offset.x * oneMinusOffset.y, offset.x * offset.y);
}

void SampleMaterialIDTextureArray(TEXTURE2D_ARRAY(_BaseMapArray), TEXTURE2D_ARRAY(_MaskMapArray), SAMPLER(sampler_common),
                                  float2 uv, float slot, out float4 baseMap, out float4 maskMap)
{
    baseMap = SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_common, uv * 0.1, slot);
    maskMap = SAMPLE_TEXTURE2D_ARRAY(_MaskMapArray, sampler_common, uv * 0.1, slot);
}

void SampleMaterialID(TEXTURE2D_ARRAY(_BaseMapArray), TEXTURE2D_ARRAY(_MaskMapArray), SAMPLER(sampler_common),
                      float2 uv, float4 slot, float4 weight, out float4 baseMap, out float4 maskMap)
{
    float4 baseMap0, baseMap1, baseMap2, baseMap3;
    float4 maskMap0, maskMap1, maskMap2, maskMap3;
    SampleMaterialIDTextureArray(_BaseMapArray, _MaskMapArray, sampler_common, uv, slot.x, baseMap0, maskMap0);
    SampleMaterialIDTextureArray(_BaseMapArray, _MaskMapArray, sampler_common, uv, slot.y, baseMap1, maskMap1);
    SampleMaterialIDTextureArray(_BaseMapArray, _MaskMapArray, sampler_common, uv, slot.z, baseMap2, maskMap2);
    SampleMaterialIDTextureArray(_BaseMapArray, _MaskMapArray, sampler_common, uv, slot.w, baseMap3, maskMap3);
    weight = CalculateBlendWeight(weight, abs(float4(baseMap0.a, baseMap1.a, baseMap2.a, baseMap3.a)) + HALF_EPS, 1);
    baseMap = Blend(baseMap0, baseMap1, baseMap2, baseMap3, weight);
    maskMap = Blend(maskMap0, maskMap1, maskMap2, maskMap3, weight);
}

#if !defined(UNITY_SHADER_VARIABLES_FUNCTIONS_INCLUDED) && !defined(UNITY_GRAPHFUNCTIONS_LW_INCLUDED)
// This function assumes the bitangent flip is encoded in tangentWS.w
float3x3 BuildTangentToWorld(float4 tangentWS, float3 normalWS)
{
    // tangentWS must not be normalized (mikkts requirement)

    // Normalize normalWS vector but keep the renormFactor to apply it to bitangent and tangent
    float3 unnormalizedNormalWS = normalWS;
    float renormFactor = 1.0 / max(FLT_MIN, length(unnormalizedNormalWS));

    // bitangent on the fly option in xnormal to reduce vertex shader outputs.
    // this is the mikktspace transformation (must use unnormalized attributes)
    float3x3 tangentToWorld = CreateTangentToWorld(unnormalizedNormalWS, tangentWS.xyz, tangentWS.w > 0.0 ? 1.0 : -1.0);

    // surface gradient based formulation requires a unit length initial normal. We can maintain compliance with mikkts
    // by uniformly scaling all 3 vectors since normalization of the perturbed normal will cancel it.
    tangentToWorld[0] = tangentToWorld[0] * renormFactor;
    tangentToWorld[1] = tangentToWorld[1] * renormFactor;
    tangentToWorld[2] = tangentToWorld[2] * renormFactor;       // normalizes the interpolated vertex normal

    return tangentToWorld;
}
#endif

void BlendWithGlobalMap(float3 globalColor, float blendWeight, float4 baseMap, float4 maskMap,
                        out float3 baseColor, out float smoothness, out float occlusion)
{
    baseColor = lerp(baseMap.rgb, globalColor, blendWeight);
    smoothness = 1 - lerp(maskMap.a, 0.85, blendWeight);
    occlusion = lerp(maskMap.b, 0.85, blendWeight);
}

#endif // TERRAIN_MATERIAL_ID_UTILITY_INCLUDED
