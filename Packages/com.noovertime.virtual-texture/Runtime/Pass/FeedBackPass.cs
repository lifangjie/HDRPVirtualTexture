using System;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class FeedBackPass : CustomPass, IDisposable
    {
        private ProfilerMarker _feedBackMarker;
        private int _frameCount;

        public FeedBackPass(ProfilerMarker feedBackMarker)
        {
            _feedBackMarker = feedBackMarker;
        }

        public void Dispose()
        {
            if (Context.Instance.PageIDOutputTexture != null)
            {
                RTHandles.Release(Context.Instance.PageIDOutputTexture);
                Context.Instance.PageIDOutputTexture = null;
            }
        }

        protected override void Execute(CustomPassContext ctx)
        {
            using (_feedBackMarker.Auto())
            {
                _frameCount++;
                _frameCount %= 64;
                var dither = Constant.BayerDither8X8[_frameCount / Constant.PageIDTextureDownscale, _frameCount % Constant.PageIDTextureDownscale];
                var referenceSize = ctx.cameraDepthBuffer.referenceSize;
                var size = new Vector2Int(
                    (int)(referenceSize.x / (float)Constant.PageIDTextureDownscale) + 1,
                    (int)(referenceSize.y / (float)Constant.PageIDTextureDownscale) + 1);
                AllocatePageIDOutputTexture(size);
                ctx.cmd.SetComputeTextureParam(Context.Instance.Resource.clearPageIDOutputTextureCompute, 0, Constant.RWPageIDOutputTexture, Context.Instance.PageIDOutputTexture);
                ctx.cmd.DispatchCompute(Context.Instance.Resource.clearPageIDOutputTextureCompute, 0, size.x / 8 + 1, size.y / 8 + 1, 1);
                ctx.cmd.SetGlobalInteger("VirtualDitherX", dither / Constant.PageIDTextureDownscale);
                ctx.cmd.SetGlobalInteger("VirtualDitherY", dither % Constant.PageIDTextureDownscale);
                ctx.cmd.SetRandomWriteTarget(7, Context.Instance.PageIDOutputTexture);
                ctx.cmd.EnableKeyword(Context.Instance.OutputPageIDTextureKeyword);
            }
        }

        private void AllocatePageIDOutputTexture(Vector2Int size)
        {
            if (Context.Instance.PageIDOutputTexture != null && Context.Instance.PageIDOutputTexture.referenceSize != size)
            {
                RTHandles.Release(Context.Instance.PageIDOutputTexture);
                Context.Instance.PageIDOutputTexture = null;
                Context.Instance.ReadBackArray.Dispose();
            }

            if (Context.Instance.PageIDOutputTexture == null)
            {
                Context.Instance.PageIDOutputTexture = RTHandles.Alloc(size.x, size.y, colorFormat: GraphicsFormat.R32_UInt, enableRandomWrite: true, name: nameof(Context.Instance.PageIDOutputTexture));
                Context.Instance.ReadBackArray = new NativeArray<uint>(size.x * size.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }
    }
}