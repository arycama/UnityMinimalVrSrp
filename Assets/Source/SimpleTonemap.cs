using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

public class SimpleTonemap : ViewRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: Header("Settings")]
        [field: SerializeField] public bool Hdr { get; private set; } = true;
        [field: SerializeField] public bool Tonemap { get; private set; } = true;
        [field: SerializeField] public float PaperWhite = 160.0f;
        [field: SerializeField] public float MinLuminance { get; private set; } = 0;
        [field: SerializeField] public float MaxLuminance { get; private set; } = 1000;
    }

    private readonly Material material;
    private readonly Settings settings;
    private readonly SimpleBloom.Settings bloomSettings;

    public SimpleTonemap(RenderGraph renderGraph, Settings settings, SimpleBloom.Settings bloomSettings) : base(renderGraph)
    {
        this.settings = settings;
        this.bloomSettings = bloomSettings;
        material = new Material(Shader.Find("Hidden/Tonemap")) { hideFlags = HideFlags.HideAndDontSave };

        var hdrSettings = HDROutputSettings.main;
        if (hdrSettings.available)
        {
            var gamut = hdrSettings.displayColorGamut;
            hdrSettings.paperWhiteNits = settings.PaperWhite;
            hdrSettings.automaticHDRTonemapping = false;
        }
    }

    public override void Render(ViewRenderData viewRenderData)
    {
        var primitiveCount = viewRenderData.camera.stereoEnabled && !SystemInfo.supportsMultiview ? 2 : 1;

        if (renderGraph.TryGetResource<XRDisplaySubsystemData>(out var displaySubsystemData))
        {
            using var pass = renderGraph.AddGenericRenderPass("Tonemapping");
            //pass.Initialize(material, 0, camera.stereoEnabled ? 2 : 1);

            displaySubsystemData.display.GetRenderPass(0, out var xrRenderPass);

            //pass.WriteTexture()

            //var renderMode = XRSettings.gameViewRenderMode;

            pass.ReadRtHandle<CameraTarget>();
            //pass.ReadRtHandle<CameraBloom>();

            var hdrSettings = HDROutputSettings.main;
            var hdrEnabled = settings.Hdr && hdrSettings.available;
            var colorGamut = ColorGamut.sRGB;
            if (hdrEnabled)
            {
                hdrSettings.RequestHDRModeChange(settings.Hdr);
                hdrSettings.automaticHDRTonemapping = false;
                hdrSettings.paperWhiteNits = settings.PaperWhite;
                colorGamut = hdrSettings.displayColorGamut;
            }

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("Tonemap", settings.Tonemap ? 1 : 0);
                pass.SetFloat("MinLuminance", settings.MinLuminance);
                pass.SetFloat("MaxLuminance", settings.MaxLuminance);
                pass.SetFloat("PaperWhite", settings.PaperWhite);
                pass.SetFloat("IsSceneView", viewRenderData.camera.cameraType == CameraType.SceneView ? 1 : 0);
                pass.SetFloat("IsPreview", viewRenderData.camera.cameraType == CameraType.Preview ? 1 : 0);
                pass.SetFloat("BloomStrength", bloomSettings.Strength);
                pass.SetFloat("ColorGamut", (int)colorGamut);
                pass.SetFloat("RenderMode", Application.isEditor ? (float)XRSettings.gameViewRenderMode : 0);

                command.SetRenderTarget(viewRenderData.target, 0, CubemapFace.Unknown, -1);

                if(viewRenderData.camera.stereoEnabled)
                {
                    command.EnableShaderKeyword(SystemInfo.supportsMultiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");
                    command.SetSinglePassStereo(SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing);
                }

                command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3 * primitiveCount, 1, pass.PropertyBlock);

                if (viewRenderData.camera.stereoEnabled)
                {
                    command.DisableShaderKeyword(SystemInfo.supportsMultiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");
                    command.SetSinglePassStereo(SinglePassStereoMode.None);
                }
            });
        }
        else
        {
            using var pass = renderGraph.AddBlitToScreenPass("Tonemapping");
            pass.Initialize(material, 0, primitiveCount);

            var renderMode = XRSettings.gameViewRenderMode;

            pass.ReadRtHandle<CameraTarget>();
            //pass.ReadRtHandle<CameraBloom>();

            var hdrSettings = HDROutputSettings.main;
            var hdrEnabled = settings.Hdr && hdrSettings.available;
            var colorGamut = ColorGamut.sRGB;
            if (hdrEnabled)
            {
                hdrSettings.RequestHDRModeChange(settings.Hdr);
                hdrSettings.automaticHDRTonemapping = false;
                hdrSettings.paperWhiteNits = settings.PaperWhite;
                colorGamut = hdrSettings.displayColorGamut;
            }

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("Tonemap", settings.Tonemap ? 1 : 0);
                pass.SetFloat("MinLuminance", settings.MinLuminance);
                pass.SetFloat("MaxLuminance", settings.MaxLuminance);
                pass.SetFloat("PaperWhite", settings.PaperWhite);
                pass.SetFloat("IsSceneView", viewRenderData.camera.cameraType == CameraType.SceneView ? 1 : 0);
                pass.SetFloat("IsPreview", viewRenderData.camera.cameraType == CameraType.Preview ? 1 : 0);
                pass.SetFloat("BloomStrength", bloomSettings.Strength);
                pass.SetFloat("ColorGamut", (int)colorGamut);
                pass.SetFloat("RenderMode", (float)XRSettings.gameViewRenderMode);

                if (viewRenderData.camera.stereoEnabled)
                    pass.AddKeyword(SystemInfo.supportsMultiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");
            });
        }
    }
}