using UnityEngine;
using UnityEngine.Rendering;

public readonly struct PrecomputedDfg : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> precomputeDfg;

    public PrecomputedDfg(ResourceHandle<RenderTexture> precomputeDfg)
    {
        this.precomputeDfg = precomputeDfg;
    }

    readonly void IRenderPassData.SetInputs(RenderPass pass)
    {
        pass.ReadTexture("PrecomputedDfg", precomputeDfg);
    }

    readonly void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}