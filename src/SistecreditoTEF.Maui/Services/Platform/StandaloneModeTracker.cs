namespace SistecreditoTEF.Maui.Services.Platform;

public sealed class StandaloneModeTracker : IStandaloneModeTracker
{
    public bool IsStandalone { get; set; }
    public bool IsRefundFromHioPos { get; set; }

    public void Reset()
    {
        IsStandalone = false;
        IsRefundFromHioPos = false;
    }
}