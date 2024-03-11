using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NoOvertime.VirtualTexture
{
    public class VirtualImageAtlas : IDisposable
    {
        private readonly int _atlasSize; // indirect texture的大小
        private readonly int _pageSizeShift; // 1 << _pageSizeShift == _pageSize

        private readonly bool[] _markAsUsed; // 记录atlas四叉树中的节点是否被占用
        private readonly byte[] _markChildAsUsed; // 记录atlas四叉树中的节点中是否有子节点被占用
        private readonly int _minimalVirtualImageSize;

        private readonly Stack<int4> _travelStack = new();

        // 记录atlas中所有image所在的节点index和x,z坐标以及imageSize
        private readonly Dictionary<int2, int4> _sector2ImageDictionary = new();

#if UNITY_EDITOR && DEBUG_TERRAIN
        private readonly Dictionary<int2, GameObject> _debugImage = new();
        private readonly GameObject _canvas;
        private readonly GameObject _imagePrefab;
#endif

        public VirtualImageAtlas(int atlasSize, int pageSize, int pageSizeShift, int minimalVirtualImageSize)
        {
            _atlasSize = atlasSize;
#if UNITY_EDITOR && DEBUG_TERRAIN
            var isPow2 = math.ispow2(pageSize);
            if (!isPow2)
            {
                throw new ArgumentException("Page Size 必须是2的次幂.");
            }

            if (1 << pageSizeShift != pageSize)
            {
                throw new ArgumentException("Page Size 和 Page Size Shift不匹配.");
            }

            var canvasPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SampleScene/Canvas.prefab");
            _canvas = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(canvasPrefab);
            _canvas.hideFlags = HideFlags.HideAndDontSave;
            _imagePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SampleScene/Image.prefab");
#endif
            _pageSizeShift = pageSizeShift;
            _minimalVirtualImageSize = minimalVirtualImageSize;

            var nodeCount = 0;
            int currentSizeNodeCount = 1;
            int currentNodeSize = atlasSize;
            while (currentNodeSize > (minimalVirtualImageSize >> pageSizeShift))
            {
                nodeCount += currentSizeNodeCount;
                currentNodeSize >>= 1;
                currentSizeNodeCount <<= 2;
            }

            _markChildAsUsed = new byte[nodeCount]; // 父节点标记用不到最小级别的image
            _markAsUsed = new bool[nodeCount + currentSizeNodeCount];
        }

        public void Dispose()
        {
#if UNITY_EDITOR && DEBUG_TERRAIN
            foreach (var (_, instance) in _debugImage)
            {
                Object.DestroyImmediate(instance);
            }

            _debugImage.Clear();
            Object.DestroyImmediate(_canvas);
#endif
        }

        public int4 GetImageInfo(int2 sector)
        {
            return _sector2ImageDictionary[sector];
        }

        public bool InsertImage(in int2 sector, in int virtualImageSize, out int4 imageInfo)
        {
            if (virtualImageSize < _minimalVirtualImageSize)
            {
#if UNITY_EDITOR && DEBUG_TERRAIN
                Debug.LogWarning($"lizha @ {sector} request {virtualImageSize} < minimal({_minimalVirtualImageSize})");
#endif
                imageInfo = int4.zero;
                return false;
            }

            var indirectSize = virtualImageSize >> _pageSizeShift;
            _travelStack.Clear();
            // nodeIndex, x, z, size
            _travelStack.Push(new int4(0, 0, 0, _atlasSize));
            while (_travelStack.TryPop(out var currentNode))
            {
                // 如果node被占用了, 那么node的子节点肯定也被占用了
                if (_markAsUsed[currentNode.x]) continue;
                // 四叉树遍历到的节点大于请求的indirectSize
                if (currentNode.w > indirectSize)
                {
                    int halfSize = currentNode.w >> 1;
                    int childNodeIndex = currentNode.x << 2;
                    _travelStack.Push(new int4(childNodeIndex + 4, currentNode.y + halfSize, currentNode.z + halfSize, halfSize));
                    _travelStack.Push(new int4(childNodeIndex + 3, currentNode.y, currentNode.z + halfSize, halfSize));
                    _travelStack.Push(new int4(childNodeIndex + 2, currentNode.y + halfSize, currentNode.z, halfSize));
                    _travelStack.Push(new int4(childNodeIndex + 1, currentNode.y, currentNode.z, halfSize));
                }
                else // 不可能遍历到小于indirectSize的节点
                {
                    // 子节点里不能有节点被占用
                    if (virtualImageSize == _minimalVirtualImageSize || _markChildAsUsed[currentNode.x] == 0)
                    {
                        _markAsUsed[currentNode.x] = true; // 标记自身被占用
                        int parent = currentNode.x >> 2; // 标记所有父节点中有子节点被占用加一
                        while (parent != 0)
                        {
                            _markChildAsUsed[parent]++;
                            parent >>= 2;
                        }

                        _sector2ImageDictionary[sector] = currentNode;
                        imageInfo = currentNode;
#if UNITY_EDITOR && DEBUG_TERRAIN
                        if (!_debugImage.TryGetValue(sector, out var instance))
                        {
                            instance = (GameObject) UnityEditor.PrefabUtility.InstantiatePrefab(_imagePrefab, _canvas.transform);
                            instance.hideFlags = HideFlags.HideAndDontSave;
                            _debugImage[sector] = instance;
                        }

                        var rectTransform = instance.GetComponent<RectTransform>();
                        instance.SetActive(true);
                        rectTransform.position = new Vector3(imageInfo.y + 1, imageInfo.z + 1);
                        rectTransform.sizeDelta = new Vector2(imageInfo.w - 2, imageInfo.w - 2);
                        var image = instance.GetComponent<Image>();
                        var color = Color.HSVToRGB(math.log2(imageInfo.w) / 8, 1, 1);
                        color.a = 0.5f;
                        image.color = color;
#endif
                        return true;
                    }
                }
            }

            imageInfo = int4.zero;
            return false; // atlas 满了?
        }

        /// <summary>
        /// VirtualImageReallocate以后用于移除老的占用部分
        /// </summary>
        public void RemoveImage(in int4 imageInfo)
        {
            _markAsUsed[imageInfo.x] = false;
            int parent = imageInfo.x >> 2; // 标记所有父节点中有子节点被占用减一
            while (parent != 0)
            {
                _markChildAsUsed[parent]--;
                parent >>= 2;
            }
        }

        /// <summary>
        /// 从VirtualImageAtlas中移除整个Sector
        /// </summary>
        public bool RemoveImage(in int2 sector)
        {
            if (_sector2ImageDictionary.TryGetValue(sector, out var imageInfo))
            {
                RemoveImage(imageInfo);
                _sector2ImageDictionary.Remove(sector);
#if UNITY_EDITOR && DEBUG_TERRAIN
                _debugImage[sector].SetActive(false);
#endif
            }

            return false;
        }
    }
}