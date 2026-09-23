using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Platform;

/// <summary>
/// QA A-12 / M-8: el throttle es el control anti-fuerza-bruta del OTP y no tenía
/// ni un test.
///
/// El defecto principal: el contador de intentos de verificación vivía en
/// [OtpViewModel], que es Transient. Navegar atrás y volver creaba un ViewModel
/// nuevo con el contador en cero mientras el mismo OTP seguía vigente, o sea que
/// el tope de 3 intentos se reiniciaba a voluntad. Al moverlo al singleton, el
/// tope se respeta durante toda la transacción.
/// </summary>
public class OtpRequestThrottleTests
{
    private static OtpRequestThrottle Build(
        int cooldownSeconds = 60, int maxResends = 3, int maxVerify = 3) =>
        new(TimeSpan.FromSeconds(cooldownSeconds), maxResends, maxVerify);

    // ------------------------------------------------------------------
    // Reenvíos
    // ------------------------------------------------------------------

    [Fact]
    public void La_primera_solicitud_esta_permitida()
    {
        Assert.IsType<OtpResendDecision.Allowed>(Build().CanRequest());
    }

    [Fact]
    public void Dentro_del_cooldown_pide_esperar()
    {
        var throttle = Build(cooldownSeconds: 60);
        throttle.RecordRequest();

        var decision = Assert.IsType<OtpResendDecision.Wait>(throttle.CanRequest());
        Assert.InRange(decision.SecondsRemaining, 55, 60);
    }

    [Fact]
    public void Sin_cooldown_se_permite_otra_solicitud()
    {
        var throttle = Build(cooldownSeconds: 0);
        throttle.RecordRequest();

        Assert.IsType<OtpResendDecision.Allowed>(throttle.CanRequest());
    }

    [Fact]
    public void Al_alcanzar_el_maximo_dice_Exceeded_de_inmediato()
    {
        // QA: antes el chequeo de "máximo alcanzado" estaba ANIDADO dentro del de
        // cooldown, así que Exceeded solo aparecía una vez transcurrido el
        // cooldown: el cajero veía "Espera 60s" y solo después "máximo
        // alcanzado". Confuso y sin motivo.
        var throttle = Build(cooldownSeconds: 60, maxResends: 2);
        throttle.RecordRequest();
        throttle.RecordRequest();

        Assert.IsType<OtpResendDecision.Exceeded>(throttle.CanRequest());
    }

    [Fact]
    public void Reset_habilita_nuevas_solicitudes()
    {
        var throttle = Build(maxResends: 1);
        throttle.RecordRequest();
        Assert.IsType<OtpResendDecision.Exceeded>(throttle.CanRequest());

        throttle.Reset();

        Assert.IsType<OtpResendDecision.Allowed>(throttle.CanRequest());
        Assert.Equal(0, throttle.ResendCount);
    }

    // ------------------------------------------------------------------
    // Intentos de verificación (QA A-12)
    // ------------------------------------------------------------------

    [Fact]
    public void Los_intentos_de_verificacion_se_agotan_y_no_bajan_de_cero()
    {
        var throttle = Build(maxVerify: 3);

        Assert.Equal(3, throttle.VerifyAttemptsLeft);
        Assert.Equal(2, throttle.RecordFailedVerification());
        Assert.Equal(1, throttle.RecordFailedVerification());
        Assert.Equal(0, throttle.RecordFailedVerification());
        Assert.True(throttle.VerifyAttemptsExhausted);

        Assert.Equal(0, throttle.RecordFailedVerification());
        Assert.Equal(0, throttle.VerifyAttemptsLeft);
    }

    [Fact]
    public void Un_codigo_nuevo_reinicia_los_intentos_de_verificacion()
    {
        // Son intentos "contra ese código": si se pide otro, es justo empezar de
        // nuevo. Lo que NO se reinicia es el contador de reenvíos.
        var throttle = Build(cooldownSeconds: 0, maxResends: 5, maxVerify: 3);
        throttle.RecordFailedVerification();
        throttle.RecordFailedVerification();
        Assert.Equal(1, throttle.VerifyAttemptsLeft);

        throttle.RecordRequest();

        Assert.Equal(3, throttle.VerifyAttemptsLeft);
        Assert.Equal(1, throttle.ResendCount);
    }

    [Fact]
    public void El_tope_de_intentos_sobrevive_a_la_navegacion()
    {
        // La instancia es la MISMA (singleton en DI) aunque el ViewModel se
        // recree: eso es exactamente lo que faltaba.
        var throttle = Build(maxVerify: 3);
        throttle.RecordFailedVerification();
        throttle.RecordFailedVerification();
        throttle.RecordFailedVerification();

        // Simula volver a entrar a la pantalla: mismo throttle, sin reset.
        Assert.True(throttle.VerifyAttemptsExhausted);
        Assert.Equal(0, throttle.VerifyAttemptsLeft);
    }

    [Fact]
    public void MaxVerifyAttempts_nunca_es_menor_que_uno()
    {
        // Una configuración en 0 dejaría al cajero sin poder verificar nunca.
        Assert.Equal(1, new OtpRequestThrottle(TimeSpan.Zero, 3, maxVerifyAttempts: 0).MaxVerifyAttempts);
    }

    // ------------------------------------------------------------------
    // Concurrencia (QA M-8: no había sincronización)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Los_contadores_son_thread_safe()
    {
        var throttle = Build(cooldownSeconds: 0, maxResends: int.MaxValue, maxVerify: int.MaxValue);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                throttle.RecordRequest();
                throttle.RecordFailedVerification();
            }
        })));

        // 8 hilos x 200 = 1600 solicitudes, sin incrementos perdidos.
        Assert.Equal(1600, throttle.ResendCount);
    }
}
