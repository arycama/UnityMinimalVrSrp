using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.XR;

public class VRRenderPipeline : RenderPipeline
{
#if UNITY_EDITOR
    private readonly Material xrMirrorViewMaterial;
#endif

    public VRRenderPipeline()
    {
#if UNITY_EDITOR
        xrMirrorViewMaterial = new Material(Shader.Find("Hidden/XRMirrorView")) { hideFlags = HideFlags.HideAndDontSave };
#endif

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

                    using (GenericPool<CommandBuffer>.Get(out var command))
                    {
                        command.Clear();

                        foreach (var camera in cameras)
                        {
                            display.zNear = Mathf.Min(display.zNear, camera.nearClipPlane);
                            display.zFar = Mathf.Max(display.zFar, camera.farClipPlane);

                            renderPass.GetRenderParameter(camera, 0, out var leftEyeParams);
                            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, leftEyeParams.view);
                            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, leftEyeParams.projection);

                            renderPass.GetRenderParameter(camera, 1, out var rightEyeParams);
                            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, rightEyeParams.view);
                            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, rightEyeParams.projection);

                            display.GetCullingParameters(camera, renderPass.cullingPassIndex, out var cullingParameters);

                            var size = new Vector2Int(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);
                            var stereoMode = camera.stereoEnabled ? (SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing) : SinglePassStereoMode.None;
                            var instanceMultiplier = camera.stereoEnabled && !SystemInfo.supportsMultiview ? 2u : 1u;
                            var stereoKeyword = camera.stereoEnabled ? (SystemInfo.supportsMultiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON") : string.Empty;

                            var cullingResults = context.Cull(ref cullingParameters);
                            context.SetupCameraProperties(camera, camera.stereoEnabled);
                            command.SetRenderTarget(renderPass.renderTarget, 0, CubemapFace.Unknown, -1);
                            command.ClearRenderTarget(true, true, camera.backgroundColor);

                            command.SetGlobalMatrixArray("WorldToClip", new Matrix4x4[2]
                            {
                                GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), false) * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left),
                                GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), false) * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right),
                            });

                            if (!string.IsNullOrEmpty(stereoKeyword))
                                command.EnableShaderKeyword(stereoKeyword);

                            if (instanceMultiplier != 1u)
                                command.SetInstanceMultiplier(instanceMultiplier);

                            command.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, camera) { renderQueueRange = RenderQueueRange.opaque }));

                            var sky = RenderSettings.skybox;
                            if(sky != null)
                            {
                                static Vector3 GetFrustumCorner(int index, Matrix4x4 worldToView, Matrix4x4 viewToClip)
                                {
                                    // Fullscreen triangle coordinates in clip space
                                    var clipPosition = index switch
                                    {
                                        0 => new Vector4(-1, -1, 1, 1),
                                        1 => new Vector4(3, -1, 1, 1),
                                        2 => new Vector4(-1, 3, 1, 1),
                                        _ => throw new ArgumentOutOfRangeException(nameof(index)),
                                    };

                                    // Transform from clip to view space
                                    var clipToView = viewToClip.inverse;
                                    var viewPos = clipToView * clipPosition;

                                    // Transform from view to camera-relative world space (Since we only want the vector from the view to the corner
                                    var viewToWorld = worldToView.inverse;
                                    viewToWorld.SetColumn(3, new Vector4(0, 0, 0, 1));

                                    // Reverse the perspective projection
                                    var cameraRelativeWorldPos = viewToWorld * viewPos;
                                    return (Vector3)cameraRelativeWorldPos / cameraRelativeWorldPos.w;
                                }

                                command.SetGlobalVectorArray("FrustumCorners", new Vector4[6]
                                {
                                    GetFrustumCorner(0, leftEyeParams.view, leftEyeParams.projection),
                                    GetFrustumCorner(1, leftEyeParams.view, leftEyeParams.projection),
                                    GetFrustumCorner(2, leftEyeParams.view, leftEyeParams.projection),
                                    GetFrustumCorner(0, rightEyeParams.view, rightEyeParams.projection),
                                    GetFrustumCorner(1, rightEyeParams.view, rightEyeParams.projection),
                                    GetFrustumCorner(2, rightEyeParams.view, rightEyeParams.projection),
                                });

                                command.DrawProcedural(Matrix4x4.identity, sky, 0, MeshTopology.Triangles, (int)(3u * instanceMultiplier));
                            }

                            if (!string.IsNullOrEmpty(stereoKeyword))
                                command.DisableShaderKeyword(stereoKeyword);

                            if (instanceMultiplier != 1u)
                                command.SetInstanceMultiplier(1u);
                        }
#if UNITY_EDITOR
                        // Blit to screen, editor only
                        command.SetGlobalTexture("Input", renderPass.renderTarget);
                        command.SetGlobalFloat("RenderMode", (float)XRSettings.gameViewRenderMode);
                        command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
                        command.DrawProcedural(Matrix4x4.identity, xrMirrorViewMaterial, 0, MeshTopology.Triangles, 3);
#endif

                        context.ExecuteCommandBuffer(command);
                    }

                    context.Submit();
                }
            }
        }
    }
}