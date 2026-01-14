using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalVR
{
    [CreateAssetMenu(menuName = "Data/Vr Render Pipeline")]
    public class VrRenderPipelineAsset : CustomRenderPipelineAssetBase
    {
        [field: SerializeField] public EnvironmentLightingSettings EnvironmentLighting { get; private set; }
        [field: SerializeField] public SimpleBloom.Settings Bloom { get; private set; }
        [field: SerializeField] public SimpleTonemap.Settings Tonemapping { get; private set; }

        public override bool UseSrpBatching { get; } = false;

        public override Type pipelineType => typeof(VrRenderPipeline);

        public override string renderPipelineShaderTag => "KaijuRenderPipeline";

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
            supportsHDR = true,
        };

        protected override RenderPipeline CreatePipeline()
        {
            return new VrRenderPipeline(this);
        }
    }
}
