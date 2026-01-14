using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.XR;

public class NativeRenderPassPipeline : RenderPipeline
{
    private readonly NativeRenderPassPipelineAsset settings;
    private readonly Material tonemapMaterial;

    public NativeRenderPassPipeline(NativeRenderPassPipelineAsset settings)
    {
        this.settings = settings;
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
                void AddGenericCameraRenderPass(Camera camera)
                {
                    if (!camera.TryGetCullingParameters(out var cullingParameters))
                        return;

                    var renderTarget = (RenderTargetIdentifier)(camera.targetTexture == null ? BuiltinRenderTextureType.CameraTarget : camera.targetTexture);
                    var size = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                    renderPassDatas.Add(new(camera, cullingParameters, renderTarget, false, camera.worldToCameraMatrix, camera.worldToCameraMatrix, camera.projectionMatrix, camera.projectionMatrix, SinglePassStereoMode.None, 1u, string.Empty, size, VRTextureUsage.None, GraphicsFormat.R8G8B8A8_SRGB));
                }

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
                                AddGenericCameraRenderPass(camera);
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
                        AddGenericCameraRenderPass(camera);
                    }
                }
            }

            using (GenericPool<CommandBuffer>.Get(out var command))
            {
                command.Clear();
                command.name = "Render Camera";

                foreach (var pass in renderPassDatas)
                {
                    var cullingParameters = pass.cullingParameters;
                    var cullingResults = context.Cull(ref cullingParameters);
                    var camera = pass.camera;

                    context.SetupCameraProperties(camera, true);

                    var flip = pass.camera.cameraType == CameraType.SceneView;

                    command.SetGlobalMatrixArray("WorldToClip", new Matrix4x4[2]
                    {
                        GL.GetGPUProjectionMatrix(pass.viewToClipLeft, flip) * pass.worldToViewLeft,
                        GL.GetGPUProjectionMatrix(pass.viewToClipRight, flip) * pass.worldToViewRight,
                    });

                    var depthTemp = Shader.PropertyToID("DepthTemp");
                    var depthTempDesc = new RenderTextureDescriptor(pass.size.x, pass.size.y, RenderTextureFormat.Depth, 32) { dimension = TextureDimension.Tex2DArray, volumeDepth = 2 };
                    command.GetTemporaryRT(depthTemp, depthTempDesc);

                    var colorTemp = Shader.PropertyToID("ColorTemp");
                    var colorTempDesc = new RenderTextureDescriptor(pass.size.x, pass.size.y, RenderTextureFormat.RGB111110Float) { dimension = TextureDimension.Tex2DArray, volumeDepth = 2 };
                    command.GetTemporaryRT(colorTemp, colorTempDesc);

                    var attachments = new NativeArray<AttachmentDescriptor>(2, Allocator.Temp);
                    {
                        attachments[0] = new(RenderTextureFormat.Depth) { loadAction = RenderBufferLoadAction.Clear, loadStoreTarget = new RenderTargetIdentifier(depthTemp, 0, CubemapFace.Unknown, -1) };
                        attachments[1] = new(RenderTextureFormat.RGB111110Float) { storeAction = RenderBufferStoreAction.Store, loadStoreTarget = new RenderTargetIdentifier(colorTemp, 0, CubemapFace.Unknown, -1) };
                    }

                    var subPasses = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
                    {
                        var colorOutputs0 = new AttachmentIndexArray(1);
                        colorOutputs0[0] = 1;
                        subPasses[0] = new SubPassDescriptor() { colorOutputs = colorOutputs0 };
                    }

                    command.BeginRenderPass(pass.size.x, pass.size.y, 2, 1, attachments, 0, subPasses);
               
                    if (!string.IsNullOrEmpty(pass.stereoKeyword))
                        command.EnableShaderKeyword(pass.stereoKeyword);

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(pass.instanceMultiplier);

                    command.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, camera) { renderQueueRange = RenderQueueRange.opaque }));

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(1u);

                    if (camera.clearFlags == CameraClearFlags.Skybox)
                    {
                        var sky = RenderSettings.skybox;
                        if (sky != null)
                        {
                            static Vector3 GetFrustumCorner(int index, Matrix4x4 worldToView, Matrix4x4 viewToClip, bool flip)
                            {
                                // Fullscreen triangle coordinates in clip space
                                var clipPosition = index switch
                                {
                                    0 => new Vector4(-1, 1, 1, 1),
                                    1 => new Vector4(3, 1, 1, 1),
                                    2 => new Vector4(-1, -3, 1, 1),
                                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                                };

                                if(!flip)
                                {
                                    clipPosition.y = -clipPosition.y;
                                }

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
                                GetFrustumCorner(0, pass.worldToViewLeft, pass.viewToClipLeft, flip),
                                GetFrustumCorner(1, pass.worldToViewLeft, pass.viewToClipLeft, flip),
                                GetFrustumCorner(2, pass.worldToViewLeft, pass.viewToClipLeft, flip),
                                GetFrustumCorner(0, pass.worldToViewRight, pass.viewToClipRight, flip),
                                GetFrustumCorner(1, pass.worldToViewRight, pass.viewToClipRight, flip),
                                GetFrustumCorner(2, pass.worldToViewRight, pass.viewToClipRight, flip),
                            });

                            command.DrawProcedural(Matrix4x4.identity, sky, 0, MeshTopology.Triangles, (int)(3u * pass.instanceMultiplier));
                        }
                    }

                    if (!string.IsNullOrEmpty(pass.stereoKeyword))
                        command.DisableShaderKeyword(pass.stereoKeyword);

                    command.EndRenderPass();
                }

                context.ExecuteCommandBuffer(command);

                if (context.SubmitForRenderPassValidation())
                    context.Submit();
                else
                    Debug.LogError("Render Pass Validation Failed");
            }
        }
    }
}
