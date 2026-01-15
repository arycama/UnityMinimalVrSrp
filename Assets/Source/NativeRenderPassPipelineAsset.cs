using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Native Render Pass Pipeline")]
public class NativeRenderPassPipelineAsset : CustomRenderPipelineAssetBase
{
    [field: SerializeField] public bool UseRenderPass { get; private set; } = true;

    public override Type pipelineType => typeof(NativeRenderPassPipeline);

    public override string renderPipelineShaderTag => string.Empty;

    public override SupportedRenderingFeatures SupportedRenderingFeatures => new()
    {
        defaultMixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
        editableMaterialRenderQueue = false,
        enlighten = false,
        lightmapBakeTypes = LightmapBakeType.Realtime,
        lightmapsModes = LightmapsMode.NonDirectional,
        lightProbeProxyVolumes = false,
        mixedLightingModes = SupportedRenderingFeatures.LightmapMixedBakeModes.None,
        motionVectors = true,
        overridesEnvironmentLighting = false,
        overridesFog = false,
        overridesMaximumLODLevel = false,
        overridesOtherLightingSettings = true,
        overridesRealtimeReflectionProbes = true,
        overridesShadowmask = true,
        particleSystemInstancing = true,
        receiveShadows = true,
        reflectionProbeModes = SupportedRenderingFeatures.ReflectionProbeModes.None,
        reflectionProbes = false,
        rendererPriority = false,
        rendererProbes = false,
        rendersUIOverlay = true,
        ambientProbeBaking = false,
        defaultReflectionProbeBaking = false,
        reflectionProbesBlendDistance = false,
        overridesEnableLODCrossFade = true,
        overridesLightProbeSystem = true,
        overridesLightProbeSystemWarningMessage = default,
        overridesLODBias = false,
        skyOcclusion = false,
        supportsClouds = false,
        supportsHDR = false
    };

    public override bool UseSrpBatching => false;

    protected override RenderPipeline CreatePipeline() => new NativeRenderPassPipeline(this);
}