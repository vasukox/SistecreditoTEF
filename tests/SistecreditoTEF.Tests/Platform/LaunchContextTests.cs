using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Tests.Platform;

/// <summary>
/// Distinguir "la app la abrió HioPos" de "la abrió el cajero desde el ícono".
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL BUG QUE ESTO PROTEGE
/// ─────────────────────────────────────────────────────────────────────────────
/// La pantalla raíz decide a qué módulo entrar. Preguntaba por
/// <c>HioposTransactionActive</c>, pero ese flag se enciende en
/// <c>HandleTransaction</c>, que corre en <c>OnResume</c> — o sea DESPUÉS de que la
/// raíz ya apareció, porque la raíz se construye dentro de <c>base.OnCreate</c>.
///
/// Resultado en terminal: al facturar, el módulo pedía la clave del cajero —que
/// solo corresponde a los abonos— en vez de ir directo a consultar el cliente.
///
/// La acción del Intent sí está disponible a tiempo, y es lo que se usa ahora.
/// </summary>
public class LaunchContextTests
{
    [Fact]
    public void El_icono_del_launcher_NO_es_arranque_de_hiopos()
    {
        var ctx = new LaunchContext { Action = "android.intent.action.MAIN" };

        Assert.False(ctx.EsDeHiopos);
    }

    [Theory]
    [InlineData("TRANSACTION")]
    [InlineData("INITIALIZE")]
    [InlineData("GET_VERSION")]
    [InlineData("GET_BEHAVIOR")]
    [InlineData("GET_CUSTOM_PARAMS")]
    [InlineData("FINALIZE")]
    public void Cualquier_accion_del_contrato_TEF_es_arranque_de_hiopos(string operacion)
    {
        var ctx = new LaunchContext
        {
            Action = $"icg.actions.electronicpayment.{HioposActions.ApkName}.{operacion}"
        };

        Assert.True(ctx.EsDeHiopos);
    }

    [Fact]
    public void La_accion_de_TRANSACTION_real_es_arranque_de_hiopos()
    {
        // Con la constante real, no una armada a mano: si el apk_name cambiara, este
        // test sigue siendo válido.
        var ctx = new LaunchContext { Action = HioposActions.Transaction };

        Assert.True(ctx.EsDeHiopos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_accion_conocida_NO_se_asume_hiopos(string? accion)
    {
        // Falla hacia el lado seguro: si no se sabe, se trata como apertura manual y
        // se pide identificación. Asumir HioPos saltearía el ingreso del cajero.
        var ctx = new LaunchContext { Action = accion };

        Assert.False(ctx.EsDeHiopos);
    }

    [Fact]
    public void El_contexto_arranca_vacio()
    {
        var ctx = new LaunchContext();

        Assert.Null(ctx.Action);
        Assert.False(ctx.EsDeHiopos);
    }
}
