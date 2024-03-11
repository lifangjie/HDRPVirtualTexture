using UnityEngine.Rendering;

namespace NoOvertime.VirtualTexture.ShaderLibrary
{
    [GenerateHLSL]
    struct ShaderConstant
    {
        public const int PageIdDownscale = Constant.PageIDTextureDownscale;
        public const int MaxVirtualPageSize = Constant.MaxVirtualPageSize;
        public const int MaxVirtualPageSizeShift = Constant.MaxVirtualPageSizeShift;
        public const int PageSize = Constant.PageSize;
        public const int PageSizeShift = Constant.PageSizeShift;
        public const int BorderSize = Constant.BorderSize;
        public const int PageSizeWithBorder = Constant.PageSizeWithBorder;
        public const float InvPageSizeWithBorder = 1f / Constant.PageSizeWithBorder;
        public const int SectorSize = Constant.SectorSize;
        public const float InvSectorSize = 1f / Constant.SectorSize;
        public const int SectorSizeShift = Constant.SectorSizeShift;
        public const int MaxPhysicalPageCount = Constant.MaxPhysicalPageCount;
        public const int MaxTexelDensity = Constant.MaxTexelDensity;
    };
}