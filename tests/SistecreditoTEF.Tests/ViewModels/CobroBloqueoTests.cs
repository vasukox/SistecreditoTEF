using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// El botón de cobrar tiene que estar deshabilitado mientras el abono está en vuelo.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTE TEST PROTEGE
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>EvaluarBloqueo</c> hacía:
///
///     if (Status == Estado.Loading) return string.Empty;   // en curso, sin mensaje
///
/// Cadena vacía significa "no hay motivo de bloqueo", así que <c>CanPagar</c>
/// devolvía true y el botón quedaba habilitado durante todo el POST a
/// <c>payCredit</c>. Y como ese return corta antes de
/// <c>HasRecentPaymentAttempt()</c>, también se salteaba la barrera del minuto.
///
/// Un segundo toque —normal en un POS lento— disparaba un segundo cobro. No se
/// duplicaba la plata porque la idempotencia persistida lo atrapaba, pero esa es la
/// última red, no la primera: el cajero terminaba en un estado "en duda" que lo
/// obligaba a ir a verificar el saldo por una operación que nunca fue ambigua.
/// </summary>
public class CobroBloqueoTests
{
    private static PagoViewModel BuildVm(out TransactionStateStore state)
    {
        state = new TransactionStateStore();
        return new PagoViewModel(
            service: null!, nav: null!, state: state,
            sesion: new SistecreditoTEF.Maui.Services.Auth.SesionCajero());
    }

    private static ActiveCredit Credito() => new(
        TypeDocument: "CC", IdDocument: "1234567890", CreditId: "CRED-1",
        CreditNumber: 583, CreateDate: "2026-01-01", CreditValue: 500_000,
        ArrearsDays: 0, MinimumPayment: 100_000, TotalPayment: 400_000,
        FeeValue: 90_000, StoreName: "KOAJ", Balance: 300_000,
        DueDate: "2026-09-23T00:00:00");

    [Fact]
    public void Mientras_el_cobro_esta_en_vuelo_no_se_puede_volver_a_cobrar()
    {
        var vm = BuildVm(out _);
        vm.CreditoSeleccionado = Credito();
        vm.MontoTexto = "100.000";

        // Con el cobro en curso el botón NO puede quedar habilitado.
        vm.Status = PagoViewModel.Estado.Loading;

        Assert.False(vm.PagarCommand.CanExecute(null));
    }

    [Fact]
    public void Mientras_el_cobro_esta_en_vuelo_se_explica_el_motivo()
    {
        var vm = BuildVm(out _);
        vm.CreditoSeleccionado = Credito();
        vm.MontoTexto = "100.000";
        vm.Status = PagoViewModel.Estado.Loading;

        _ = vm.PagarCommand.CanExecute(null);

        // Un botón gris sin explicación se lee como "la app no funciona".
        Assert.False(string.IsNullOrEmpty(vm.MotivoBloqueo));
    }

    [Fact]
    public void Con_monto_valido_y_sin_cobro_en_curso_si_se_puede_cobrar()
    {
        // Contraprueba: el bloqueo no debe dejar el botón gris para siempre, que es
        // el defecto opuesto y ya ocurrió antes en este proyecto.
        var vm = BuildVm(out _);
        vm.CreditoSeleccionado = Credito();
        vm.MontoTexto = "100.000";

        Assert.True(vm.PagarCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.MotivoBloqueo);
    }

    [Fact]
    public void Un_abono_en_duda_bloquea_hasta_verificar()
    {
        var vm = BuildVm(out _);
        vm.CreditoSeleccionado = Credito();
        vm.MontoTexto = "100.000";
        vm.RequiereVerificacion = true;

        Assert.False(vm.PagarCommand.CanExecute(null));
        Assert.Contains("Verifica", vm.MotivoBloqueo);
    }
}
