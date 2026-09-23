using SistecreditoTEF.Maui.Common;
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

    // HU-134 (Fase 3): el plazo por defecto NO puede quedar en 1 (primer
    // elemento de la CollectionView) cuando el maximo de meses es, por
    // ejemplo, 6. Antes, con MesesSeleccionados=12 al inicio, la CollectionView
    // sobrescribia el plazo con su primer elemento (1) cuando el ItemsSource
    // se filtraba por el maximo. Ahora el plazo arranca en 0 y se ajusta al
    // mayor permitido apenas llega el resultado del limite.
    [Fact]
    public void MesesSeleccionados_inicia_en_cero()
    {
        var vm = BuildVm(new TransactionStateStore());
        Assert.Equal(0, vm.MesesSeleccionados);
    }

    // ==================================================================
    // El desglose en pantalla tiene que SUMAR el total
    // ==================================================================

    /// <summary>
    /// Numeros REALES de Credinet para $199.900 a 3 meses, tomados del sandbox.
    ///
    /// El defecto que protege: la pantalla mostraba "Aval" con
    /// <c>assuranceValue</c> (19.990), que es el aval SIN IVA. El desglose no sumaba
    /// el total y el cajero no tenia como explicarle la diferencia al cliente:
    ///
    ///     199.900 + 7.586 + 19.990 = 227.476  contra un total de 231.273
    ///
    /// Los 3.797 que faltaban son el IVA del aval (<c>assuranceTaxValue</c> 3.798).
    /// </summary>
    private static SistecreditoTEF.Maui.Models.CreditDetails Detalles199900() => new(
        DownPayment:            0,
        TotalFeeValue:          77091,
        CreditValue:            199900,
        Fees:                   3,
        AssuranceValue:         19990,
        InterestRate:           0.018854,
        TotalInterestValue:     7586,
        TotalDownPayment:       0,
        FeeCreditValue:         69162,
        AssuranceFeeValue:      6663,
        AssuranceTotalValue:    23788,
        AssuranceTaxFeeValue:   1266,
        AssuranceTaxValue:      3798,
        DownPaymentPercentage:  0.10,
        AssurancePercentage:    0.10,
        AssuranceTotalFeeValue: 7929,
        TotalPaymentValue:      231273,
        CustomerAllowPhotoSignature: true);

    [Fact]
    public void El_desglose_cuadra_con_el_total_a_pagar()
    {
        var d = Detalles199900();

        // Monto + intereses + aval CON IVA = total a pagar, con a lo sumo el peso de
        // redondeo que mete Credinet.
        var suma = d.CreditValue + d.TotalInterestValue + d.AssuranceTotalValue;

        Assert.InRange(suma - d.TotalPaymentValue, -1.0, 1.0);
    }

    /// <summary>
    /// Y con el aval SIN IVA no cuadra: este test documenta por que no se usa ese
    /// campo, para que nadie lo "simplifique" de vuelta.
    /// </summary>
    [Fact]
    public void Con_el_aval_sin_IVA_el_desglose_NO_cuadraria()
    {
        var d = Detalles199900();

        var sumaMal = d.CreditValue + d.TotalInterestValue + d.AssuranceValue;

        Assert.True(Math.Abs(sumaMal - d.TotalPaymentValue) > 1000,
            "Si esto empieza a cuadrar, Credinet cambio el significado de los campos " +
            "y hay que revisar el desglose de la pantalla.");
    }

    /// <summary>
    /// La cuota mensual por el numero de cuotas tiene que dar el total: es la
    /// comprobacion que un cliente hace de cabeza en el mostrador.
    /// </summary>
    [Fact]
    public void La_cuota_por_el_plazo_da_el_total()
    {
        var d = Detalles199900();

        Assert.InRange(d.TotalFeeValue * d.Fees - d.TotalPaymentValue, -1.0, 1.0);
    }

    /// <summary>
    /// La pantalla muestra el aval CON IVA. Si alguien vuelve a AssuranceValue, este
    /// test falla.
    /// </summary>
    [Fact]
    public void La_pantalla_muestra_el_aval_con_IVA()
    {
        var vm = BuildVm(new TransactionStateStore());
        vm.Detalles = Detalles199900();

        Assert.Equal(23788d.ToColombianCurrency(), vm.Aval);
        Assert.NotEqual(19990d.ToColombianCurrency(), vm.Aval);
    }

    // ==================================================================
    // La cuota, contra la definicion oficial del manual
    // ==================================================================

    /// <summary>
    /// Manual M-SCL-03 v05: <c>totalFeeValue</c> es "el valor total de la cuota para
    /// cada periodo, incluye los valores de capital, financiacion y aval", y sus
    /// partes son <c>feeCreditValue</c> (credito, sin aval), <c>assuranceFeeValue</c>
    /// (aval por cuota) y <c>assuranceTaxFeeValue</c> (IVA del aval por cuota).
    ///
    /// Con los numeros reales da EXACTO, sin redondeo: 69.162 + 6.663 + 1.266 = 77.091.
    /// Si esto dejara de cerrar, Credinet cambio el significado de algun campo.
    /// </summary>
    [Fact]
    public void La_cuota_es_capital_mas_aval_mas_IVA_del_aval()
    {
        var d = Detalles199900();

        Assert.Equal(
            d.TotalFeeValue,
            d.FeeCreditValue + d.AssuranceFeeValue + d.AssuranceTaxFeeValue);
    }

    /// <summary>
    /// La parte de credito de la cuota, por el plazo, tiene que dar el monto mas los
    /// intereses: 69.162 x 3 = 207.486 = 199.900 + 7.586. Exacto.
    ///
    /// Es la comprobacion de que los intereses que mostramos son los de ESTE credito
    /// y no un valor de otra escala.
    /// </summary>
    [Fact]
    public void La_parte_de_credito_por_el_plazo_da_monto_mas_intereses()
    {
        var d = Detalles199900();

        Assert.Equal(
            d.FeeCreditValue * d.Fees,
            d.CreditValue + d.TotalInterestValue);
    }

    /// <summary>
    /// El aval total es el aval por cuota por el plazo, y el IVA total idem. Fija que
    /// no se mezclen los campos "por cuota" con los "totales", que es el error facil:
    /// se llaman casi igual.
    /// </summary>
    [Fact]
    public void Los_totales_del_aval_son_los_por_cuota_por_el_plazo()
    {
        var d = Detalles199900();

        Assert.InRange(d.AssuranceFeeValue * d.Fees - d.AssuranceValue, -1.0, 1.0);
        Assert.InRange(d.AssuranceTaxFeeValue * d.Fees - d.AssuranceTaxValue, -1.0, 1.0);
        Assert.InRange(
            d.AssuranceValue + d.AssuranceTaxValue - d.AssuranceTotalValue, -1.0, 1.0);
    }

    /// <summary>
    /// El monto del desglose sale de <c>creditValue</c> de la RESPUESTA, no del campo
    /// de captura. Se comprueba con un valor distinto del que tiene el campo: si
    /// leyera [Monto], daria $0 porque el campo esta vacio.
    /// </summary>
    [Fact]
    public void El_monto_financiado_sale_de_la_respuesta()
    {
        var vm = BuildVm(new TransactionStateStore());
        vm.Detalles = Detalles199900();

        Assert.Equal(199900d.ToColombianCurrency(), vm.MontoFinanciado);
        Assert.Equal(0m, vm.Monto);   // el campo esta vacio y aun asi el desglose sabe
    }

    /// <summary>
    /// Y si el cajero cambia el monto despues de simular, el desglose NO se queda con
    /// numeros viejos: la simulacion se invalida entera.
    ///
    /// Es la propiedad que importa en una pantalla que origina un credito. Un
    /// desglose que sobrevive al cambio de monto es peor que ninguno: describe una
    /// operacion que ya no es la que el cajero esta a punto de confirmar.
    /// </summary>
    [Fact]
    public void Cambiar_el_monto_invalida_el_desglose_entero()
    {
        var vm = BuildVm(new TransactionStateStore());
        vm.Detalles = Detalles199900();
        vm.Status = SeleccionCuotasViewModel.EstadoSimulacion.Success;

        vm.MontoTexto = "999.999";   // el cajero cambia el valor

        Assert.False(vm.HasResults);
        Assert.Equal("$ 0", vm.MontoFinanciado);
        Assert.Equal("$ 0", vm.CuotaMensual);
        Assert.Equal("$ 0", vm.TotalPagar);
    }

    /// <summary>
    /// La tasa que se imprime en el comprobante es la EFECTIVA ANUAL, y Credinet la
    /// manda como fraccion. 0,251241 tiene que leerse 25,12%, no 0,25% ni 1,89%
    /// (que seria la mensual, <c>interestRate</c>).
    /// </summary>
    [Fact]
    public void La_tasa_efectiva_anual_se_formatea_como_porcentaje()
    {
        Assert.Equal("25.12%", 0.251241.ToColombianPercentage());
    }
}
