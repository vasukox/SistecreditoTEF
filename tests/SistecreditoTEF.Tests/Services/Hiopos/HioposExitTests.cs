using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using HioposResultCodes = SistecreditoTEF.Maui.Services.Hiopos.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// Tests de "Volver a HioPos".
///
/// Lo que se protege aca no es el texto de un boton: es que salirse del modulo con
/// una venta de HioPos viva SIEMPRE le devuelva un resultado al POS. Si no, HioPos
/// queda esperando y la venta queda colgada en la caja, y el sintoma (una factura
/// que no avanza) no se parece en nada a la causa.
/// </summary>
public class HioposExitTests
{
    private static (HioposExit salida, TransactionStateStore estado, FakeResultHandler pos) Build(
        bool ventaViva,
        string? transactionType = "SALE")
    {
        var estado = new TransactionStateStore();

        if (ventaViva)
        {
            estado.SetActiveTransaction(TransaccionDe(transactionType));
            estado.HioposTransactionActive = true;
        }

        var pos = new FakeResultHandler();
        return (new HioposExit(estado, pos, new HioposResultBuilder(new ReceiptBuilder())), estado, pos);
    }

    private static HioposTransaction TransaccionDe(string? tipo) =>
        new(TransactionType: tipo,
            TenderType: "CREDIT",
            CurrencyIso: "COP",
            LanguageIso: "es",
            AmountCents: "5000000",
            TipAmountCents: null,
            TaxAmountCents: null,
            TaxDetail: null,
            TransactionId: "tx-1",
            TransactionData: null,
            ReceiptPrinterColumns: "42",
            IsAdvancedPayment: false,
            OverPaymentType: 0,
            SurchargeCents: null,
            DocumentData: null,
            DocumentPath: null,
            ShopData: null,
            SellerData: null);

    // ------------------------------------------------------------------
    // Dentro de una operacion de HioPos
    // ------------------------------------------------------------------

    [Fact]
    public void Disponible_es_true_solo_con_una_operacion_viva()
    {
        var (conVenta, _, _)  = Build(ventaViva: true);
        var (sinVenta, _, _)  = Build(ventaViva: false);

        Assert.True(conVenta.Disponible);
        Assert.False(sinVenta.Disponible);
    }

    [Fact]
    public void Volver_le_entrega_el_resultado_al_POS()
    {
        var (salida, _, pos) = Build(ventaViva: true);

        var manejado = salida.Volver("test");

        Assert.True(manejado);
        Assert.NotNull(pos.Respuesta);
        Assert.Equal(HioposActions.Transaction, pos.Respuesta!.Action);
        Assert.Equal(HioposResultCodes.Result.OkValue, pos.Respuesta.ResultCode);
        Assert.Equal("FAILED", pos.Respuesta.StringExtras[HioposExtras.TransactionResult]);
    }

    /// <summary>
    /// El tipo se lee ANTES de limpiar el estado. Si el orden se invirtiera, Clear()
    /// ya habria borrado la transaccion y el POS recibiria la salida sin saber que
    /// operacion se esta cerrando — un bug silencioso, porque todo lo demas seguiria
    /// funcionando.
    /// </summary>
    [Fact]
    public void Volver_hace_eco_del_tipo_leyendolo_antes_de_limpiar()
    {
        var (salida, _, pos) = Build(ventaViva: true, transactionType: "REFUND");

        salida.Volver("test");

        Assert.Equal("REFUND", pos.Respuesta!.StringExtras[HioposExtras.TransactionType]);
    }

    [Fact]
    public void Volver_limpia_el_estado_del_flujo()
    {
        var (salida, estado, _) = Build(ventaViva: true);

        salida.Volver("test");

        Assert.False(estado.HioposTransactionActive);
        Assert.Null(estado.ActiveTransaction);
    }

    /// <summary>
    /// Dos toques rapidos en el boton no pueden mandar dos resultados: el segundo
    /// llega cuando el estado ya esta limpio y tiene que ser un no-op.
    /// </summary>
    [Fact]
    public void Volver_dos_veces_solo_responde_una_vez()
    {
        var (salida, _, pos) = Build(ventaViva: true);

        Assert.True(salida.Volver("primer toque"));
        Assert.False(salida.Volver("segundo toque"));

        Assert.Equal(1, pos.Llamadas);
    }

    // ------------------------------------------------------------------
    // Fuera de HioPos (abonos abiertos desde el icono)
    // ------------------------------------------------------------------

    /// <summary>
    /// Sin operacion viva no se le habla al POS: la app arranco desde el icono y el
    /// "atras" tiene que ser un pop normal. Devolver true aca cerraria la app en la
    /// cara del cajero.
    /// </summary>
    [Fact]
    public void Volver_sin_operacion_no_hace_nada_y_cede_la_navegacion()
    {
        var (salida, _, pos) = Build(ventaViva: false);

        var manejado = salida.Volver("test");

        Assert.False(manejado);
        Assert.Equal(0, pos.Llamadas);
    }

    // ------------------------------------------------------------------
    // Si el POS no acepta el resultado
    // ------------------------------------------------------------------

    /// <summary>
    /// Si entregar el resultado falla, se devuelve false para que el llamador haga
    /// su navegacion normal. Quedarse en la pantalla con el boton muerto es la peor
    /// de las salidas posibles para quien esta en caja.
    /// </summary>
    [Fact]
    public void Volver_devuelve_false_si_el_POS_lanza()
    {
        var estado = new TransactionStateStore();
        estado.SetActiveTransaction(TransaccionDe("SALE"));
        estado.HioposTransactionActive = true;

        var salida = new HioposExit(estado, new ThrowingResultHandler(),
            new HioposResultBuilder(new ReceiptBuilder()));

        Assert.False(salida.Volver("test"));
    }

    private sealed class FakeResultHandler : ITransactionResultHandler
    {
        public HioposResponse? Respuesta { get; private set; }
        public int Llamadas { get; private set; }

        public void FinishWithResult(HioposResponse response)
        {
            Respuesta = response;
            Llamadas++;
        }
    }

    private sealed class ThrowingResultHandler : ITransactionResultHandler
    {
        public void FinishWithResult(HioposResponse response) =>
            throw new InvalidOperationException("no hay Activity");
    }
}
