using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.XR;

public class VRRenderPipeline : RenderPipeline
{
    public VRRenderPipelineAsset Settings;


    private readonly CommandBuffer command;

    public VRRenderPipeline(VRRenderPipelineAsset settings)
    {
        Settings = settings;
        command = new() { name = "Render" };
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            if (!camera.TryGetCullingParameters(true, out var cullingParameters))
                return;

            var cullingResults = context.Cull(ref cullingParameters);
            context.SetupCameraProperties(camera, camera.stereoEnabled);
            command.ClearRenderTarget(true, false, default);

            if (camera.stereoEnabled)
            {
                if(SystemInfo.supportsMultiview)
                    command.EnableShaderKeyword("STEREO_MULTIVIEW_ON");
                else
                {
                    command.EnableShaderKeyword("STEREO_INSTANCING_ON");
                    command.SetInstanceMultiplier(2u);
                }

                context.StartMultiEye(camera);
            }

            command.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, camera) { renderQueueRange = RenderQueueRange.opaque }));
            command.DrawRendererList(context.CreateSkyboxRendererList(camera));

            if (camera.stereoEnabled)
            {
                if (SystemInfo.supportsMultiview)
                    command.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
                else
                {
                    command.DisableShaderKeyword("STEREO_INSTANCING_ON");
                    command.SetInstanceMultiplier(1u);
                }
            }

            context.ExecuteCommandBuffer(command);
            command.Clear();

            if (camera.stereoEnabled)
            {
                context.StopMultiEye(camera);
                context.StereoEndRender(camera);
            }
        }

        context.Submit();
    }
}