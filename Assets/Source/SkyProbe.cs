using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class SkyProbe : ViewRenderFeature
{
    private readonly EnvironmentLightingSettings environmentLighting;
    private readonly Material defaultEnvironmentMaterial;

    public SkyProbe(RenderGraph renderGraph, EnvironmentLightingSettings environmentLighting) : base(renderGraph)
    {
        this.environmentLighting = environmentLighting;
        defaultEnvironmentMaterial = new Material(Shader.Find("Hidden/Default Skybox")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(ViewRenderData viewRenderData)
    {
        var material = RenderSettings.skybox;
        var passIndex = material == null ? -1 : material.FindPass("Reflection Probe");

        var reflectionProbeTemp = renderGraph.GetTexture(environmentLighting.Resolution, GraphicsFormat.B10G11R11_UFloatPack32, hasMips: true, autoGenerateMips: true);

        if (material == null || passIndex == -1)
        {
            material = defaultEnvironmentMaterial;
            passIndex = 0;
        }

        using var pass = renderGraph.AddFullscreenRenderPass("Environment Cubemap");
        pass.Initialize(material, passIndex);
        pass.WriteTexture(reflectionProbeTemp);

        renderGraph.SetResource(new EnvironmentProbeTempResult(reflectionProbeTemp));
    }
}
