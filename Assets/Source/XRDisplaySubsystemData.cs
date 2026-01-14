using UnityEngine.Rendering;
using UnityEngine.XR;

public readonly struct XRDisplaySubsystemData : IRenderPassData
{
    public readonly XRDisplaySubsystem display;

    public XRDisplaySubsystemData(XRDisplaySubsystem display) => this.display = display ?? throw new System.ArgumentNullException(nameof(display));

    void IRenderPassData.SetInputs(RenderPass pass)
    {
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}