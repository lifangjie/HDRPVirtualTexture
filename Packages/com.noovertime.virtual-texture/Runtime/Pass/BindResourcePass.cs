using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class BindResourcePass : CustomPass
    {
        protected override void Execute(CustomPassContext ctx)
        {
            ctx.cmd.SetGlobalTexture(Constant.Sector2VirtualImageInfoTextureID, Context.Instance.Sector2VirtualImageInfoTexture.RT);
            ctx.cmd.SetGlobalTexture(Constant.IndirectionTextureID, Context.Instance.IndirectionTexture.RT);
            ctx.cmd.SetGlobalTexture(Constant.PhysicalPageBaseMapAtlasID, Context.Instance.PhysicalPageAtlas.PhysicalPageBaseMapAtlas);
            ctx.cmd.SetGlobalTexture(Constant.PhysicalPageMaskMapAtlasID, Context.Instance.PhysicalPageAtlas.PhysicalPageMaskMapAtlas);
            ctx.cmd.EnableKeyword(Context.Instance.VirtualTextureKeyword);
        }
    }
}