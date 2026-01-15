using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.Pool;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PhysicalVR
{
    public class VrRenderPipeline : CustomRenderPipelineBase<VrRenderPipelineAsset>
    {
        private double previousTime;

#if UNITY_EDITOR
        private readonly Material xrMirrorViewMaterial;
#endif

        public VrRenderPipeline(VrRenderPipelineAsset asset) : base(asset)
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

        protected override List<FrameRenderFeature> InitializePerFrameRenderFeatures() => new()
        {
            new PrecomputeDfg(renderGraph),
            new GenericFrameRenderFeature(renderGraph, "Render Frame", (context) =>
            {
                #if UNITY_EDITOR
                    var time = EditorApplication.isPlaying && !EditorApplication.isPaused ? Time.unscaledTimeAsDouble : EditorApplication.timeSinceStartup;
                #else
                    var time = Time.unscaledTimeAsDouble;
                #endif

                var deltaTime = time - previousTime;
                previousTime = time;
                renderGraph.SetResource(new TimeData(time, previousTime));
            })
        };

        protected override void CollectViewRenderData(List<Camera> cameras, ScriptableRenderContext context, List<ViewRenderData> viewRenderDatas)
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

                            renderGraph.SetResource(new ViewInfo(new(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight)));

                            display.GetCullingParameters(camera, renderPass.cullingPassIndex, out var cullingParameters);

                            var size = new Int2(renderPass.renderTargetScaledWidth, renderPass.renderTargetScaledHeight);
                            var transform = camera.transform.WorldRigidTransform();
                            var vrUsage = renderPass.renderTargetDesc.vrUsage;
                            var stereoMode = SystemInfo.supportsMultiview ? SinglePassStereoMode.Multiview : SinglePassStereoMode.Instancing;
                            viewRenderDatas.Add(new(size, camera.nearClipPlane, camera.farClipPlane, camera.TanHalfFov(), transform, camera, context, cullingParameters, renderPass.renderTarget, vrUsage, stereoMode));
                        }
                    }
                    else
                    {
                        renderGraph.ClearResource<XRDisplaySubsystemData>();
                    }
                }
                else
                {
                    renderGraph.ClearResource<XRDisplaySubsystemData>();

                    foreach (var camera in cameras)
                    {
                        var width = camera.scaledPixelWidth;
                        var height = camera.scaledPixelHeight;

                        renderGraph.RtHandleSystem.SetScreenSize(width, height);
                        renderGraph.SetResource(new ViewInfo(new(width, height)));

                        if (!camera.TryGetCullingParameters(out var cullingParameters))
                            continue;

                        // Somewhat hacky.. but this is kind of required to deal with some unity hacks so meh
                        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                        viewRenderDatas.Add(new(camera.ViewSize(), camera.nearClipPlane, camera.farClipPlane, camera.TanHalfFov(), camera.transform.WorldRigidTransform(), camera, context, cullingParameters, BuiltinRenderTextureType.CameraTarget, VRTextureUsage.None, SinglePassStereoMode.None));
                    }
                }
            }
        }

        protected override List<ViewRenderFeature> InitializePerCameraRenderFeatures() => new()
        {
            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                // This is required to make the quest link output work correctly, I think. (Might not be neccessary now that we're using the xr display subsystem stuff)
                //context.SetupCameraProperties(camera, camera.stereoEnabled);

                BeginCameraRendering(viewRenderData.context, viewRenderData.camera);

                var cullingParameters = viewRenderData.cullingParameters;
                var cullingResults = viewRenderData.context.Cull(ref cullingParameters);
                renderGraph.SetResource(new CullingResultsData(cullingResults));
            }),

            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                using var pass = renderGraph.AddGenericRenderPass("Setup Lighting");

                var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
                var sunDirection = Float3.Up;
                var sunColor = Color.white * Math.Pi;

                foreach (var light in cullingResults.visibleLights)
                {
                    if (light.lightType != LightType.Directional)
                        continue;

                    sunDirection = (Vector3)(-light.localToWorldMatrix.GetColumn(2));
                    sunColor = light.finalColor;
                    break;
                }

                var jitter = Float2.Zero;
                var timeData = renderGraph.GetResource<TimeData>();

                var fogDensity = 0f;// CoreUtils.IsSceneViewFogEnabled(viewRenderData.camera) ? RenderSettings.fogDensity : 0f;

                var viewDataBuffer = renderGraph.SetConstantBuffer((
                    viewRenderData.camera.GetStereoGpuViewProjectionMatrix(Camera.StereoscopicEye.Left),
                    viewRenderData.camera.GetStereoGpuViewProjectionMatrix(Camera.StereoscopicEye.Right),
                    new Float4(viewRenderData.camera.GetViewPosition(Camera.StereoscopicEye.Left), 0f),
                    new Float4(viewRenderData.camera.GetViewPosition(Camera.StereoscopicEye.Right), 0f),
                    sunDirection,
                    fogDensity,
                    sunColor.LinearFloat3(),
                    (float)timeData.time,
                    RenderSettings.fogColor.LinearFloat3(),
                    (float)renderGraph.FrameIndex,
                    new Float4(viewRenderData.camera.GetFrustumCorner(0, Camera.StereoscopicEye.Left), 0f),
                    new Float4(viewRenderData.camera.GetFrustumCorner(1, Camera.StereoscopicEye.Left), 0f),
                    new Float4(viewRenderData.camera.GetFrustumCorner(2, Camera.StereoscopicEye.Left), 0f),
                    new Float4(viewRenderData.camera.GetFrustumCorner(0, Camera.StereoscopicEye.Right), 0f),
                    new Float4(viewRenderData.camera.GetFrustumCorner(1, Camera.StereoscopicEye.Right), 0f),
                    new Float4(viewRenderData.camera.GetFrustumCorner(2, Camera.StereoscopicEye.Right), 0f),
                    new Float4((Float2)viewRenderData.viewSize, viewRenderData.tanHalfFov)
                ));

                renderGraph.SetResource<ViewData>(new(viewDataBuffer));
            }),

            //new SkyProbe(renderGraph, asset.EnvironmentLighting),
            //new EnvironmentConvolve(renderGraph, asset.EnvironmentLighting),

            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                var viewInfo = renderGraph.GetResource<ViewInfo>();
                renderGraph.BeginNativeRenderPass(viewInfo.viewSize);

                var cameraDepth = renderGraph.GetTexture(viewInfo.viewSize, GraphicsFormat.D32_SFloat_S8_UInt, 2, TextureDimension.Tex2DArray, isScreenTexture: true, clearFlags: RTClearFlags.DepthStencil, vrTextureUsage: viewRenderData.vrTextureUsage);
                renderGraph.SetRTHandle<CameraDepth>(cameraDepth);

                var cameraTarget = renderGraph.GetTexture(viewInfo.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, 2, TextureDimension.Tex2DArray, isScreenTexture: true, clearFlags: RTClearFlags.Color, vrTextureUsage: viewRenderData.vrTextureUsage);
                renderGraph.SetRTHandle<CameraTarget>(cameraTarget);

                var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

                using var pass = renderGraph.AddObjectRenderPass("Draw Opaque");

                pass.Initialize("SRPDefaultUnlit", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
                pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
                pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<CameraTarget>();

                //pass.ReadResource<EnvironmentData>();
                pass.ReadResource<PrecomputedDfg>();
                pass.ReadResource<ViewData>();
            }),

            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                if(RenderSettings.skybox != null)
                {
                    using var pass = renderGraph.AddFullscreenRenderPass("Draw Sky");
                    pass.Initialize(RenderSettings.skybox, 0, viewRenderData.camera.stereoEnabled && !SystemInfo.supportsMultiview ? 2 : 1, viewRenderData.camera.stereoEnabled);
                    pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
                    pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
                    pass.ReadResource<ViewData>();
                }
            }),

            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

                using var pass = renderGraph.AddObjectRenderPass("Draw Transparent");
                pass.Initialize("SRPDefaultUnlit", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.transparent, SortingCriteria.CommonTransparent);
                pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepth);
                pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

                //pass.ReadResource<EnvironmentData>();
                pass.ReadResource<PrecomputedDfg>();
                pass.ReadResource<ViewData>();
            }),

            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                renderGraph.EndNativeRenderPass();
            }),

            //new SimpleBloom(renderGraph, asset.Bloom),
            new SimpleTonemap(renderGraph, asset.Tonemapping, asset.Bloom),

#if UNITY_EDITOR
            // Blit to scene view pass, editor only
            new GenericViewRenderFeature(renderGraph, viewRenderData =>
            {
                if (!viewRenderData.camera.stereoEnabled || !renderGraph.TryGetResource<XRDisplaySubsystemData>(out var displaySubsystemData))
                    return;

                displaySubsystemData.display.GetRenderPass(0, out var xrRenderPass);

                using var pass = renderGraph.AddBlitToScreenPass("End Stereo");
                pass.Initialize(xrMirrorViewMaterial);
                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("RenderMode", (float)XRSettings.gameViewRenderMode);
                    command.SetGlobalTexture("Input", xrRenderPass.renderTarget);
                });
            })
#endif
        };
    }
}
