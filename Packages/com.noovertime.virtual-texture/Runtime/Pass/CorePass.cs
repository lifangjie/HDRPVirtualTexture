using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class CorePass : CustomPass, IDisposable
    {
        private Stage _stage;
        private JobHandle _jobHandle;
        private readonly BindResourcePass _bindResourcePass;
        private readonly ReallocateVirtualPagePass _reallocateVirtualPagePass;
        private readonly FeedBackPass _feedBackPass;
        private readonly RequestAsyncReadBackPass _requestAsyncReadBackPass;
        private readonly RenderingPagePass _renderingPagePass;
        private readonly ClearPass _clearPass;
        private NativeHashSet<uint> _deduplicateSet;
        private readonly PackedPageIDComparer _pageIDComparer;

        public CorePass()
        {
            _bindResourcePass = new BindResourcePass();
            _reallocateVirtualPagePass = new ReallocateVirtualPagePass(_reallocateVirtualPageMarker)
            {
                enabled = false
            };
            _feedBackPass = new FeedBackPass(_feedBackMarker)
            {
                enabled = false
            };
            _requestAsyncReadBackPass = new RequestAsyncReadBackPass(Callback, _requestAsyncReadBackMarker)
            {
                enabled = false
            };
            _renderingPagePass = new RenderingPagePass(_renderingPageMarker)
            {
                enabled = false
            };
            _clearPass = new ClearPass();
            bool registerBindResourcePass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _bindResourcePass, -1);
            bool registerReallocateVirtualPagePass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _reallocateVirtualPagePass);
            bool registerFeedBackPass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _feedBackPass);
            bool registerRequestAsyncReadBackPass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _requestAsyncReadBackPass);
            bool registerRenderingPagePass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _renderingPagePass);
            bool registerClearPass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.AfterOpaqueDepthAndNormal, _clearPass);
            Assert.IsTrue(registerBindResourcePass);
            Assert.IsTrue(registerReallocateVirtualPagePass);
            Assert.IsTrue(registerFeedBackPass);
            Assert.IsTrue(registerRequestAsyncReadBackPass);
            Assert.IsTrue(registerRenderingPagePass);
            Assert.IsTrue(registerClearPass);
            _deduplicateSet = new NativeHashSet<uint>(Constant.MaxDeduplicatedPageCount, Allocator.Persistent);
            _pageIDComparer = new PackedPageIDComparer();
        }

        protected override void Execute(CustomPassContext ctx)
        {
            _jobHandle.Complete();
            switch (_stage)
            {
                case Stage.Waiting:
                    Waiting();
                    break;
                case Stage.ReallocateVirtualPage:
                    ReallocateVirtualPage();
                    break;
                case Stage.FeedBack:
                    FeedBack();
                    break;
                case Stage.RequestAsyncReadBack:
                    RequestAsyncReadBack();
                    break;
                case Stage.ReadingBack:
                    ReadingBack();
                    break;
                case Stage.Deduplicate:
                    Deduplicate();
                    break;
                case Stage.SortPageID:
                    SortPageID();
                    break;
                case Stage.RenderingPage:
                    RenderingPage();
                    break;
                default:
                    throw new ArgumentException($"Unknown Stage:{_stage}");
            }
        }

        private ProfilerMarker _waitingMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(Waiting)}");

        /// <summary>
        /// 可能可以用于检查摄像机是否有变动
        /// 暂时不实现
        /// </summary>
        private void Waiting()
        {
            using (_waitingMarker.Auto())
            {
                _stage = Stage.ReallocateVirtualPage;
            }
        }

        private ProfilerMarker _reallocateVirtualPageMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(ReallocateVirtualPage)}");

        private void ReallocateVirtualPage()
        {
            using (_reallocateVirtualPageMarker.Auto())
            {
                _reallocateVirtualPagePass.enabled = true;
                _stage = Stage.FeedBack;
            }
        }

        private ProfilerMarker _feedBackMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(FeedBack)}");

        private void FeedBack()
        {
            using (_feedBackMarker.Auto())
            {
                _reallocateVirtualPagePass.enabled = false;
                _feedBackPass.enabled = true;
                _stage = Stage.RequestAsyncReadBack;
            }
        }

        private ProfilerMarker _requestAsyncReadBackMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(RequestAsyncReadBack)}");

        private void RequestAsyncReadBack()
        {
            using (_requestAsyncReadBackMarker.Auto())
            {
                _readingBackTimeout = 0;
                _feedBackPass.enabled = false;
                _requestAsyncReadBackPass.enabled = true;
                _requestAsyncReadBackPass.IsReadingBack = true;
                _stage = Stage.ReadingBack;
            }
        }

        private static ProfilerMarker _readingBackMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(ReadingBack)}");
        private int _readingBackTimeout;

        private void ReadingBack()
        {
            using (_readingBackMarker.Auto())
            {
                _requestAsyncReadBackPass.enabled = false;
                _readingBackTimeout++;
                if (_readingBackTimeout > Constant.ReadingBackTimeout)
                {
#if UNITY_EDITOR && DEBUG_TERRAIN
                    Debug.LogError($"lizha @ async read back timeout detect.");
#endif
                    _stage = Stage.Waiting;
                }
            }
        }

        private void Callback(AsyncGPUReadbackRequest request)
        {
            using (_readingBackMarker.Auto())
            {
                _requestAsyncReadBackPass.IsReadingBack = false;
#if UNITY_EDITOR && DEBUG_TERRAIN
                if (request.hasError)
                {
                    Debug.LogError($"lizha @ async read back error detect.");
                }
#endif
                _stage = Stage.Deduplicate;
            }
        }

        private ProfilerMarker _deduplicateMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(Deduplicate)}");

        private void Deduplicate()
        {
            using (_deduplicateMarker.Auto())
            {
                var deduplicateJob = new DeduplicateJob(_deduplicateSet, Context.Instance.ReadBackArray, Context.Instance.ResolvedPageID, Context.Instance.ImageInfo);
                _jobHandle = deduplicateJob.Schedule();
                _stage = Stage.SortPageID;
            }
        }

        private ProfilerMarker _sortPageIDMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(SortPageID)}");

        private void SortPageID()
        {
            using (_sortPageIDMarker.Auto())
            {
                Context.Instance.ResolvedPageID.Sort(_pageIDComparer);
                _renderingPagePass.StartRendering();
                _renderingPagePass.enabled = true;
                _stage = Stage.RenderingPage;
            }
        }

        private ProfilerMarker _renderingPageMarker = new ProfilerMarker(ProfilerCategory.Render, $"VirtualTexture.Execute.{nameof(RenderingPage)}");

        private void RenderingPage()
        {
            using (_renderingPageMarker.Auto())
            {
                if (_renderingPagePass.IsRenderingFinished())
                {
                    _renderingPagePass.enabled = false;
                    _stage = Stage.Waiting;
                }
            }
        }

        [BurstCompile]
        private struct DeduplicateJob : IJob
        {
            private NativeHashSet<uint> _deduplicateSet;
            [ReadOnly] private readonly NativeArray<uint> _readBackArray;
            private NativeList<uint> _deduplicatedPageID;
            [ReadOnly] private NativeHashSet<int3> _imageInfo;

            public DeduplicateJob(NativeHashSet<uint> deduplicateSet, NativeArray<uint> readBackArray, NativeList<uint> deduplicatedPageID, NativeHashSet<int3> imageInfo)
            {
                _deduplicateSet = deduplicateSet;
                _readBackArray = readBackArray;
                _deduplicatedPageID = deduplicatedPageID;
                _imageInfo = imageInfo;
            }

            public void Execute()
            {
                _deduplicateSet.Clear();
                _deduplicatedPageID.Clear();
                foreach (var packedPageID in _readBackArray)
                {
                    if (packedPageID == 0) continue;
                    _deduplicateSet.Add(packedPageID);
                }

                foreach (var imageInfo in _imageInfo)
                {
                    var virtualImageSizeLog = math.ceillog2(imageInfo.z);
                    uint additionalPageID =
                        (((uint)(imageInfo.x >> virtualImageSizeLog) & 0xFFF) << 20) +
                        (((uint)(imageInfo.y >> virtualImageSizeLog) & 0xFFF) << 8) +
                        ((uint)virtualImageSizeLog << 4) +
                        (uint)virtualImageSizeLog;
                    _deduplicateSet.Add(additionalPageID);
                }

                foreach (var pageID in _deduplicateSet)
                {
                    _deduplicatedPageID.Add(pageID);
                }
            }
        }

        [BurstCompile]
        private struct PackedPageIDComparer : IComparer<uint>
        {
            public int Compare(uint x, uint y)
            {
                var key0 = Utility.UnpackPageID(x);
                var key1 = Utility.UnpackPageID(y);
                return (key0.w - key0.z) - (key1.w - key1.z);
            }
        }

        public void Dispose()
        {
            _jobHandle.Complete();
            bool unregisterBindResourcePass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _bindResourcePass);
            bool unregisterReallocateVirtualPagePass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _reallocateVirtualPagePass);
            bool unregisterFeedBackPass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _feedBackPass);
            bool unregisterRequestAsyncReadBackPass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _requestAsyncReadBackPass);
            bool unregisterRenderingPagePass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _renderingPagePass);
            bool unregisterClearPass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.AfterOpaqueDepthAndNormal, _clearPass);
            Assert.IsTrue(unregisterBindResourcePass);
            Assert.IsTrue(unregisterReallocateVirtualPagePass);
            Assert.IsTrue(unregisterFeedBackPass);
            Assert.IsTrue(unregisterRequestAsyncReadBackPass);
            Assert.IsTrue(unregisterRenderingPagePass);
            Assert.IsTrue(unregisterClearPass);
#if UNITY_EDITOR
            CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeGBuffer, _bindResourcePass);
#endif
            _reallocateVirtualPagePass.Dispose();
            _feedBackPass.Dispose();
            _requestAsyncReadBackPass.Dispose();
            _renderingPagePass.Dispose();
            _deduplicateSet.Dispose();
        }

        private enum Stage
        {
            Waiting,
            ReallocateVirtualPage,
            FeedBack,
            RequestAsyncReadBack,
            ReadingBack,
            Deduplicate,
            SortPageID,
            RenderingPage
        }
    }
}