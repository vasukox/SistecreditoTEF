using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// EL NOMBRE DE TIENDA QUE VENIA EN EL CREDITO NO SE MUESTRA MAS.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO QUE PASO EN LA TIENDA
/// ─────────────────────────────────────────────────────────────────────────────
/// Desde una tienda de Suba se reporto que, al buscar por cedula para recibir un
/// abono, el credito aparecia rotulado "037 Tienda Koaj Cll 18 Montevideo" — una
/// tienda de Bogota. El credito se habia abierto en Suba. O sea que el
/// <c>storeName</c> de <c>getactivecredits</c> NO describe al credito: describe a
/// quien pregunta.
///
/// Y como iba suelto al lado del numero de credito, sin rotulo, el cajero lo leia
/// como "este credito se tomo alla" y se lo decia al cliente.
///
/// Se reemplazo por la fecha de apertura, que si es un dato del credito. Estos
/// tests fijan las dos mitades: que la fecha salga bien, y que el nombre de tienda
/// no vuelva a colarse por la puerta de atras.
/// </summary>
public class TiendaDelCreditoTests
{
    /// <summary>Valor bien reconocible: si reaparece en pantalla, se ve.</summary>
    private const string TiendaDeCredinet = "037 Tienda Koaj Cll 18 Montevideo";

    private static ActiveCredit Credito(string createDate) => new(
        TypeDocument: "CC", IdDocument: "1234567890", CreditId: "CRED-1",
        CreditNumber: 7076, CreateDate: createDate, CreditValue: 500_000,
        ArrearsDays: 0, MinimumPayment: 65_435, TotalPayment: 359_900,
        FeeValue: 64_529, StoreName: TiendaDeCredinet, Balance: 359_900,
        DueDate: "2026-10-29T00:00:00");

    private static PagoViewModel Pantalla(ActiveCredit credito)
    {
        var vm = new PagoViewModel(
            service: null!, nav: null!, state: new TransactionStateStore(),
            sesion: new SistecreditoTEF.Maui.Services.Auth.SesionCajero());
        vm.CreditoSeleccionado = credito;
        return vm;
    }

    // ------------------------------------------------------------------
    // La fecha de apertura
    // ------------------------------------------------------------------

    [Fact]
    public void LaFechaDeApertura_SaleEnFormatoColombiano()
    {
        var credito = Credito("2026-03-12T00:00:00");

        Assert.Equal("12/03/2026", credito.CreateDateDisplay);
        Assert.True(credito.TieneFechaDeApertura);
    }

    /// <summary>
    /// Si la fecha no se puede interpretar, NO se inventa un guion: la fila
    /// desaparece. "Abierto el -" no informa y hace dudar del resto de la tarjeta.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-una-fecha")]
    public void SinFechaUtilizable_NoSeMuestraNada(string createDate)
    {
        var credito = Credito(createDate);

        Assert.Equal(string.Empty, credito.CreateDateDisplay);
        Assert.False(credito.TieneFechaDeApertura);
    }

    // ------------------------------------------------------------------
    // La pantalla de cobro
    // ------------------------------------------------------------------

    [Fact]
    public void LaPantallaDeCobro_NoMuestraLaTiendaQueDevuelveCredinet()
    {
        var vm = Pantalla(Credito("2026-03-12T00:00:00"));

        Assert.DoesNotContain("Koaj", vm.Subtitulo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Montevideo", vm.Subtitulo, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Abierto el 12/03/2026", vm.Subtitulo);
    }

    [Fact]
    public void SinFecha_ElSubtituloQuedaVacio_NoAMedias()
    {
        var vm = Pantalla(Credito("no-es-una-fecha"));

        Assert.Equal(string.Empty, vm.Subtitulo);
    }

    [Fact]
    public void SinCreditoSeleccionado_NoSeRompe()
    {
        var vm = new PagoViewModel(
            service: null!, nav: null!, state: new TransactionStateStore(),
            sesion: new SistecreditoTEF.Maui.Services.Auth.SesionCajero());

        Assert.Equal(string.Empty, vm.Subtitulo);
    }
}
