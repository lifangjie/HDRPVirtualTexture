using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;

namespace NoOvertime.VirtualTexture
{
    public static class Utility
    {
        /// <summary>
        /// virtual page X, virtual page Y, mip, log2(virtual page size)
        /// </summary>
        [BurstCompile]
        public static int4 UnpackPageID(uint packedPageID)
        {
            return new int4((int)(packedPageID >> 20), (int)((packedPageID >> 8) & 0xFFF), (int)((packedPageID >> 4) & 0xF), (int)(packedPageID & 0xF));
        }

        public static ulong EncodeLRUKey(in int2 sector, in int2 localPageID, int mip, int virtualImageSizeLog)
        {
            var virtualPageMip0 = Constant.MaxVirtualPageSize * sector + (localPageID << (Constant.MaxVirtualPageSizeShift - virtualImageSizeLog));
            int physicalMip = (Constant.MaxVirtualPageSizeShift - virtualImageSizeLog + mip);
            int2 virtualPageID = virtualPageMip0 >> physicalMip;
            // 8bit empty, 24bit x, 24bit y, 8bit mip
            return ((ulong)virtualPageID.x << 32) + ((ulong)virtualPageID.y << 8) + (ulong)physicalMip;
        }

        public static (int2, int) DecodeLRUKey(ulong lruKey)
        {
            int2 virtualPageID;
            virtualPageID.x = (int)(lruKey >> 32) & 0xFFFFFF;
            virtualPageID.y = (int)(lruKey >> 8) & 0xFFFFFF;
            int physicalMip = (int)(lruKey) & 0xFF;
            return (virtualPageID, physicalMip);
        }

        public static int CalculateTargetImageSize(in float2 sectorPosition, in float2 cameraPosition)
        {
            float distance = math.lengthsq((sectorPosition - cameraPosition));
            float t = (distance / Constant.SwitchDistance);
            int lodImage = 0;
            if (t >= 1)
            {
                lodImage = (int)math.log2(t) + 1;
            }

            int virtualImageSize = Constant.HighestResolution >> lodImage;
            return virtualImageSize;
        }
    }

    public class LinkedListNodeCache<T>
    {
        private int _nodesCreated = 0;
        private LinkedList<T> _nodeCache;

        /// <summary>
        /// Creates or returns a LinkedListNode of the requested type and set the value.
        /// </summary>
        /// <param name="val">The value to set to returned node to.</param>
        /// <returns>A LinkedListNode with the value set to val.</returns>
        public LinkedListNode<T> Acquire(T val)
        {
            if (_nodeCache != null)
            {
                var n = _nodeCache.First;
                if (n != null)
                {
                    _nodeCache.RemoveFirst();
                    n.Value = val;
                    return n;
                }
            }

            _nodesCreated++;
            return new LinkedListNode<T>(val);
        }

        /// <summary>
        /// Release the linked list node for later use.
        /// </summary>
        /// <param name="node"></param>
        public void Release(LinkedListNode<T> node)
        {
            if (_nodeCache == null)
                _nodeCache = new LinkedList<T>();

            node.Value = default(T);
            _nodeCache.AddLast(node);
        }

        internal int CreatedNodeCount => _nodesCreated;

        internal int CachedNodeCount
        {
            get => _nodeCache == null ? 0 : _nodeCache.Count;
            set
            {
                if (_nodeCache == null)
                    _nodeCache = new LinkedList<T>();
                while (value < _nodeCache.Count)
                    _nodeCache.RemoveLast();
                while (value > _nodeCache.Count)
                    _nodeCache.AddLast(new LinkedListNode<T>(default));
            }
        }
    }
}