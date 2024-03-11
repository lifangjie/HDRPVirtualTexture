using UnityEngine.Rendering.HighDefinition;

namespace NoOvertime.VirtualTexture
{
    public class ClearPass : CustomPass
    {
        protected override void Execute(CustomPassContext ctx)
        {
            ctx.cmd.DisableKeyword(Context.Instance.OutputPageIDTextureKeyword);
            ctx.cmd.DisableKeyword(Context.Instance.VirtualTextureKeyword);
        }
    }
}