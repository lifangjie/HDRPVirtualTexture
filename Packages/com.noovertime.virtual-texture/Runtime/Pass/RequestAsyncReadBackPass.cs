using System;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class RequestAsyncReadBackPass : CustomPass, IDisposable
    {
        private ProfilerMarker _requestAsyncReadBackMarker;
        private readonly Action<AsyncGPUReadbackRequest> _callback;
        public bool IsReadingBack;

        public RequestAsyncReadBackPass(Action<AsyncGPUReadbackRequest> callback, ProfilerMarker requestAsyncReadBackMarker)
        {
            _callback = callback;
            _requestAsyncReadBackMarker = requestAsyncReadBackMarker;
        }

        public async void Dispose()
        {
            var readBackArray = Context.Instance.ReadBackArray;
            if (IsReadingBack)
            {
                await Task.Yield();
            }

            if (readBackArray.IsCreated) readBackArray.Dispose();
        }

        protected override void Execute(CustomPassContext ctx)
        {
            using (_requestAsyncReadBackMarker.Auto())
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!Context.Instance.ReadBackArray.IsCreated)
                {
                    Debug.LogError("lizha @ wtf?");
                }
                var safetyHandle = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref Context.Instance.ReadBackArray, safetyHandle);
#endif
                ctx.cmd.DisableKeyword(Context.Instance.OutputPageIDTextureKeyword);
                ctx.cmd.RequestAsyncReadbackIntoNativeArray(ref Context.Instance.ReadBackArray, Context.Instance.PageIDOutputTexture, 0, GraphicsFormat.R32_UInt, _callback);
            }
        }
    }
}