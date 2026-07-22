namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Resultado del chequeo de throttle al pedir un reenvio de OTP.
/// Permite al VM mostrar mensajes amigables sin acoplar la logica de
/// cooldown/max a la UI.
/// </summary>
public abstract record OtpResendDecision
{
    public sealed record Allowed : OtpResendDecision;
    public sealed record Wait(double SecondsRemaining) : OtpResendDecision;
    public sealed record Exceeded : OtpResendDecision;
}