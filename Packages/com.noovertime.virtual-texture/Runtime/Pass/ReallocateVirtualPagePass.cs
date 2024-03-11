using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class ReallocateVirtualPagePass : CustomPass, IDisposable
    {
        private ProfilerMarker _reallocateVirtualPageMarker;
        private readonly List<KeyValuePair<int2, int4>> _delayRemoveSectors;
        private readonly Dictionary<int2, (int4, int4)> _delayRemoveImages;
        private readonly Stack<int3> _travelStack;
        private readonly List<int2> _requestSectors;
        private int2 _lastCameraSector;
        private float2 _lastCameraPositionXZ;

        public ReallocateVirtualPagePass(ProfilerMarker reallocateVirtualPageMarker)
        {
            _reallocateVirtualPageMarker = reallocateVirtualPageMarker;
            _delayRemoveSectors = new List<KeyValuePair<int2, int4>>();
            _delayRemoveImages = new Dictionary<int2, (int4, int4)>();
            _travelStack = new Stack<int3>();
            _requestSectors = new List<int2>(Constant.MaxPreloadSector);
            _lastCameraSector = new int2(int.MinValue, int.MinValue);
            _lastCameraPositionXZ = new float2(float.MinValue, float.MinValue);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            using (_reallocateVirtualPageMarker.Auto())
            {
                Vector3 cameraPosition = ctx.hdCamera.camera.transform.position;

                if (CenterSectorChanged(cameraPosition))
                {
                    UpdateRequestSectors(ctx.cmd);
                }

                if (CameraPositionChanged(cameraPosition))
                {
                    ReallocateVirtualImage(ctx.cmd);
                }
            }
        }

        public void Dispose()
        {
            _lastCameraSector = new int2(int.MinValue, int.MinValue);
            _lastCameraPositionXZ = new float2(float.MinValue, float.MinValue);
        }

        #region StreamingSectors

        private bool CenterSectorChanged(in float3 cameraPosition)
        {
            var cameraSector = (int2)cameraPosition.xz >> Constant.SectorSizeShift;
            if (cameraSector.x == _lastCameraSector.x && cameraSector.y == _lastCameraSector.y) return false;
            _lastCameraSector = cameraSector;
            return true;
        }

        private void UpdateRequestSectors(CommandBuffer cmd)
        {
            // x, z, size
            _requestSectors.Clear();
            _travelStack.Push(new int3(0, 0, Context.Instance.TerrainSize >> Constant.SectorSizeShift));
            while (_travelStack.TryPop(out var currentNode))
            {
                int halfSize = currentNode.z >> 1;
                // 直接按sector下标来计算distance
                var sqrDistance = math.lengthsq(currentNode.xy - _lastCameraSector + halfSize);
                if (currentNode.z == 1) // 已经到最小级别了, 不要再往下遍历
                {
                    if (sqrDistance < Constant.SectorPreloadDistance * Constant.SectorPreloadDistance) // sector load范围大概是256+32(区块边缘到中心)
                    {
                        _requestSectors.Add(new int2(currentNode.x, currentNode.y));
                    }
                }
                else
                {
                    if (sqrDistance < math.lengthsq(new float2(halfSize + Constant.SectorPreloadDistance, halfSize + Constant.SectorPreloadDistance)))
                    {
                        _travelStack.Push(new int3(currentNode.x + halfSize, currentNode.y + halfSize, halfSize));
                        _travelStack.Push(new int3(currentNode.x, currentNode.y + halfSize, halfSize));
                        _travelStack.Push(new int3(currentNode.x + halfSize, currentNode.y, halfSize));
                        _travelStack.Push(new int3(currentNode.x, currentNode.y, halfSize));
                    }
                }
            }

            // remove unnecessary sectors
            _delayRemoveSectors.Clear();
            foreach (var pair in Context.Instance.AllSectors)
            {
                if (!_requestSectors.Contains(pair.Key))
                {
                    _delayRemoveSectors.Add(pair);
                }
            }

            foreach (var (removeSector, imageInfo) in _delayRemoveSectors)
            {
                Context.Instance.AllSectors.Remove(removeSector);
                Context.Instance.VirtualImageAtlas.RemoveImage(removeSector);
                Context.Instance.ImageInfo2Sector.Remove(imageInfo.yzw);
                Context.Instance.ImageInfo.Remove(imageInfo.yzw);
                if (imageInfo.w > 0)
                {
                    Context.Instance.IndirectionTexture.RemoveVirtualImage(cmd, imageInfo);
                }
            }

            // load new request sectors
            foreach (var sector in _requestSectors)
            {
                if (Context.Instance.AllSectors.ContainsKey(sector)) continue;
                Context.Instance.AllSectors.Add(sector, int4.zero);
            }
        }

        #endregion

        #region AllocateVirtualImage

        private bool CameraPositionChanged(float3 cameraPosition)
        {
            float2 cameraPositionXZ = cameraPosition.xz;
            var sqrPositionDelta = math.lengthsq(_lastCameraPositionXZ - cameraPositionXZ);
            if (sqrPositionDelta < Constant.CameraPositionSqrDeltaThreshold) return false;
            _lastCameraPositionXZ = cameraPositionXZ;
            return true;
        }

        private void ReallocateVirtualImage(CommandBuffer cmd)
        {
            _delayRemoveImages.Clear();
            foreach (var (sector, oldImageInfo) in Context.Instance.AllSectors)
            {
                int virtualImageSize = Utility.CalculateTargetImageSize(sector.xy * 64 + 32, _lastCameraPositionXZ);
                int oldVirtualImageSize = oldImageInfo.w << Constant.PageSizeShift;
                if (virtualImageSize != oldVirtualImageSize)
                {
                    Context.Instance.VirtualImageAtlas.InsertImage(sector, virtualImageSize, out var imageInfo);
                    _delayRemoveImages[sector] = (oldImageInfo, imageInfo);
#if DEBUG_TERRAIN
                    UnityEngine.Assertions.Assert.AreNotEqual(imageInfo.w, 0, $"InsertImage of sector:{sector}, size:{virtualImageSize} failed.");
#if UNITY_EDITOR
                    Debug.Log($"lizha @ {Time.frameCount} {sector} change from {oldVirtualImageSize} to {virtualImageSize}");
#endif
#endif
                }
            }

            foreach (var (sector, (oldImageInfo, newImageInfo)) in _delayRemoveImages)
            {
                Context.Instance.ImageInfo2Sector.Remove(oldImageInfo.yzw);
                Context.Instance.ImageInfo.Remove(oldImageInfo.yzw);
                Context.Instance.AllSectors.Remove(sector);
                if (newImageInfo.w > 0)
                {
                    Context.Instance.ImageInfo2Sector.Add(newImageInfo.yzw, sector);
                    Context.Instance.ImageInfo.Add(newImageInfo.yzw);
                    Context.Instance.AllSectors.Add(sector, newImageInfo);
                }

                if (oldImageInfo.w > 0)
                {
                    Context.Instance.IndirectionTexture.RemapVirtualImage(cmd, oldImageInfo, newImageInfo);
                }

                Context.Instance.VirtualImageAtlas.RemoveImage(oldImageInfo);
            }

            foreach (var (sector, imageInfo) in Context.Instance.AllSectors)
            {
                // 最大是log2(65536/256(pageSize)) = 8, 最小是log2(1024(minimalVirtualImageSize)/256) = 2
                // (1024 texel/cm is mip 0)
                var virtualPageSizeLog = math.ceillog2(imageInfo.w);
                // 12bit virtual page x, 12bit virtual page z, 8bit virtual page log2(size)
                uint encoded = ((uint)imageInfo.y << 20) + ((uint)imageInfo.z << 8) + (uint)virtualPageSizeLog;
                Context.Instance.Sector2VirtualImageInfoTexture.UpdateSector(sector, encoded);
            }

            Context.Instance.Sector2VirtualImageInfoTexture.UpdateSector2VirtualImageInfo(cmd);
        }

        #endregion
    }
}