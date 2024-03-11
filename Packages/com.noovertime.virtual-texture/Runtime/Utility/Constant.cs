using UnityEngine;

namespace NoOvertime.VirtualTexture
{
    public static class Constant
    {
        public const int MaxVirtualPageSize = 256;
        public const int MaxVirtualPageSizeShift = 8;
        public const int PageIDTextureDownscale = 8;
        public const int MaxDeduplicatedPageCount = 256;
        public const int ReadingBackTimeout = 8;
        public const int SectorSizeShift = 6;
        public const int SectorSize = 1 << SectorSizeShift;
        public const int PageSizeShift = 8;
        public const int PageSize = 1 << PageSizeShift;
        public const int BorderSize = 4;
        public const int PageSizeWithBorder = PageSize + 2 * BorderSize;
        public const int IndirectionTextureSize = 1024;
        public const int MinimalVirtualImageSize = 2048;
        public const float CameraPositionSqrDeltaThreshold = 4;
        public const int MaxTexelDensity = 1024;
        public const int HighestResolution = SectorSize * MaxTexelDensity;
        public const float SwitchDistance = 64 * 64 * 1.5f;
        public const int MaxPreloadSector = 256;
        public const float SectorPreloadDistance = 6;
        // 1080P下大约400个Page左右
        // 2K下大约700个Page左右
        // 4K下会超过1200个Page
        public const int MaxPhysicalPageCount = 1023;
        public const int RenderingPagePerFrame = 16;
        public const int UpdateIndirectionTexturePerFrame = 64;

        public static readonly int ParamsID = Shader.PropertyToID("_params");
        // rendering physical page
        public static readonly int TargetIndexID = Shader.PropertyToID("_targetIndex");
        public static readonly int SplatMapID = Shader.PropertyToID("_SplatMap");
        public static readonly int BaseMapArrayID = Shader.PropertyToID("_BaseMapArray");
        public static readonly int MaskMapArrayID = Shader.PropertyToID("_MaskMapArray");
        public static readonly int RWPhysicalPageBaseMapAtlasID = Shader.PropertyToID("_physicalPageBaseMapAtlas");
        public static readonly int RWPhysicalPageMaskMapAtlasID = Shader.PropertyToID("_physicalPageMaskMapAtlas");
        public static readonly int PhysicalPageBaseMapAtlasID = Shader.PropertyToID("PhysicalPageBaseMapAtlas");
        public static readonly int PhysicalPageMaskMapAtlasID = Shader.PropertyToID("PhysicalPageMaskMapAtlas");

        public static readonly int Write2AtlasCountID = Shader.PropertyToID("_write2AtlasCount");
        public static readonly int Write2AtlasBufferID = Shader.PropertyToID("_write2AtlasBuffer");
        public static readonly int Sector2VirtualImageInfoTextureID = Shader.PropertyToID("Sector2VirtualImageInfoTexture");
        public static readonly int RWSector2VirtualImageInfoTextureID = Shader.PropertyToID("_sector2VirtualImageInfoTexture");
        public static readonly int Write2IndirectionCountID = Shader.PropertyToID("_write2IndirectionCount");
        public static readonly int Write2IndirectionBufferID = Shader.PropertyToID("_write2IndirectionBuffer");
        public static readonly int RWPageIDOutputTexture = Shader.PropertyToID("_pageIDOutputTexture");
        public static readonly int IndirectionTextureID = Shader.PropertyToID("IndirectionTexture");
        public static readonly int[] RWIndirectionTextureIDs =
        {
           Shader.PropertyToID("_indirectionTexture0"),
           Shader.PropertyToID("_indirectionTexture1"),
           Shader.PropertyToID("_indirectionTexture2"),
           Shader.PropertyToID("_indirectionTexture3"),
           Shader.PropertyToID("_indirectionTexture4"),
           Shader.PropertyToID("_indirectionTexture5"),
           Shader.PropertyToID("_indirectionTexture6"),
           Shader.PropertyToID("_indirectionTexture7"),
           Shader.PropertyToID("_indirectionTexture8"),
        };

        public static readonly int[,] BayerDither8X8 =
        {
            { 0, 32, 8, 40, 2, 34, 10, 42 },
            { 48, 16, 56, 24, 50, 18, 58, 26 },
            { 12, 44, 4, 36, 14, 46, 6, 38 },
            { 60, 28, 52, 20, 62, 30, 54, 22 },
            { 3, 35, 11, 43, 1, 33, 9, 41 },
            { 51, 19, 59, 27, 49, 17, 57, 25 },
            { 15, 47, 7, 39, 13, 45, 5, 37 },
            { 63, 31, 55, 23, 61, 29, 53, 21 }
        };
    }
}