using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.TestTools;

namespace NoOvertime.VirtualTexture.Tests.Editor
{
    public class VirtualImageAtlasTests
    {
        private VirtualImageAtlas _virtualImageAtlas;
        private GameObject _canvas;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _virtualImageAtlas = new VirtualImageAtlas(Constant.IndirectionTextureSize, Constant.PageSize, Constant.PageSizeShift, Constant.MinimalVirtualImageSize);
            _canvas = new GameObject("Canvas");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _virtualImageAtlas.Dispose();
            Object.DestroyImmediate(_canvas);
            yield return null;
        }

        [UnityTest]
        public IEnumerator VirtualImageAtlasTest()
        {
            // prepare sectors
            Dictionary<int2, int4> sectors = new Dictionary<int2, int4>();
            const int range = 9;
            for (int x = 0; x < range; x++)
            {
                for (int z = 0; z < range; z++)
                {
                    sectors.Add(new int2(x, z), int4.zero);
                }
            }

            // prepare camera positions
            List<float2> cameraPositions = new List<float2>();
            for (int x = 0; x < Constant.SectorSize; x++)
            {
                for (int z = 0; z < Constant.SectorSize; z++)
                {
                    cameraPositions.Add(new float2(x, z) + math.floor(range * 0.5f) * Constant.SectorSize + 0.5f);
                }
            }

            Dictionary<int2, (int4, int4)> delayRemoveImages = new Dictionary<int2, (int4, int4)>();
            yield return null;
            foreach (var cameraPosition in cameraPositions)
            {
                delayRemoveImages.Clear();
                foreach (var (sector, oldImageInfo) in sectors)
                {
                    var sectorPosition = new float2(sector * Constant.SectorSize + Constant.SectorSize / 2);
                    var virtualImageSize = Utility.CalculateTargetImageSize(sectorPosition, cameraPosition);
                    if (virtualImageSize < Constant.MinimalVirtualImageSize) continue;
                    int oldVirtualImageSize = oldImageInfo.w << Constant.PageSizeShift;
                    if (virtualImageSize != oldVirtualImageSize)
                    {
                        _virtualImageAtlas.InsertImage(sector, virtualImageSize, out var imageInfo);
                        delayRemoveImages[sector] = (oldImageInfo, imageInfo);
                        Assert.AreNotEqual(imageInfo.w, 0, $"InsertImage of sector:{sector}, size:{virtualImageSize} failed.");
                        bool crossMoreThanOne = oldVirtualImageSize != 0 && oldVirtualImageSize / virtualImageSize != 2 && virtualImageSize / oldVirtualImageSize != 2;
                        if (crossMoreThanOne)
                        {
                            Debug.LogWarning("跨越超过1级mip的 virtual image size 切换.");
                        }
                        //Assert.IsFalse(crossMoreThanOne, "跨越超过1级mip的 virtual image size 切换.");
                    }
                }

                foreach (var (sector, (oldImageInfo, newImageInfo)) in delayRemoveImages)
                {
                    sectors.Remove(sector);
                    sectors.Add(sector, newImageInfo);
                    _virtualImageAtlas.RemoveImage(oldImageInfo);
                }

                yield return null;
            }

            yield return null;
        }
    }
}