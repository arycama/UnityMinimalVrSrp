using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Native Render Pass Pipeline")]
public class NativeRenderPassPipelineAsset : RenderPipelineAsset
{
    [field: SerializeField] public bool UseRenderPass { get; private set; } = true;

    public override Type pipelineType => typeof(NativeRenderPassPipeline);

    public override string renderPipelineShaderTag => string.Empty;

    protected override RenderPipeline CreatePipeline() => new NativeRenderPassPipeline(this);
}