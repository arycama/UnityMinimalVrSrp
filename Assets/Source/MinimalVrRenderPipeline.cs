using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.XR;

public class MinimalVrRenderPipeline : RenderPipeline
{
#if UNITY_EDITOR
    private readonly Material xrMirrorViewMaterial;
#endif

    private readonly Material tonemapMaterial;

    public MinimalVrRenderPipeline()
    {
#if UNITY_EDITOR
        xrMirrorViewMaterial = new Material(Shader.Find("Hidden/XRMirrorView")) { hideFlags = HideFlags.HideAndDontSave };
#endif

        tonemapMaterial = new Material(Shader.Find("Hidden/Tonemap")) { hideFlags = HideFlags.HideAndDontSave };

        using (ListPool<XRDisplaySubsystem>.Get(out var displayList))
        {
            SubsystemManager.GetSubsystems(displayList);
            if (displayList.Count > 0)
            {
                var display = displayList[0];
                display.disableLegacyRenderer = true;
                display.sRGB = true;
                display.textureLayout = XRDisplaySubsystem.TextureLayout.Texture2DArray;
                display.SetMSAALevel(1);
            }
        }
    }

    protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        using (ListPool<RenderPassData>.Get(out var renderPassDatas))
        {
            using (ListPool<XRDisplaySubsystem>.Get(out var displayList))
            {
                SubsystemManager.GetSubsystems(displayList);
                if (displayList.Count > 0)
                {
                    var display = displayList[0];
                    var passCount = display.GetRenderPassCount();
                    if (passCount > 0)
                    {
                        display.GetRenderPass(0, out var renderPass);

                        foreach (var camera in cameras)
                        {

                            if (camera.cameraType == CameraType.SceneView)
                            {
                                if (camera.TryGetCullingParameters(out var cullingParameters))
                                {
                                    var renderTarget = (RenderTargetIdentifier)(camera.targetTexture == null ? BuiltinRenderTextureType.CameraTarget : camera.targetTexture);
                                    var size = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                                    renderPassDatas.Add(new(camera, cullingParameters, renderTarget, false, camera.worldToCameraMatrix, camera.worldToCameraMatrix, camera.projectionMatrix, camera.projectionMatrix, SinglePassStereoMode.None, 1u, string.Empty, size, VRTextureUsage.None, GraphicsFormat.R8G8B8A8_SRGB));
                                }
                            }
                            else if (camera.cameraType == CameraType.Game)
                            {
                                display.zNear = Mathf.Min(display.zNear, camera.nearClipPlane);
                                display.zFar = Mathf.Max(display.zFar, camera.farClipPlane);
                                display.GetCullingParameters(camera, renderPass.cullingPassIndex, out var cullingParameters);

                                renderPass.GetRenderParameter(camera, 0, out var leftEye);
                                renderPass.GetRenderParameter(camera, 1, out var rightEye);

                                var stereoMode = camera.stereoEnabled ? (SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing) : SinglePassStereoMode.None;
                                var instanceMultiplier = camera.stereoEnabled && !SystemInfo.supportsMultiview ? 2u : 1u;
                                var stereoKeyword = camera.stereoEnabled ? (SystemInfo.supportsMultiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON") : string.Empty;
                                var size = new Vector2Int(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);

                                renderPassDatas.Add(new(camera, cullingParameters, renderPass.renderTarget, true, leftEye.view, rightEye.view, leftEye.projection, rightEye.projection, stereoMode, instanceMultiplier, stereoKeyword, size, renderPass.renderTargetDesc.vrUsage, GraphicsFormat.R8G8B8A8_SRGB));
                            }
                        }
                    }
                }
                else
                {
                    foreach (var camera in cameras)
                    {
                        if (camera.TryGetCullingParameters(out var cullingParameters))
                        {
                            var renderTarget = (RenderTargetIdentifier)(camera.targetTexture == null ? BuiltinRenderTextureType.CameraTarget : camera.targetTexture);
                            var size = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                            renderPassDatas.Add(new(camera, cullingParameters, renderTarget, false, camera.worldToCameraMatrix, camera.worldToCameraMatrix, camera.projectionMatrix, camera.projectionMatrix, SinglePassStereoMode.None, 1u, string.Empty, size, VRTextureUsage.None, GraphicsFormat.R8G8B8A8_SRGB));

                        }
                    }
                }
            }

            using (GenericPool<CommandBuffer>.Get(out var command))
            {
                command.Clear();

                foreach (var pass in renderPassDatas)
                {
                    var cullingParameters = pass.cullingParameters;
                    var cullingResults = context.Cull(ref cullingParameters);

                    var cameraTarget = Shader.PropertyToID("CameraTarget");
                    var cameraTargetDesc = new RenderTextureDescriptor(pass.size.x, pass.size.y, UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32, 32) { dimension = TextureDimension.Tex2DArray, volumeDepth = 2, vrUsage = pass.vrUsage };
                    command.GetTemporaryRT(cameraTarget, cameraTargetDesc);

                    command.SetRenderTarget(cameraTarget, 0, CubemapFace.Unknown, -1);
                    command.ClearRenderTarget(pass.camera.clearFlags != CameraClearFlags.Nothing, pass.camera.clearFlags == CameraClearFlags.SolidColor, pass.camera.backgroundColor);

                    command.SetGlobalMatrixArray("WorldToClip", new Matrix4x4[2]
                    {
                        GL.GetGPUProjectionMatrix(pass.viewToClipLeft, true) * pass.worldToViewLeft,
                        GL.GetGPUProjectionMatrix(pass.viewToClipRight, true) * pass.worldToViewRight,
                    });

                    if (!string.IsNullOrEmpty(pass.stereoKeyword))
                        command.EnableShaderKeyword(pass.stereoKeyword);

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(pass.instanceMultiplier);

                    command.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, pass.camera) { renderQueueRange = RenderQueueRange.opaque }));

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(1u);

                    if (pass.camera.clearFlags == CameraClearFlags.Skybox)
                    {
                        var sky = RenderSettings.skybox;
                        if (sky != null)
                        {
                            static Vector3 GetFrustumCorner(int index, Matrix4x4 worldToView, Matrix4x4 viewToClip)
                            {
                                // Fullscreen triangle coordinates in clip space
                                var clipPosition = index switch
                                {
                                    0 => new Vector4(-1, 1, 1, 1),
                                    1 => new Vector4(3, 1, 1, 1),
                                    2 => new Vector4(-1, -3, 1, 1),
                                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                                };

                                // Transform from clip to view space
                                // Transform from view to camera-relative world space (Since we only want the vector from the view to the corner
                                var viewToWorld = worldToView.inverse;
                                viewToWorld.SetColumn(3, new Vector4(0, 0, 0, 1));

                                // Reverse the perspective projection
                                var cameraRelativeWorldPos = viewToWorld * (viewToClip.inverse * clipPosition);
                                return (Vector3)cameraRelativeWorldPos / cameraRelativeWorldPos.w;
                            }

                            command.SetGlobalVectorArray("FrustumCorners", new Vector4[6]
                            {
                                GetFrustumCorner(0, pass.worldToViewLeft, pass.viewToClipLeft),
                                GetFrustumCorner(1, pass.worldToViewLeft, pass.viewToClipLeft),
                                GetFrustumCorner(2, pass.worldToViewLeft, pass.viewToClipLeft),
                                GetFrustumCorner(0, pass.worldToViewRight, pass.viewToClipRight),
                                GetFrustumCorner(1, pass.worldToViewRight, pass.viewToClipRight),
                                GetFrustumCorner(2, pass.worldToViewRight, pass.viewToClipRight),
                            });

                            command.DrawProcedural(Matrix4x4.identity, sky, 0, MeshTopology.Triangles, (int)(3u * pass.instanceMultiplier));
                        }
                    }

                    // Tonemap and output blit
                    command.SetRenderTarget(pass.renderTargetIdentifier, 0, CubemapFace.Unknown, -1);
                    command.SetGlobalTexture("_UnityFBInput0", cameraTarget);
                    command.SetGlobalFloat("Flip", pass.camera.cameraType == CameraType.SceneView ? 1f : 0f);
                    command.DrawProcedural(Matrix4x4.identity, tonemapMaterial, 0, MeshTopology.Triangles, (int)(3u * pass.instanceMultiplier));

                    if (!string.IsNullOrEmpty(pass.stereoKeyword))
                        command.DisableShaderKeyword(pass.stereoKeyword);

#if UNITY_EDITOR
                    if (pass.requiresMirrorBlit)
                    {
                        // Blit to screen, editor only
                        command.SetGlobalTexture("Input", pass.renderTargetIdentifier);
                        command.SetGlobalFloat("RenderMode", (float)XRSettings.gameViewRenderMode);
                        command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
                        command.DrawProcedural(Matrix4x4.identity, xrMirrorViewMaterial, 0, MeshTopology.Triangles, 3);
                    }
#endif
                }

                context.ExecuteCommandBuffer(command);
                context.Submit();
            }
        }
    }
}