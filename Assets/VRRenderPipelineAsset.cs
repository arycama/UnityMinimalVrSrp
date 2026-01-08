using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "VR Render Pipeline")]
public class VRRenderPipelineAsset : RenderPipelineAsset
{
    public override Type pipelineType => typeof(VRRenderPipeline);

    public override string renderPipelineShaderTag => string.Empty;

    protected override RenderPipeline CreatePipeline() => new VRRenderPipeline();
}