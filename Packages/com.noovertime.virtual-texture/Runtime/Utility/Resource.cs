using UnityEngine;

namespace NoOvertime.VirtualTexture
{
    public class Resource : ScriptableObject
    {
        public ComputeShader clearPageIDOutputTextureCompute;
        public ComputeShader clearSector2VirtualImageInfoTextureCompute;
        public ComputeShader updateSector2VirtualImageInfoTextureCompute;
        public ComputeShader updateIndirectionTextureCompute;
        public ComputeShader remapIndirectionTextureCompute;
        public ComputeShader renderingPhysicalPageCompute;
        public Material frameDebuggerMaterial;
    }
}