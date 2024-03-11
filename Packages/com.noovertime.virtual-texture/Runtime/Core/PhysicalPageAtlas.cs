using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace NoOvertime.VirtualTexture
{
    public class PhysicalPageAtlas : IDisposable
    {
        private readonly RTHandle _physicalPageBaseMapAtlas;
        private readonly RTHandle _physicalPageMaskMapAtlas;
        private readonly uint[] _paramsArray = new uint[6];
        private readonly ComputeBuffer _paramsBuffer;

        // ReSharper disable once ConvertConstructorToMemberInitializers
        public PhysicalPageAtlas()
        {
            _physicalPageBaseMapAtlas = RTHandles.Alloc(Constant.PageSizeWithBorder, Constant.PageSizeWithBorder, Constant.MaxPhysicalPageCount,
                colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureDimension.Tex2DArray, enableRandomWrite: true);
            _physicalPageMaskMapAtlas = RTHandles.Alloc(Constant.PageSizeWithBorder, Constant.PageSizeWithBorder, Constant.MaxPhysicalPageCount,
                colorFormat: GraphicsFormat.R8G8B8A8_UNorm, dimension: TextureDimension.Tex2DArray, enableRandomWrite: true);
            _paramsBuffer = new ComputeBuffer(6, 4, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        }

        public void Dispose()
        {
            RTHandles.Release(_physicalPageBaseMapAtlas);
            RTHandles.Release(_physicalPageMaskMapAtlas);
            _paramsBuffer.Dispose();
        }

        public RTHandle PhysicalPageBaseMapAtlas => _physicalPageBaseMapAtlas;
        public RTHandle PhysicalPageMaskMapAtlas => _physicalPageMaskMapAtlas;

        public void Render(CommandBuffer cmd, in int2 sector, in int2 localPageID, in int4 pageID, in int index)
        {
            _paramsArray[0] = (uint)sector.x;
            _paramsArray[1] = (uint)sector.y;
            _paramsArray[2] = (uint)localPageID.x;
            _paramsArray[3] = (uint)localPageID.y;
            _paramsArray[4] = (uint)pageID.z; // mip
            _paramsArray[5] = (uint)pageID.w; // virtual image size log2
            cmd.SetBufferData(_paramsBuffer, _paramsArray);
            cmd.SetComputeBufferParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.ParamsID, _paramsBuffer);
            cmd.SetComputeIntParam(Context.Instance.Resource.renderingPhysicalPageCompute, Constant.TargetIndexID, index);
            //cmd.SetGlobalTexture(Constant.SplatMapID, Context.Instance.RuntimeResource.splatMap);
            cmd.SetComputeTextureParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.SplatMapID, Context.Instance.RuntimeResource.splatMap);
            cmd.SetComputeTextureParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.BaseMapArrayID, Context.Instance.RuntimeResource.baseMapArray);
            cmd.SetComputeTextureParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.MaskMapArrayID, Context.Instance.RuntimeResource.maskMapArray);
            cmd.SetComputeTextureParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.RWPhysicalPageBaseMapAtlasID, _physicalPageBaseMapAtlas);
            cmd.SetComputeTextureParam(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.RWPhysicalPageMaskMapAtlasID, _physicalPageMaskMapAtlas);
            cmd.DispatchCompute(Context.Instance.Resource.renderingPhysicalPageCompute, 0, Constant.PageSizeWithBorder / 4, Constant.PageSizeWithBorder / 4, 1);
        }
    }
}