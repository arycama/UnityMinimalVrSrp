using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Minimal Vr Render Pipeline")]
public class MinimalVrRenderPipelineAsset : RenderPipelineAsset
{
    public override Type pipelineType => typeof(MinimalVrRenderPipeline);

    public override string renderPipelineShaderTag => string.Empty;

    protected override RenderPipeline CreatePipeline() => new MinimalVrRenderPipeline();
}