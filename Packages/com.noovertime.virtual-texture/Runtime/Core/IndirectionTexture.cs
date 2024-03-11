using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NoOvertime.VirtualTexture
{
    public class IndirectionTexture : IDisposable
    {
        private readonly RTHandle _indirectionTexture;
        private readonly int4[] _write2IndirectionTextureArray = new int4[Constant.UpdateIndirectionTexturePerFrame];
        private readonly int4[] _write2IndirectionTextureArrayMip0 = new int4[Constant.UpdateIndirectionTexturePerFrame];
        private int _write2IndirectionCount;
        private int _write2IndirectionMip0Count;
        private readonly ComputeBuffer _write2IndirectionBuffer;
        private readonly ComputeBuffer _paramsBuffer;
        private readonly uint[] _paramsArray = new uint[6];

        public IndirectionTexture(int textureSize)
        {
            _indirectionTexture = RTHandles.Alloc(textureSize, textureSize, 1, DepthBits.None, GraphicsFormat.R16_UInt, useMipMap: true, autoGenerateMips: false, enableRandomWrite: true,
                name: nameof(_indirectionTexture));
            _write2IndirectionBuffer = new ComputeBuffer(Constant.UpdateIndirectionTexturePerFrame, 16, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _paramsBuffer = new ComputeBuffer(6, 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            CommandBuffer cmd = new CommandBuffer();
            for (int i = 0; i < Constant.RWIndirectionTextureIDs.Length; i++)
            {
                _paramsArray[0] = (uint)(1024 >> i);
                _paramsArray[2] = 0;
                _paramsArray[3] = 0;
                cmd.SetBufferData(_paramsBuffer, _paramsArray);
                cmd.SetComputeBufferParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.ParamsID, _paramsBuffer);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, i);
                int threadGroupCount = 1024 >> (2 + i);
                cmd.DispatchCompute(Context.Instance.Resource.remapIndirectionTextureCompute, 1, threadGroupCount, threadGroupCount, 1);
            }

            Graphics.ExecuteCommandBuffer(cmd);
        }

        public void Dispose()
        {
            RTHandles.Release(_indirectionTexture);
            _write2IndirectionBuffer.Dispose();
            _paramsBuffer.Dispose();
        }

        public RTHandle RT => _indirectionTexture;

        public bool IsUpdateArrayFarFromFull()
        {
            return _write2IndirectionCount + 2 < Constant.UpdateIndirectionTexturePerFrame && _write2IndirectionMip0Count + 2 < Constant.UpdateIndirectionTexturePerFrame;
        }

        public void AddUpdateArray(in int4 pageID, in int slot)
        {
            Assert.IsFalse(_write2IndirectionCount >= _write2IndirectionTextureArray.Length);
            if (pageID.z == 0)
            {
                _write2IndirectionTextureArrayMip0[_write2IndirectionMip0Count++] = new int4(pageID.xyz, slot);
            }
            else
            {
                _write2IndirectionTextureArray[_write2IndirectionCount++] = new int4(pageID.xyz, slot);
            }
        }

        public void UpdateIndirectionTexture(CommandBuffer cmd)
        {
            cmd.SetGlobalInteger(Constant.Write2IndirectionCountID, _write2IndirectionMip0Count);
            cmd.SetBufferData(_write2IndirectionBuffer, _write2IndirectionTextureArrayMip0);
            cmd.SetComputeBufferParam(Context.Instance.Resource.updateIndirectionTextureCompute, 0, Constant.Write2IndirectionBufferID, _write2IndirectionBuffer);
            cmd.SetComputeTextureParam(Context.Instance.Resource.updateIndirectionTextureCompute, 0, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, 0);
            cmd.DispatchCompute(Context.Instance.Resource.updateIndirectionTextureCompute, 0, 8, 1, 1);
            _write2IndirectionMip0Count = 0;

            cmd.SetGlobalInteger(Constant.Write2IndirectionCountID, _write2IndirectionCount);
            cmd.SetBufferData(_write2IndirectionBuffer, _write2IndirectionTextureArray);
            cmd.SetComputeBufferParam(Context.Instance.Resource.updateIndirectionTextureCompute, 1, Constant.Write2IndirectionBufferID, _write2IndirectionBuffer);
            for (int mip = 0; mip < Constant.RWIndirectionTextureIDs.Length; mip++)
            {
                cmd.SetComputeTextureParam(Context.Instance.Resource.updateIndirectionTextureCompute, 1, Constant.RWIndirectionTextureIDs[mip], _indirectionTexture, mip);
            }

            cmd.DispatchCompute(Context.Instance.Resource.updateIndirectionTextureCompute, 1, 8, 1, 1);
            _write2IndirectionCount = 0;
        }

        public void RemoveVirtualImage(CommandBuffer cmd, int4 imageInfo)
        {
            int mipCount = math.ceillog2(imageInfo.w);
            for (int i = 0; i < mipCount; i++)
            {
                _paramsArray[0] = (uint)imageInfo.w >> i;
                _paramsArray[2] = (uint)imageInfo.y >> i;
                _paramsArray[3] = (uint)imageInfo.z >> i;
                cmd.SetBufferData(_paramsBuffer, _paramsArray);
                cmd.SetComputeBufferParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.ParamsID, _paramsBuffer);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, i);
                int clearThreadGroups = math.max(imageInfo.w >> (2 + i), 1);
                cmd.DispatchCompute(Context.Instance.Resource.remapIndirectionTextureCompute, 1, clearThreadGroups, clearThreadGroups, 1);
            }
        }

        /// <summary>
        /// downscale的时候直接清掉mip0，因为有递归mip查询indirection entry的逻辑
        /// </summary>
        public void RemapVirtualImage(CommandBuffer cmd, int4 oldImageInfo, int4 newImageInfo)
        {
            Assert.AreNotEqual(oldImageInfo.w, newImageInfo.w);
            if (oldImageInfo.w < newImageInfo.w) // upscale
            {
                Upscale(cmd, oldImageInfo, newImageInfo);
            }
            else // downscale
            {
                Downscale(cmd, oldImageInfo, newImageInfo);
            }
        }

        private void Upscale(CommandBuffer cmd, int4 oldImageInfo, int4 newImageInfo)
        {
            Assert.IsTrue(oldImageInfo.w < newImageInfo.w);
            int mipCount = math.ceillog2(oldImageInfo.w);
            int mipDelta = math.ceillog2(newImageInfo.w) - mipCount;
            for (int mip = 0; mip <= mipCount; mip++)
            {
                _paramsArray[0] = (uint)oldImageInfo.w >> mip;
                _paramsArray[2] = (uint)oldImageInfo.y >> mip;
                _paramsArray[3] = (uint)oldImageInfo.z >> mip;
                _paramsArray[4] = (uint)newImageInfo.y >> (mip + mipDelta);
                _paramsArray[5] = (uint)newImageInfo.z >> (mip + mipDelta);
                cmd.SetBufferData(_paramsBuffer, _paramsArray);
                cmd.SetComputeBufferParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.ParamsID, _paramsBuffer);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, mip);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.RWIndirectionTextureIDs[1], _indirectionTexture, mip + mipDelta);
                int threadGroups = math.max(oldImageInfo.w >> mip + 2, 1);
                cmd.DispatchCompute(Context.Instance.Resource.remapIndirectionTextureCompute, 0, threadGroups, threadGroups, 1);
            }
        }

        private void Downscale(CommandBuffer cmd, int4 oldImageInfo, int4 newImageInfo)
        {
            Assert.IsTrue(oldImageInfo.w > newImageInfo.w);
            int mipCount = math.ceillog2(newImageInfo.w);
            int mipDelta = math.ceillog2(oldImageInfo.w) - mipCount;

            for (int mip = 0; mip < mipDelta; mip++)
            {
                _paramsArray[0] = (uint)oldImageInfo.w >> mip;
                _paramsArray[2] = (uint)oldImageInfo.y >> mip;
                _paramsArray[3] = (uint)oldImageInfo.z >> mip;
                cmd.SetBufferData(_paramsBuffer, _paramsArray);
                cmd.SetComputeBufferParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.ParamsID, _paramsBuffer);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 1, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, 0);
                int clearThreadGroups = math.max(oldImageInfo.w >> mip + 2, 1);
                cmd.DispatchCompute(Context.Instance.Resource.remapIndirectionTextureCompute, 1, clearThreadGroups, clearThreadGroups, 1);
            }

            for (int mip = 0; mip <= mipCount; mip++)
            {
                _paramsArray[0] = (uint)newImageInfo.w >> mip;
                _paramsArray[2] = (uint)oldImageInfo.y >> (mip + mipDelta);
                _paramsArray[3] = (uint)oldImageInfo.z >> (mip + mipDelta);
                _paramsArray[4] = (uint)newImageInfo.y >> mip;
                _paramsArray[5] = (uint)newImageInfo.z >> mip;
                cmd.SetBufferData(_paramsBuffer, _paramsArray);
                cmd.SetComputeBufferParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.ParamsID, _paramsBuffer);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.RWIndirectionTextureIDs[0], _indirectionTexture, mip + mipDelta);
                cmd.SetComputeTextureParam(Context.Instance.Resource.remapIndirectionTextureCompute, 0, Constant.RWIndirectionTextureIDs[1], _indirectionTexture, mip);
                int threadGroups = math.max(newImageInfo.w >> mip + 2, 1);
                cmd.DispatchCompute(Context.Instance.Resource.remapIndirectionTextureCompute, 0, threadGroups, threadGroups, 1);
            }
        }
    }
}