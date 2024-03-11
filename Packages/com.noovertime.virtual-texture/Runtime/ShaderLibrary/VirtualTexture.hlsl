#ifndef VIRTUAL_TEXTURE_INCLUDED
#define VIRTUAL_TEXTURE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "ShaderConstant.cs.hlsl"

RWTexture2D<uint> PageIDOutputTexture : register(u7);
Texture2D<uint> Sector2VirtualImageInfoTexture;
Texture2D<uint> IndirectionTexture;
Texture2DArray<float4> PhysicalPageBaseMapAtlas;
Texture2DArray<float4> PhysicalPageMaskMapAtlas;
uint VirtualDitherX;
uint VirtualDitherY;
SamplerState sampler_linear_clamp_aniso8;

// From https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm
float MipLevelAnisotropy(float2 uv, float size)
{
    float2 dX = ddx(uv * size);
    float2 dY = ddy(uv * size);
    float squaredLengthX = dot(dX, dX);
    float squaredLengthY = dot(dY, dY);
    float determinant = abs(dX.x*dY.y - dX.y*dY.x);
    bool isMajorX = squaredLengthX > squaredLengthY;
    float squaredLengthMajor = isMajorX ? squaredLengthX : squaredLengthY;
    float lengthMajor = sqrt(squaredLengthMajor);
    float normMajor = 1.f/lengthMajor;

    float2 anisoLineDirection;
    anisoLineDirection.x = (isMajorX ? dX.x : dY.x) * normMajor;
    anisoLineDirection.y = (isMajorX ? dX.y : dY.y) * normMajor;

    float ratioOfAnisotropy = squaredLengthMajor/determinant;

    // clamp ratio and compute LOD
    float lengthMinor;
    const float maxAniso = 8;
    if ( ratioOfAnisotropy > maxAniso ) // maxAniso comes from a Sampler state.
    {
        // ratio is clamped - LOD is based on ratio (preserves area)
        ratioOfAnisotropy = maxAniso;
        lengthMinor = lengthMajor/ratioOfAnisotropy;
    }
    else
    {
        // ratio not clamped - LOD is based on area
        lengthMinor = determinant/lengthMajor;
    }

    // clamp to top LOD
    if (lengthMinor < 1.0)
    {
        ratioOfAnisotropy = max( 1.0, ratioOfAnisotropy*lengthMinor );

        // lengthMinor = 1.0 // This line is no longer recommended for future hardware
        //
        // The commented out line above was part of the D3D10 spec until 8/17/2009,
        // when it was finally noticed that it was undesirable.
        //
        // Consider the case when the LOD is negative (lengthMinor less than 1),
        // but a positive LOD bias will be applied later on due to
        // sampler / instruction settings.
        //
        // With the clamp of lengthMinor above, the log2() below would make a
        // negative LOD become 0, after which any LOD biasing would apply later.
        // That means with biasing, LOD values less than the bias amount are
        // unavailable.  This would look blurrier than isotropic filtering,
        // which is obviously incorrect.  The output of this routine must allow
        // negative LOD values, so that LOD bias (if used) can still result in
        // hitting the most detailed mip levels.
        //
        // Because this issue was only noticed years after the D3D10 spec was originally
        // authored, many implementations will include a clamp such as commented out
        // above.  WHQL must therefore allow implementations that support either
        // behavior - clamping or not.  It is recommended that future hardware
        // does not do the clamp to 1.0 (thus allowing negative LOD).
        // The same applies for D3D11 hardware as well, since even the D3D11 specs
        // had already been locked down for a long time before this issue was uncovered.
    }

    float LOD = log2(lengthMinor);
    return LOD;
}

uint2 GetVirtualPageID(float3 positionWS, out float mip, out uint virtualPageSizeLog)
{
    // 对于virtual image size 65536的sector来说, 这个sector上有65536/PAGE_SIZE(256)个page, virtual page size = 256
    // 同理, 对于virtual image size 1024的sector来说, 这个sector上只有1024/PAGE_SIZE(4)个page, virtual page size = 4
    // 获取当前sector的imageInfo, 12bit virtual page x, 12bit virtual page z, 8bit virtual page log2(size)
    // indirection texture上每个pixel对应一个physical page, 对应(1024x1024)这么多个page
    // feedback输出的pageID其实是indirection texture的texel position
    const uint packedImageInfo = LOAD_TEXTURE2D(Sector2VirtualImageInfoTexture, positionWS.xz / 64);
    uint3 imageInfo = uint3(packedImageInfo >> 20, (packedImageInfo >> 8) & 0xFFF, packedImageInfo & 0xF);
    virtualPageSizeLog = imageInfo.z;
    mip = MipLevelAnisotropy(positionWS.xz, MAX_TEXEL_DENSITY) - MAX_VIRTUAL_PAGE_SIZE_SHIFT + virtualPageSizeLog;
    mip = clamp(mip, 0, virtualPageSizeLog);
    // (positionWS.xz % SECTOR_SIZE) 是先定位到相对于当前Sector的local坐标
    const float2 sectorPosition = positionWS.xz % SECTOR_SIZE;
    // * (1 << imageInfo.z) >> SECTOR_SIZE_SHIFT 是计算当前Sector上每一米有多少个virtual page(对应多少个physical page)
    const uint2 virtualImageUV = ((uint2)(sectorPosition * (1 << virtualPageSizeLog))) >> SECTOR_SIZE_SHIFT;
    // 再加上当前virtual image在atlas中的偏移和mip
    uint2 virtualPageID = (virtualImageUV + imageInfo.xy) >> ((uint)mip);
    return virtualPageID;
}

bool MatchMipLevel(uint2 virtualPageID, int virtualPageSizeLog, inout int mip, out int slot)
{
    UNITY_UNROLLX(MAX_VIRTUAL_PAGE_SIZE_SHIFT)
    while (true)
    {
        slot = LOAD_TEXTURE2D_LOD(IndirectionTexture, virtualPageID, mip);
        if (slot < MAX_PHYSICAL_PAGE_COUNT) return true;
        virtualPageID = virtualPageID >> 1;
        mip++;
        if (mip > virtualPageSizeLog) break;
    }
    return false;
}

bool SampleVT(float3 positionRWS, out float4 baseMap, out float4 maskMap)
{
    float3 positionWS = GetAbsolutePositionWS(positionRWS);
    int mip;
    uint virtualPageSizeLog;
    const uint2 virtualPageID = GetVirtualPageID(positionWS, mip, virtualPageSizeLog);
    int slot;
    const bool match = MatchMipLevel(virtualPageID, virtualPageSizeLog, mip, slot);
    if (match)
    {
        const uint texelPerMeter = 1 << virtualPageSizeLog - mip;
        const float2 sectorPosition = frac(positionWS.xz * INV_SECTOR_SIZE * texelPerMeter);
        float2 uvInPhysicalPage = (sectorPosition * PAGE_SIZE + 4.0f) * INV_PAGE_SIZE_WITH_BORDER;
        baseMap = SAMPLE_TEXTURE2D_ARRAY_LOD(PhysicalPageBaseMapAtlas, sampler_linear_clamp_aniso8, uvInPhysicalPage, slot, 0);
        maskMap = SAMPLE_TEXTURE2D_ARRAY_LOD(PhysicalPageMaskMapAtlas, sampler_linear_clamp_aniso8, uvInPhysicalPage, slot, 0);
        // debug border/mip
        // if ((uvInPhysicalPage * 264.f).x < 4.f || (uvInPhysicalPage * 264.f).x > 256.f)
        // {
        //     baseMap = 0;
        // }
        // if ((uvInPhysicalPage * 264.f).y < 4.f || (uvInPhysicalPage * 264.f).y > 256.f)
        // {
        //     baseMap = 0;
        // }
    }
    else
    {
        baseMap = 0;
        maskMap = 0;
    }
    return match;
}

void OutputPageID(float3 positionWS, uint2 positionSS)
{
    uint mip, virtualPageSizeLog;
    uint2 virtualPageID = GetVirtualPageID(positionWS, mip, virtualPageSizeLog);
    const uint packed = (virtualPageID.x << 20) + (virtualPageID.y << 8) + ((mip & 0xF) << 4) + virtualPageSizeLog;
    uint2 downscaleSS = positionSS % PAGE_ID_DOWNSCALE;
    if (downscaleSS.x == VirtualDitherX && downscaleSS.y == VirtualDitherY)
    {
        // size - mip < 0 的话表示这个page是废弃的(根本无法被indirection texture表达)
        if (virtualPageSizeLog == 0 || mip > virtualPageSizeLog)
        {
            PageIDOutputTexture[positionSS.xy / PAGE_ID_DOWNSCALE] = 0;
        }
        else
        {
            PageIDOutputTexture[positionSS.xy / PAGE_ID_DOWNSCALE] = packed;
        }
    }
}
#endif
