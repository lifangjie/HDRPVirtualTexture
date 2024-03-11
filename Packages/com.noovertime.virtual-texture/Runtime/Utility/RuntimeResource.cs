using System;
using UnityEngine;

namespace NoOvertime.VirtualTexture
{
    [Serializable]
    public struct RuntimeResource
    {
        public Texture2D splatMap;
        public Texture2DArray baseMapArray;
        public Texture2DArray maskMapArray;
    }
}