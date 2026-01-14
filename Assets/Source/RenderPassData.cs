using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

readonly struct RenderPassData
{
    public readonly Camera camera;
    public readonly ScriptableCullingParameters cullingParameters;
    public readonly RenderTargetIdentifier renderTargetIdentifier;
    public readonly bool requiresMirrorBlit;
    public readonly Matrix4x4 worldToViewLeft, worldToViewRight, viewToClipLeft, viewToClipRight;
    public readonly SinglePassStereoMode stereoMode;
    public readonly uint instanceMultiplier;
    public readonly string stereoKeyword;
    public readonly Vector2Int size;
    public readonly VRTextureUsage vrUsage;
    public readonly GraphicsFormat graphicsFormat;

    public RenderPassData(Camera camera, ScriptableCullingParameters cullingParameters, RenderTargetIdentifier renderTargetIdentifier, bool requiresMirrorBlit, Matrix4x4 worldToViewLeft, Matrix4x4 worldToViewRight, Matrix4x4 viewToClipLeft, Matrix4x4 viewToClipRight, SinglePassStereoMode stereoMode, uint instanceMultiplier, string stereoKeyword, Vector2Int size, VRTextureUsage vrUsage, GraphicsFormat graphicsFormat)
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
        this.vrUsage = vrUsage;
        this.graphicsFormat = graphicsFormat;
    }
}