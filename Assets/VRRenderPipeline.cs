using System;
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

    struct RenderPassData
    {
        public Camera camera;
        public ScriptableCullingParameters cullingParameters;
        public RenderTargetIdentifier renderTargetIdentifier;
        public bool requiresMirrorBlit;
        public Matrix4x4 worldToViewLeft, worldToViewRight, viewToClipLeft, viewToClipRight;
        public SinglePassStereoMode stereoMode;
        public uint instanceMultiplier;
        public string stereoKeyword;
        public Vector2Int size;

        public RenderPassData(Camera camera, ScriptableCullingParameters cullingParameters, RenderTargetIdentifier renderTargetIdentifier, bool requiresMirrorBlit, Matrix4x4 worldToViewLeft, Matrix4x4 worldToViewRight, Matrix4x4 viewToClipLeft, Matrix4x4 viewToClipRight, SinglePassStereoMode stereoMode, uint instanceMultiplier, string stereoKeyword, Vector2Int size)
        {
            this.camera = camera;
            this.cullingParameters = cullingParameters;
            this.renderTargetIdentifier = renderTargetIdentifier;
            this.requiresMirrorBlit = requiresMirrorBlit;
            this.worldToViewLeft = worldToViewLeft;
            this.worldToViewRight = worldToViewRight;
            this.viewToClipLeft = viewToClipLeft;
            this.viewToClipRight = viewToClipRight;
            this.stereoMode = stereoMode;
            this.instanceMultiplier = instanceMultiplier;
            this.stereoKeyword = stereoKeyword;
            this.size = size;
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
                            if (camera.cameraType == CameraType.Game)
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

                                renderPassDatas.Add(new(camera, cullingParameters, renderPass.renderTarget, true, leftEye.view, rightEye.view, leftEye.projection, rightEye.projection, stereoMode, instanceMultiplier, stereoKeyword, size));
                            }
                            else if(camera.cameraType == CameraType.SceneView)
                            {
                                if (camera.TryGetCullingParameters(out var cullingParameters))
                                    renderPassDatas.Add(new(camera, cullingParameters, BuiltinRenderTextureType.CameraTarget, false, camera.worldToCameraMatrix, camera.worldToCameraMatrix, camera.projectionMatrix, camera.projectionMatrix, SinglePassStereoMode.None, 1u, string.Empty, new(camera.pixelWidth, camera.pixelHeight)));
                            }
                        }
                    }
                }
                else
                {
                    foreach (var camera in cameras)
                    {
                        if (camera.TryGetCullingParameters(out var cullingParameters))
                            renderPassDatas.Add(new(camera, cullingParameters, BuiltinRenderTextureType.CameraTarget, false, camera.worldToCameraMatrix, camera.worldToCameraMatrix, camera.projectionMatrix, camera.projectionMatrix, SinglePassStereoMode.None, 1u, string.Empty, new(camera.pixelWidth, camera.pixelHeight)));
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

                    command.SetRenderTarget(pass.renderTargetIdentifier, 0, CubemapFace.Unknown, -1);
                    command.ClearRenderTarget(pass.camera.clearFlags != CameraClearFlags.Nothing, pass.camera.clearFlags == CameraClearFlags.SolidColor, pass.camera.backgroundColor);

                    var flip = pass.camera.cameraType == CameraType.SceneView;

                    command.SetGlobalMatrixArray("WorldToClip", new Matrix4x4[2]
                    {
                        GL.GetGPUProjectionMatrix(pass.viewToClipLeft, flip) * pass.worldToViewLeft,
                        GL.GetGPUProjectionMatrix(pass.viewToClipRight, flip) * pass.worldToViewRight,
                    });

                    if (!string.IsNullOrEmpty(pass.stereoKeyword))
                        command.EnableShaderKeyword(pass.stereoKeyword);

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(pass.instanceMultiplier);

                    command.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, pass.camera) { renderQueueRange = RenderQueueRange.opaque }));

                    if (pass.camera.clearFlags == CameraClearFlags.Skybox)
                    {
                        var sky = RenderSettings.skybox;
                        if (sky != null)
                        {
                            static Vector3 GetFrustumCorner(int index, Matrix4x4 worldToView, Matrix4x4 viewToClip, bool flip)
                            {
                                // Fullscreen triangle coordinates in clip space
                                var clipPosition = index switch
                                {
                                    0 => new Vector4(-1, -1, 1, 1),
                                    1 => new Vector4(3, -1, 1, 1),
                                    2 => new Vector4(-1, 3, 1, 1),
                                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                                };

                                if (flip)
                                    clipPosition.y = -clipPosition.y;

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

                    if (pass.instanceMultiplier != 1u)
                        command.SetInstanceMultiplier(1u);

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