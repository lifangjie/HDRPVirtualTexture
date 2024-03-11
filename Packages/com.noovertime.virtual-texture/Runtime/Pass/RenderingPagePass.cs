using System;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    /// <summary>
    /// 因为unity terrain使用的整张的splat map而不是分sector的
    /// BakePhysicalPage.compute需要获得world position
    /// </summary>
    public class RenderingPagePass : CustomPass, IDisposable
    {
        private ProfilerMarker _renderingPageMarker;
        private readonly LRUCache<ulong, int> _lruCache = new(Constant.MaxPhysicalPageCount);
        private int _pageIndex;

        public RenderingPagePass(ProfilerMarker renderingPageMarker)
        {
            _renderingPageMarker = renderingPageMarker;
            for (int i = 0; i < Constant.MaxPhysicalPageCount; i++)
            {
                _lruCache.Insert(ulong.MaxValue - (ulong) i, i);
            }
        }

        public void Dispose()
        {
        }

        public void StartRendering()
        {
            _pageIndex = 0;
#if UNITY_EDITOR && DEBUG_TERRAIN
            Debug.Log($"lizha @ {Time.frameCount} Page count: {Context.Instance.ResolvedPageID.Length}");
#endif
        }

        public bool IsRenderingFinished()
        {
            return _pageIndex >= Context.Instance.ResolvedPageID.Length;
        }

        protected override void Execute(CustomPassContext ctx)
        {
            using (_renderingPageMarker.Auto())
            {
                int renderingCount = 0;
                while (!IsRenderingFinished() && renderingCount < Constant.RenderingPagePerFrame && Context.Instance.IndirectionTexture.IsUpdateArrayFarFromFull())
                {
                    var packedPageID = Context.Instance.ResolvedPageID[_pageIndex++];
                    var pageID = Utility.UnpackPageID(packedPageID);
                    var virtualPageSize = 1 << pageID.w;
                    // 先回到mip0, 而后右移再左移抹掉余数即可得到indirection page的起始位置
                    var indirectionPageXZ = ((pageID.xy << pageID.z) >> pageID.w) << pageID.w;
                    // 当前page在sector中的相对位置
                    var localPageID = (pageID.xy << pageID.z) - indirectionPageXZ;
                    // 计算得到sector后, 通过偏移sector即可得到真实的世界空间virtual image uv
                    bool getSector = Context.Instance.ImageInfo2Sector.TryGetValue(new int3(indirectionPageXZ, virtualPageSize), out var sector);
                    Assert.IsTrue(getSector);
                    ulong lruKey = Utility.EncodeLRUKey(sector, localPageID, pageID.z, pageID.w);
                    // 先看下LruCache里是否存有此page
                    bool contains = _lruCache.Touch(lruKey, out var slot);
                    // 如果有，continue
                    if (!contains)
                    {
                        // 如果没有，看下容量满了没(预分配了无效的page, 一定是满的)
                        // 满了的话需要移除一个slot出来给新page用
                        // 另外需要注意一下sector image size的降低可能需要清理mip0 key
                        // 这里有个问题:
                        // sector virtual image size降低的时候，indirection上的原有mip0会被抹掉
                        // 但是LRUCache没法移除，这样即使physical page还存活的情况下indirection entry没了
                        // 当virtual image size降低后立即回升，需要重新建立indirection entry
                        // 暂时没什么好想法，只能每帧都更新indirection texture?
                        bool removeLast = _lruCache.RemoveLast(out var removeLRUKey, out slot);
                        if (removeLRUKey < 0x7FFFFFFFFFFFFFFFu) // 是一个有效的LRUKey
                        {
                            var (virtualPageID, physicalMip) = Utility.DecodeLRUKey(removeLRUKey);
                            int2 removeSector = (virtualPageID << physicalMip) / Constant.MaxVirtualPageSize;
                            bool getRemoveSectorInfo = Context.Instance.AllSectors.TryGetValue(removeSector, out var imageInfo);
                            bool isSectorValid = imageInfo.w > 0;
                            if (getRemoveSectorInfo && isSectorValid)
                            {
                                int2 localVirtualPageID = (virtualPageID << physicalMip) % Constant.MaxVirtualPageSize;
                                int4 removePageID = int4.zero;
                                int virtualPageSizeLog = math.ceillog2(imageInfo.w);
                                int mip = physicalMip - Constant.MaxVirtualPageSizeShift + virtualPageSizeLog;
                                mip = math.max(mip, 0);
                                removePageID.xy = (imageInfo.yz >> mip) + (localVirtualPageID >> physicalMip);
                                removePageID.z = mip;
                                removePageID.w = virtualPageSizeLog;
                                Context.Instance.IndirectionTexture.AddUpdateArray(removePageID, 65535);
                            }
                        }

                        Assert.IsTrue(removeLast, "LRU Cache怎么搞成空的了");
                        bool insert = _lruCache.Insert(lruKey, slot);
                        Assert.IsTrue(insert, "LRU Cache插入失败");
                        Context.Instance.PhysicalPageAtlas.Render(ctx.cmd, sector, localPageID, pageID, slot);
                        renderingCount++;
                    }

                    // xy是indirection texture的uv, z是mip, w是physical page的index
                    Context.Instance.IndirectionTexture.AddUpdateArray(pageID, slot);
                }

                Context.Instance.IndirectionTexture.UpdateIndirectionTexture(ctx.cmd);
            }
        }
    }
}