using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// HU-134: al abrir la pantalla de simulacion, el monto debe precargarse con
/// lo facturado en HioPos en vez de quedar en 0 para que el cajero lo digite.
/// </summary>
public class SeleccionCuotasViewModelTests
{
    private static HioposTransaction Sale(string? amountCents) => new(
        TransactionType: "SALE",
        TenderType: null,
        CurrencyIso: null,
        LanguageIso: null,
        AmountCents: amountCents,
        TipAmountCents: null,
        TaxAmountCents: null,
        TaxDetail: null,
        TransactionId: null,
        TransactionData: null,
        ReceiptPrinterColumns: null,
        IsAdvancedPayment: false,
        OverPaymentType: 0,
        SurchargeCents: null,
        DocumentData: null,
        DocumentPath: null,
        ShopData: null,
        SellerData: null);

    // service/nav no se usan en Inicializar(); se pasan null! a proposito.
    private static SeleccionCuotasViewModel BuildVm(ITransactionStateStore state) =>
        new(service: null!, state: state, nav: null!);

    [Fact]
    public void Inicializar_precarga_monto_desde_amount_de_hipos()
    {
        var state = new TransactionStateStore();
        state.SetActiveTransaction(Sale("8500000")); // 85.000,00 pesos en centavos
        var vm = BuildVm(state);

        vm.Inicializar();

        Assert.Equal(85000m, vm.Monto);
    }

    [Fact]
    public void Inicializar_sin_amount_deja_monto_en_cero()
    {
        var state = new TransactionStateStore();
        state.SetActiveTransaction(Sale(null));
        var vm = BuildVm(state);

        vm.Inicializar();

        Assert.Equal(0m, vm.Monto);
    }

    [Fact]
    public void Inicializar_no_pisa_un_monto_ya_ingresado()
    {
        var state = new TransactionStateStore();
        state.SetActiveTransaction(Sale("8500000"));
        var vm = BuildVm(state);
        vm.Monto = 120000m; // el cajero ya ajusto el valor a mano

        vm.Inicializar();

        Assert.Equal(120000m, vm.Monto);
    }
}
