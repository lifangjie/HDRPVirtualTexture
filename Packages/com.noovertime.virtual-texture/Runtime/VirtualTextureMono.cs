using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    [ExecuteInEditMode]
    public class VirtualTextureMono : MonoBehaviour
    {
        public Resource resource;
        public int terrainSize;
        public RuntimeResource runtimeResource;

        private CorePass _corePass;

        private void OnEnable()
        {
            Assert.AreEqual(terrainSize % 64, 0, $"TerrainSize 必须是 SectorSize({Constant.SectorSize}) 的整数倍.");
            Context.Instance.Initialize(terrainSize, resource, runtimeResource);
            Graphics.Blit(Texture2D.whiteTexture, Context.Instance.IndirectionTexture.RT);
            _corePass = new CorePass();
            bool registerPass = CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeRendering, _corePass);
            Assert.IsTrue(registerPass);
        }

        private void OnDisable()
        {
            // TODO: async read back will hold native array, should cancel request first
            bool unregisterPass = CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforeRendering, _corePass);
            Assert.IsTrue(unregisterPass);
            _corePass.Dispose();
            Context.Instance.Dispose();
        }
    }
}