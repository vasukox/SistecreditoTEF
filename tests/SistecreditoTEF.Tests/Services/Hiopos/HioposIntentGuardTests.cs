using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// Regla de convivencia entre los dos modos que comparten la misma Activity
/// (<c>launchMode=singleTask</c>): facturación desde HioPos y recaudo desde el
/// ícono del launcher.
///
/// El caso que motivó estos tests, reproducido en el terminal: el cajero elige
/// Sistecrédito, vuelve atrás en HioPos, y vuelve a elegir Sistecrédito. Ese
/// segundo TRANSACTION se descartaba, y el módulo seguía mostrando el cliente de
/// la venta anterior.
/// </summary>
public class HioposIntentGuardTests
{
    private const string Transaction = "icg.actions.electronicpayment.permoda.TRANSACTION";
    private const string Initialize  = "icg.actions.electronicpayment.permoda.INITIALIZE";
    private const string Launcher    = HioposIntentGuard.LauncherAction;

    // ------------------------------------------------------------------
    // Acciones de HioPos: SIEMPRE se procesan
    // ------------------------------------------------------------------

    [Fact]
    public void Un_TRANSACTION_nuevo_se_procesa_aunque_haya_una_venta_viva()
    {
        // ESTE es el caso del defecto. HioPos es la autoridad sobre la venta: si
        // manda un TRANSACTION nuevo, hay que rehacer el flujo con esa venta.
        var d = HioposIntentGuard.Evaluate(Transaction,
            hioposTransactionActive: true, isStandalone: false);

        Assert.False(d.Discard);
        Assert.Contains("venta nueva", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Transaction)]
    [InlineData(Initialize)]
    [InlineData("icg.actions.electronicpayment.permoda.GET_BEHAVIOR")]
    [InlineData("icg.actions.electronicpayment.permoda.FINALIZE")]
    public void Las_acciones_de_HioPos_nunca_se_descartan(string action)
    {
        // En ninguna combinación de estado.
        foreach (var viva in new[] { true, false })
        foreach (var standalone in new[] { true, false })
        {
            var d = HioposIntentGuard.Evaluate(action, viva, standalone);
            Assert.False(d.Discard,
                $"'{action}' se descarto con venta viva={viva}, standalone={standalone}.");
        }
    }

    // ------------------------------------------------------------------
    // Launcher: se descarta solo cuando corresponde
    // ------------------------------------------------------------------

    [Fact]
    public void El_launcher_se_descarta_si_hay_una_factura_esperando_resultado()
    {
        // Es el propósito original del guard: que tocar el ícono por error no
        // rompa la venta que HioPos está esperando.
        var d = HioposIntentGuard.Evaluate(Launcher,
            hioposTransactionActive: true, isStandalone: false);

        Assert.True(d.Discard);
        Assert.Contains("HioPos", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void El_launcher_se_descarta_si_ya_se_esta_en_el_flujo_de_abonos()
    {
        // Re-entrar al ícono estando en abonos reiniciaría el flujo a mitad de camino.
        var d = HioposIntentGuard.Evaluate(Launcher,
            hioposTransactionActive: false, isStandalone: true);

        Assert.True(d.Discard);
    }

    [Fact]
    public void El_launcher_se_procesa_cuando_no_hay_nada_en_curso()
    {
        var d = HioposIntentGuard.Evaluate(Launcher,
            hioposTransactionActive: false, isStandalone: false);

        Assert.False(d.Discard);
        Assert.Contains("abonos", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Robustez
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_action_nulo_o_vacio_no_se_trata_como_launcher(string? action)
    {
        // Un Intent sin action no es el del ícono; no debe descartarse por esa vía.
        var d = HioposIntentGuard.Evaluate(action,
            hioposTransactionActive: true, isStandalone: false);

        Assert.False(d.Discard);
    }

    [Fact]
    public void Toda_decision_trae_un_motivo_para_el_log()
    {
        // El motivo es lo que permite diagnosticar en el terminal por qué un intent
        // se procesó o no.
        foreach (var action in new[] { Transaction, Launcher, Initialize })
        foreach (var viva in new[] { true, false })
        foreach (var standalone in new[] { true, false })
        {
            var d = HioposIntentGuard.Evaluate(action, viva, standalone);
            Assert.False(string.IsNullOrWhiteSpace(d.Reason));
        }
    }
}
