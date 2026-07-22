using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Throttle de reenvio de OTP. Evita que el cajero (o el codigo) regeneren
/// el codigo demasiadas veces o demasiado rapido.
///
/// Politica (configurable por IConfiguration "Credinet:OtpResendCooldownSeconds"
/// y "Credinet:OtpMaxResends", con defaults 60s y 3):
///   - Cooldown: minimo N segundos entre solicitudes.
///   - Maximo: tope de reenvios por sesion (transaccion). Se resetea al
///     Confirmar() exitoso (ConfirmacionViewModel.Finalizar o al cambiar
///     de transaccion desde MainActivity.HandleTransaction).
///
/// Singleton en DI. Sobrevive back navigation en el flujo de OTP porque
/// vive en el mismo proceso (no se recrea por transaccion).
/// </summary>
public class OtpRequestThrottle
{
    private readonly TimeSpan _cooldown;
    private readonly int _maxResends;

    private DateTime? _lastRequestedAt;
    private int _resendCount;

    public OtpRequestThrottle(TimeSpan cooldown, int maxResends)
    {
        _cooldown = cooldown;
        _maxResends = maxResends;
    }

    public OtpRequestThrottle() : this(TimeSpan.FromSeconds(60), 3) { }

    public TimeSpan Cooldown => _cooldown;
    public int MaxResends => _maxResends;
    public int ResendCount => _resendCount;
    public DateTime? LastRequestedAt => _lastRequestedAt;

    /// <summary>
    /// Chequea si se puede pedir un reenvio ahora mismo. No modifica estado.
    /// </summary>
    public OtpResendDecision CanRequest()
    {
        if (_lastRequestedAt is { } last)
        {
            var elapsed = DateTime.UtcNow - last;
            if (elapsed < _cooldown)
            {
                var remaining = _cooldown - elapsed;
                return new OtpResendDecision.Wait(remaining.TotalSeconds);
            }
            if (_resendCount >= _maxResends)
                return new OtpResendDecision.Exceeded();
        }
        return new OtpResendDecision.Allowed();
    }

    /// <summary>
    /// Registra una solicitud exitosa. Llamar SOLO cuando Credinet respondio OK.
    /// Si falla, no llamar: asi el cajero puede reintentar de inmediato.
    /// </summary>
    public void RecordRequest()
    {
        _lastRequestedAt = DateTime.UtcNow;
        _resendCount++;
    }

    /// <summary>
    /// Resetea el throttle. Llamar al iniciar una nueva transaccion o al
    /// confirmar un credito exitoso.
    /// </summary>
    public void Reset()
    {
        _lastRequestedAt = null;
        _resendCount = 0;
    }
}