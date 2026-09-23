using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Control anti-abuso del OTP. Vive como SINGLETON en DI para sobrevivir a la
/// navegación entre pantallas.
///
/// Politica (configurable, ver [ApiConfig]):
///   - Cooldown: mínimo N segundos entre solicitudes de código.
///   - Máximo de reenvíos por transacción.
///   - Máximo de INTENTOS DE VERIFICACIÓN por transacción.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA A-12 — POR QUÉ LOS INTENTOS SE CUENTAN AQUÍ Y NO EN EL VIEWMODEL
/// ─────────────────────────────────────────────────────────────────────────────
/// Antes el contador de intentos era <c>private int _attempts</c> dentro de
/// [OtpViewModel], y los ViewModels están registrados como **Transient**. Bastaba
/// navegar atrás y volver a la pantalla de OTP para obtener un ViewModel nuevo
/// con <c>_attempts = 0</c>, mientras el mismo OTP seguía vigente: el tope de 3
/// intentos se reiniciaba a voluntad. Un control de fuerza bruta no puede vivir
/// en un objeto que se recrea con cada navegación.
///
/// Al moverlo al singleton, el tope se respeta durante toda la transacción y solo
/// se reinicia donde corresponde: al iniciar una TRANSACTION nueva o al pedir un
/// código nuevo.
///
/// QA M-8: todos los accesos están sincronizados. Antes no lo estaban, aunque el
/// objeto se toca desde el hilo de UI y desde las continuaciones async.
/// </summary>
public class OtpRequestThrottle
{
    private readonly TimeSpan _cooldown;
    private readonly int _maxResends;
    private readonly int _maxVerifyAttempts;
    private readonly object _gate = new();

    private DateTime? _lastRequestedAt;
    private int _resendCount;
    private int _verifyAttempts;

    public OtpRequestThrottle(TimeSpan cooldown, int maxResends, int maxVerifyAttempts = 3)
    {
        _cooldown = cooldown;
        _maxResends = maxResends;
        _maxVerifyAttempts = Math.Max(1, maxVerifyAttempts);
    }

    public OtpRequestThrottle() : this(TimeSpan.FromSeconds(60), 3) { }

    public TimeSpan Cooldown => _cooldown;
    public int MaxResends => _maxResends;
    public int MaxVerifyAttempts => _maxVerifyAttempts;

    public int ResendCount { get { lock (_gate) return _resendCount; } }
    public DateTime? LastRequestedAt { get { lock (_gate) return _lastRequestedAt; } }

    /// <summary>Intentos de verificación ya consumidos en esta transacción.</summary>
    public int VerifyAttempts { get { lock (_gate) return _verifyAttempts; } }

    /// <summary>Intentos de verificación que le quedan al cajero.</summary>
    public int VerifyAttemptsLeft
    {
        get { lock (_gate) return Math.Max(0, _maxVerifyAttempts - _verifyAttempts); }
    }

    /// <summary>True si se agotaron los intentos de verificación.</summary>
    public bool VerifyAttemptsExhausted => VerifyAttemptsLeft <= 0;

    /// <summary>
    /// ¿Se puede pedir un código ahora? No modifica estado.
    ///
    /// QA: el chequeo de "máximo alcanzado" ya NO está anidado dentro del
    /// chequeo de cooldown. Antes <c>Exceeded</c> solo se devolvía una vez
    /// transcurrido el cooldown, así que el cajero veía "Espera 60s" y solo
    /// después "máximo alcanzado" — confuso y sin motivo.
    /// </summary>
    public OtpResendDecision CanRequest()
    {
        lock (_gate)
        {
            if (_resendCount >= _maxResends)
                return new OtpResendDecision.Exceeded();

            if (_lastRequestedAt is { } last)
            {
                var elapsed = DateTime.UtcNow - last;
                if (elapsed < _cooldown)
                    return new OtpResendDecision.Wait((_cooldown - elapsed).TotalSeconds);
            }

            return new OtpResendDecision.Allowed();
        }
    }

    /// <summary>
    /// Registra una solicitud exitosa de código. Llamar SOLO cuando Credinet
    /// respondió OK; si falla, no llamar, así el cajero puede reintentar ya.
    ///
    /// Un código nuevo reinicia los intentos de verificación: son intentos
    /// "contra ese código".
    /// </summary>
    public void RecordRequest()
    {
        lock (_gate)
        {
            _lastRequestedAt = DateTime.UtcNow;
            _resendCount++;
            _verifyAttempts = 0;
        }
    }

    /// <summary>
    /// Registra un intento de verificación FALLIDO por código incorrecto.
    /// Devuelve los intentos restantes.
    ///
    /// QA M-9: NO llamar cuando el fallo es de red o de infraestructura. Antes el
    /// <c>catch (Exception)</c> del ViewModel incrementaba el contador, así que
    /// tres cortes de red seguidos dejaban al cajero con "Se agotaron los
    /// intentos" y un OTP perfectamente válido.
    /// </summary>
    public int RecordFailedVerification()
    {
        lock (_gate)
        {
            _verifyAttempts++;
            return Math.Max(0, _maxVerifyAttempts - _verifyAttempts);
        }
    }

    /// <summary>
    /// Reinicia todo el throttle. Llamar al iniciar una transacción nueva o al
    /// confirmar un crédito exitoso.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _lastRequestedAt = null;
            _resendCount = 0;
            _verifyAttempts = 0;
        }
        AppLogger.I("OtpRequestThrottle", "Throttle de OTP reiniciado.");
    }
}
