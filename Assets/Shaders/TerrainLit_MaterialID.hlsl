TEXTURE2D_ARRAY(_BaseMapArray); // share sampler with mask map array
TEXTURE2D_ARRAY(_MaskMapArray);
SAMPLER(sampler_linear_repeat_aniso8);
TEXTURE2D(_SplatMap);
TEXTURE2D(_GlobalMap);
float _NearBlendDistance;
float _FarBlendDistance;

#include "TerrainMaterialIDUtility.hlsl"
#include "Packages/com.noovertime.virtual-texture/Runtime/ShaderLibrary/VirtualTexture.hlsl"

void TerrainLitShade(float2 uv, float3 positionRWS, inout TerrainLitSurfaceData surfaceData)
{
    float4 baseMap, maskMap;
    #if defined(_VIRTUAL_TEXTURE)
    float4 vtBaseMap, vtMaskMap;
    SampleVT(positionRWS, vtBaseMap, vtMaskMap);
    baseMap = vtBaseMap;
    maskMap = vtMaskMap;
    #else
    float4 slot;
    float2 vertexPosX, vertexPosY, vertexPosZ, vertexPosW;
    float3 positionWS = GetAbsolutePositionWS(positionRWS);
    float4 weight = RectGrid(positionWS.xz, vertexPosX, vertexPosY, vertexPosZ, vertexPosW);
    slot.x = LOAD_TEXTURE2D_LOD(_SplatMap, vertexPosX, 0).r * 255.5;
    slot.y = LOAD_TEXTURE2D_LOD(_SplatMap, vertexPosY, 0).r * 255.5;
    slot.z = LOAD_TEXTURE2D_LOD(_SplatMap, vertexPosZ, 0).r * 255.5;
    slot.w = LOAD_TEXTURE2D_LOD(_SplatMap, vertexPosW, 0).r * 255.5;

    SampleMaterialID(_BaseMapArray, _MaskMapArray, sampler_linear_repeat_aniso8, positionWS.xz, slot, weight, baseMap, maskMap);
    #endif
    float3 normalTS = UnpackNormalAG(float4(1.0, maskMap.g, 1.0, maskMap.r));

    const float4 globalMap = SAMPLE_TEXTURE2D(_GlobalMap, sampler_linear_repeat_aniso8, uv);
    const float globalBlendWeight = saturate((length(positionRWS) - _NearBlendDistance) / _FarBlendDistance);
    float3 baseColor = baseMap.rgb;
    float smoothness = 1 - maskMap.a;
    float occlusion = maskMap.b;
    BlendWithGlobalMap(globalMap.rgb, globalBlendWeight, baseMap, maskMap, baseColor, smoothness, occlusion);
    normalTS = lerp(normalTS, float3(0, 0, 1), globalBlendWeight);

    surfaceData.albedo = baseColor;
    surfaceData.normalData = -normalTS;
    surfaceData.smoothness = smoothness;
    surfaceData.metallic = 0;
    surfaceData.ao = occlusion;
}

void TerrainLitDebug(float2 uv, inout float3 baseColor)
{
    #ifdef DEBUG_DISPLAY
    //baseColor = GetTextureDataDebug(_DebugMipMapMode, uv, _MainTex, _MainTex_TexelSize, _MainTex_MipInfo, baseColor);
    #endif
}
