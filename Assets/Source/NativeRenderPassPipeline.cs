using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.XR;

public class NativeRenderPassPipeline : CustomRenderPipelineBase<NativeRenderPassPipelineAsset>
{
    private readonly Material tonemapMaterial;

#if UNITY_EDITOR
    private readonly Material xrMirrorViewMaterial;
#endif

    public NativeRenderPassPipeline(NativeRenderPassPipelineAsset settings) : base(settings)
    {
        tonemapMaterial = new Material(Shader.Find("Hidden/SimpleTonemap")) { hideFlags = HideFlags.HideAndDontSave };

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

    protected override void CollectViewRenderData(List<Camera> cameras, ScriptableRenderContext context, List<ViewRenderData> viewRenderDatas)
    {
        using (ListPool<XRDisplaySubsystem>.Get(out var displayList))
        {
            void AddGenericCameraRenderPass(Camera camera)
            {
                if (!camera.TryGetCullingParameters(out var cullingParameters))
                    return;

                var renderTarget = (RenderTargetIdentifier)(camera.targetTexture == null ? BuiltinRenderTextureType.CameraTarget : camera.targetTexture);
                var size = new Int2(camera.pixelWidth, camera.pixelHeight);
                viewRenderDatas.Add(new(size, camera.nearClipPlane, camera.farClipPlane, camera.TanHalfFov(), camera.transform.WorldRigidTransform(), camera, context, cullingParameters, renderTarget, VRTextureUsage.None, SinglePassStereoMode.None));
                renderGraph.RtHandleSystem.SetScreenSize(size.x, size.y);
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
                            display.zNear = Math.Min(display.zNear, camera.nearClipPlane);
                            display.zFar = Math.Max(display.zFar, camera.farClipPlane);

                            renderPass.GetRenderParameter(camera, 0, out var leftEyeParams);
                            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, leftEyeParams.view);
                            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, leftEyeParams.projection);

                            renderPass.GetRenderParameter(camera, 1, out var rightEyeParams);
                            camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, rightEyeParams.view);
                            camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, rightEyeParams.projection);

                            renderGraph.RtHandleSystem.SetScreenSize(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);
                            renderGraph.SetResource(new XRDisplaySubsystemData(display));

                            display.GetCullingParameters(camera, renderPass.cullingPassIndex, out var cullingParameters);

                            var size = new Int2(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);
                            var transform = camera.transform.WorldRigidTransform();
                            var vrUsage = renderPass.renderTargetDesc.vrUsage;
                            var stereoMode = SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing;
                            viewRenderDatas.Add(new(size, camera.nearClipPlane, camera.farClipPlane, camera.TanHalfFov(), transform, camera, context, cullingParameters, renderPass.renderTarget, vrUsage, stereoMode));
                            renderGraph.RtHandleSystem.SetScreenSize(size.x, size.y);
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
    }

    protected override List<FrameRenderFeature> InitializePerFrameRenderFeatures() => new();

    protected override List<ViewRenderFeature> InitializePerCameraRenderFeatures() => new()
    {
        new GenericViewRenderFeature(renderGraph, viewRenderData =>
        {
            using var pass = renderGraph.AddGenericRenderPass("Setup Camera");

            var cullingParameters = viewRenderData.cullingParameters;
            var cullingResults = viewRenderData.context.Cull(ref cullingParameters);
            renderGraph.SetResource(new CullingResultsData(cullingResults));

            pass.SetRenderFunction((command, pass) =>
            {
                var camera = viewRenderData.camera;
                var flip = camera.cameraType == CameraType.SceneView;

                command.SetGlobalMatrixArray("WorldToClip", new Matrix4x4[2]
                {
                    camera.GetStereoGpuViewProjectionMatrix(Camera.StereoscopicEye.Left, true),
                    camera.GetStereoGpuViewProjectionMatrix(Camera.StereoscopicEye.Right, true),
                });

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
                        //clipPosition.y = -clipPosition.y;
                    }

                    // Transform from clip to view space
                    // Transform from view to camera-relative world space (Since we only want the vector from the view to the corner
                    var viewToWorld = worldToView.inverse;
                    viewToWorld.SetColumn(3, new Vector4(0, 0, 0, 1));

                    // Reverse the perspective projection
                    var cameraRelativeWorldPos = viewToWorld * (viewToClip.inverse * clipPosition);
                    return (Vector3)cameraRelativeWorldPos / cameraRelativeWorldPos.w;
                }

                var worldToViewLeft = camera.stereoEnabled ? camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left) : camera.worldToCameraMatrix;
                var worldToViewRight = camera.stereoEnabled ? camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right) : camera.worldToCameraMatrix;
                var viewToClipLeft = camera.stereoEnabled ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : camera.projectionMatrix;
                var viewToClipRight = camera.stereoEnabled ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) : camera.projectionMatrix;

                command.SetGlobalVectorArray("FrustumCorners", new Vector4[6]
                {
                    GetFrustumCorner(0, worldToViewLeft, viewToClipLeft, flip),
                    GetFrustumCorner(1, worldToViewLeft, viewToClipLeft, flip),
                    GetFrustumCorner(2, worldToViewLeft, viewToClipLeft, flip),
                    GetFrustumCorner(0, worldToViewRight, viewToClipRight, flip),
                    GetFrustumCorner(1, worldToViewRight, viewToClipRight, flip),
                    GetFrustumCorner(2, worldToViewRight, viewToClipRight, flip),
                });
            });
        }),

        new GenericViewRenderFeature(renderGraph, viewRenderData =>
        {
            var volumeDepth = viewRenderData.vrTextureUsage == VRTextureUsage.None ? 1 : 2; // TODO: Move into viewRenderData?
            var depth = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.D32_SFloat_S8_UInt, volumeDepth, volumeDepth > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D, isScreenTexture: true, clearFlags: RTClearFlags.DepthStencil);
            var color = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, volumeDepth, volumeDepth > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D, isScreenTexture: true);

            renderGraph.SetRTHandle<CameraDepth>(depth);
            renderGraph.SetRTHandle<CameraTarget>(color);

            using var pass = renderGraph.AddObjectRenderPass("Render Objects");

            var camera = viewRenderData.camera;
            var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

            pass.Initialize("SRPDefaultUnlit", viewRenderData.context, cullingResults, camera, RenderQueueRange.opaque );

            pass.WriteDepth(depth, loadAction: RenderBufferLoadAction.DontCare, storeAction: RenderBufferStoreAction.Store);
            pass.WriteTexture(color, loadAction: RenderBufferLoadAction.DontCare, storeAction: RenderBufferStoreAction.Store);
            
            //pass.SetRenderFunction((command, pass) =>
            //{
            //    var attachments = new NativeArray<AttachmentDescriptor>(3, Allocator.Temp);
            //    {
            //        attachments[0] = new(GraphicsFormat.D32_SFloat_S8_UInt) { loadAction = RenderBufferLoadAction.Clear, storeAction = RenderBufferStoreAction.DontCare };
            //        attachments[1] = new(GraphicsFormat.B10G11R11_UFloatPack32){ loadAction = RenderBufferLoadAction.DontCare, storeAction = RenderBufferStoreAction.DontCare };
            //        attachments[2] = new(GraphicsFormat.R8G8B8A8_SRGB) { storeAction = RenderBufferStoreAction.Store, loadStoreTarget = new RenderTargetIdentifier(viewRenderData.target, 0, CubemapFace.Unknown, -1) };
            //    }

            //    var subPasses = new NativeArray<SubPassDescriptor>(2, Allocator.Temp);
            //    {
            //        var colorOutputs0 = new AttachmentIndexArray(1);
            //        colorOutputs0[0] = 1;
            //        subPasses[0] = new SubPassDescriptor() { colorOutputs = colorOutputs0 };

            //        var inputs1 = new AttachmentIndexArray(1);
            //        inputs1[0] = 1;

            //        var colorOutputs1 = new AttachmentIndexArray(1);
            //        colorOutputs1[0] = 2;
            //        subPasses[1] = new SubPassDescriptor() { colorOutputs = colorOutputs1, inputs = inputs1 };
            //    }

            //    command.BeginRenderPass(viewRenderData.viewSize.x, viewRenderData.viewSize.y, volumeDepth, 1, attachments, 0, subPasses);

            //    if (viewRenderData.stereoMode != SinglePassStereoMode.None)
            //        command.EnableShaderKeyword(viewRenderData.stereoMode == SinglePassStereoMode.Multiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");

            //    if (viewRenderData.stereoMode == SinglePassStereoMode.Instancing)
            //        command.SetInstanceMultiplier(2u);

            //    command.DrawRendererList(viewRenderData.context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, viewRenderData.camera) { renderQueueRange = RenderQueueRange.opaque }));

            //    if (viewRenderData.stereoMode == SinglePassStereoMode.Instancing)
            //        command.SetInstanceMultiplier(1u);
            //});
        }),

        new GenericViewRenderFeature(renderGraph, viewRenderData =>
        {
            if (viewRenderData.camera.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null)
                return;

            var volumeDepth = viewRenderData.vrTextureUsage == VRTextureUsage.None ? 1 : 2; // TODO: Move into viewRenderData?
            using var pass = renderGraph.AddFullscreenRenderPass("Render Sky");
            pass.Initialize(RenderSettings.skybox, isStereo: volumeDepth > 1);

            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
            pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
        }),

        new GenericViewRenderFeature(renderGraph, viewRenderData =>
        {
            using var pass = renderGraph.AddGenericRenderPass("Tonemap");

            pass.ReadTexture("_UnityFBInput0", renderGraph.GetRTHandle<CameraTarget>());

            var volumeDepth = viewRenderData.vrTextureUsage == VRTextureUsage.None ? 1 : 2;
            var instanceMultiplier = viewRenderData.stereoMode == SinglePassStereoMode.Instancing ? 2u : 1u;

            pass.SetRenderFunction((command, pass) =>
            {
                command.SetRenderTarget(viewRenderData.target, 0, CubemapFace.Unknown, -1);

                if (volumeDepth > 1)
                    command.EnableShaderKeyword("USE_TEXTURE_ARRAY");

                command.SetGlobalTexture("_UnityFBInput0", pass.GetRenderTexture(renderGraph.GetRTHandle<CameraTarget>()));
                command.SetGlobalFloat("IsSceneView", viewRenderData.camera.cameraType == CameraType.SceneView ? 1 : 0);
                command.SetGlobalVector("Resolution", (Float4)viewRenderData.viewSize);

                if (viewRenderData.stereoMode != SinglePassStereoMode.None)
                    command.EnableShaderKeyword(viewRenderData.stereoMode == SinglePassStereoMode.Multiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");

                command.DrawProcedural(Matrix4x4.identity, tonemapMaterial, 0, MeshTopology.Triangles, (int)(3u * instanceMultiplier));

                if (volumeDepth > 1)
                    command.DisableShaderKeyword("USE_TEXTURE_ARRAY");

                if (viewRenderData.stereoMode != SinglePassStereoMode.None)
                    command.DisableShaderKeyword(viewRenderData.stereoMode == SinglePassStereoMode.Multiview ? "STEREO_MULTIVIEW_ON" : "STEREO_INSTANCING_ON");

                //command.EndRenderPass();
            });
        }),

#if UNITY_EDITOR
        new GenericViewRenderFeature(renderGraph, viewRenderData =>
        {
            if (viewRenderData.camera.cameraType != CameraType.Game || viewRenderData.stereoMode == SinglePassStereoMode.None)
                return;

            using var pass = renderGraph.AddGenericRenderPass("XR Mirror View");
            pass.SetRenderFunction((command, pass) =>
            {
                viewRenderData.context.ExecuteCommandBuffer(command);
                command.Clear();

                // Blit to screen, editor only
                command.SetGlobalTexture("Input", viewRenderData.target);
                command.SetGlobalFloat("RenderMode", (float)XRSettings.gameViewRenderMode);
                command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget, 0, CubemapFace.Unknown, -1);
                command.DrawProcedural(Matrix4x4.identity, xrMirrorViewMaterial, 0, MeshTopology.Triangles, 3);
            });
        })
#endif
    };
}