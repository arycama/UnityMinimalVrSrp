using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PrecomputeDfg : FrameRenderFeature
{
    public override string ProfilerNameOverride => "Precompute Dfg";

    private readonly Material precomputeDfgMaterial;
    private readonly ResourceHandle<RenderTexture> precomputedDfg;

    public PrecomputeDfg(RenderGraph renderGraph) : base(renderGraph)
    {
        precomputeDfgMaterial = new Material(Shader.Find("Hidden/PrecomputeDfg")) { hideFlags = HideFlags.HideAndDontSave };

        precomputedDfg = renderGraph.GetTexture(32, GraphicsFormat.R16G16_UNorm, isPersistent: true, isExactSize: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Precompute Dfg"))
        {
            pass.Initialize(precomputeDfgMaterial, 0);
            pass.WriteTexture(precomputedDfg);
        }

        renderGraph.SetResource(new PrecomputedDfg(precomputedDfg), true);
    }

    protected override void Cleanup(bool disposing)
    {
        renderGraph.ReleasePersistentResource(precomputedDfg, -1);
    }

    public override void Render(ScriptableRenderContext context)
    {
    }
}
