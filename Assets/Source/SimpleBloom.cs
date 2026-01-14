using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class SimpleBloom : ViewRenderFeature
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0f, 1f)] public float Strength { get; private set; } = 0.125f;
        [field: SerializeField, Range(2, 12)] public int MaxMip { get; private set; } = 6;
    }

    private readonly Settings settings;
    private readonly Material material;

    public SimpleBloom(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;
        material = new Material(Shader.Find("Hidden/Simple Bloom")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public override void Render(ViewRenderData viewRenderData)
    {
        renderGraph.AddProfileBeginPass("Bloom");

        var bloomIds = ListPool<ResourceHandle<RenderTexture>>.Get();

        var screenSize = renderGraph.RtHandleSystem.ScreenSize;

        // Need to queue up all the textures first
        var mipCount = Mathf.Min(settings.MaxMip, (int)Mathf.Log(Mathf.Max(screenSize.x, screenSize.y), 2));
        for (var i = 0; i < mipCount; i++)
        {
            var width = Mathf.Max(1, screenSize.x >> (i + 1));
            var height = Mathf.Max(1, screenSize.y >> (i + 1));

            var resultId = renderGraph.GetTexture(new(width, height), GraphicsFormat.B10G11R11_UFloatPack32);
            bloomIds.Add(resultId);
        }

        // Downsample
        for (var i = 0; i < mipCount; i++)
        {
            var width = Mathf.Max(1, screenSize.x >> (i + 1));
            var height = Mathf.Max(1, screenSize.y >> (i + 1));

            var source = i > 0 ? bloomIds[i - 1] : renderGraph.GetRTHandle<CameraTarget>();

            using var pass = renderGraph.AddFullscreenRenderPass("Bloom Down", (new Float2(1.0f / width, 1.0f / height), source, settings));
            pass.Initialize(material, 0);
            pass.WriteTexture(bloomIds[i], RenderBufferLoadAction.DontCare);

            var rt = i > 0 ? bloomIds[i - 1] : renderGraph.GetRTHandle<CameraTarget>();
            pass.ReadTexture("Input", rt);
           //pass.AddRenderPassData<ViewData>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetVector("RcpResolution", data.Item1);
                pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.source));
            });
        }

        // Upsample
        for (var i = mipCount - 1; i > 0; i--)
        {
            var input = bloomIds[i];

            var width = Mathf.Max(1, screenSize.x >> i);
            var height = Mathf.Max(1, screenSize.y >> i);

            using var pass = renderGraph.AddFullscreenRenderPass("Bloom Up", (settings, new Float2(1f / width, 1f / height), input));

            pass.Initialize(material, 2);
            pass.WriteTexture(bloomIds[i - 1]);
            pass.ReadTexture("Input", input);

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("Strength", data.settings.Strength);
                pass.SetVector("RcpResolution", data.Item2);
                pass.SetVector("InputScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.input));
            });
        }

        renderGraph.SetRTHandle<CameraBloom>(bloomIds[0]);
        ListPool<ResourceHandle<RenderTexture>>.Release(bloomIds);

        renderGraph.AddProfileEndPass("Bloom");
    }
}
