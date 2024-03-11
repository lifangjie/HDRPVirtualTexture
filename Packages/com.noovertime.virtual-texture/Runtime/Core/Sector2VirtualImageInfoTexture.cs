using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NoOvertime.VirtualTexture
{
    public class Sector2VirtualImageInfoTexture : IDisposable
    {
        private readonly uint3[] _updateSector2VirtualImageInfoArray = new uint3[Constant.MaxPreloadSector];
        private readonly RTHandle _sector2VirtualImageInfo;
        private readonly ComputeBuffer _write2AtlasBuffer;
        private int _updateSector2VirtualImageInfoCount;
        private readonly int _clearThreadGroups;

        public Sector2VirtualImageInfoTexture(int sectorCount)
        {
            _sector2VirtualImageInfo = RTHandles.Alloc(sectorCount, sectorCount, colorFormat: GraphicsFormat.R32_UInt, enableRandomWrite: true);
            _write2AtlasBuffer = new ComputeBuffer(Constant.MaxPreloadSector, 12, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _clearThreadGroups = sectorCount / 8 + 1;
        }

        public void Dispose()
        {
            RTHandles.Release(_sector2VirtualImageInfo);
            _write2AtlasBuffer.Dispose();
        }
        
        public RTHandle RT => _sector2VirtualImageInfo;

        public void UpdateSector(int2 sector, uint encoded)
        {
            _updateSector2VirtualImageInfoArray[_updateSector2VirtualImageInfoCount++] = new uint3((uint2)sector, encoded);
        }

        public void UpdateSector2VirtualImageInfo(CommandBuffer cmd)
        {
            // clear
            cmd.SetComputeTextureParam(Context.Instance.Resource.clearSector2VirtualImageInfoTextureCompute, 0, Constant.RWSector2VirtualImageInfoTextureID, _sector2VirtualImageInfo);
            cmd.DispatchCompute(Context.Instance.Resource.clearSector2VirtualImageInfoTextureCompute, 0, _clearThreadGroups, _clearThreadGroups, 1);
            // update
            cmd.SetGlobalInteger(Constant.Write2AtlasCountID, _updateSector2VirtualImageInfoCount);
            cmd.SetComputeBufferParam(Context.Instance.Resource.updateSector2VirtualImageInfoTextureCompute, 0, Constant.Write2AtlasBufferID, _write2AtlasBuffer);
            cmd.SetBufferData(_write2AtlasBuffer, _updateSector2VirtualImageInfoArray);
            cmd.SetComputeTextureParam(Context.Instance.Resource.updateSector2VirtualImageInfoTextureCompute, 0, Constant.RWSector2VirtualImageInfoTextureID, _sector2VirtualImageInfo);
            cmd.DispatchCompute(Context.Instance.Resource.updateSector2VirtualImageInfoTextureCompute, 0, 16, 1, 1);
            _updateSector2VirtualImageInfoCount = 0;
        }
    }
}