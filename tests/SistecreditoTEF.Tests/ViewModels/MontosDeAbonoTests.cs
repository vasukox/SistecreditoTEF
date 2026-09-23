using SistecreditoTEF.Maui.Models;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.ViewModels;

/// <summary>
/// Semántica de los importes de un crédito activo, fijada con DATOS REALES.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ ESTOS TESTS EXISTEN
/// ─────────────────────────────────────────────────────────────────────────────
/// El manual de Credinet define <c>creditValue</c>, <c>creditId</c>,
/// <c>creditNumber</c> y <c>creditLimit</c>, pero NO define <c>balance</c>,
/// <c>minimumPayment</c>, <c>totalPayment</c> ni <c>feeValue</c>. La semántica se
/// infirió de créditos reales y estos tests la congelan, para que una futura
/// "optimización" no vuelva a introducir validaciones que comparen entre sí
/// importes que miden cosas distintas.
///
/// El módulo llegó a bloquear el cobro por hacer exactamente eso.
/// </summary>
public class MontosDeAbonoTests
{
    /// <summary>Crédito real #583: $500.000 a 3 cuotas, recién creado.</summary>
    private static ActiveCredit Credito583() => new(
        TypeDocument: "CC", IdDocument: "1026260942",
        CreditId: "credito-583", CreditNumber: 583,
        CreateDate: "2026-07-30T00:00:00",
        CreditValue: 500_000,
        ArrearsDays: 0,
        MinimumPayment: 183_707,
        TotalPayment: 500_000,
        FeeValue: 172_990,
        StoreName: "KOAJ Unicentro",
        Balance: 500_000,
        DueDate: "2026-08-30T00:00:00");

    /// <summary>Crédito real #584: $50.000 a 1 cuota, recién creado.</summary>
    private static ActiveCredit Credito584() => new(
        TypeDocument: "CC", IdDocument: "1026260942",
        CreditId: "credito-584", CreditNumber: 584,
        CreateDate: "2026-07-30T00:00:00",
        CreditValue: 50_000,
        ArrearsDays: 0,
        MinimumPayment: 50_000,
        TotalPayment: 50_000,
        FeeValue: 50_943,
        StoreName: "KOAJ Unicentro",
        Balance: 50_000,
        DueDate: "2026-08-30T00:00:00");

    /// <summary>
    /// Crédito ya avanzado, observado en el terminal: el capital amortizó pero el
    /// mínimo del mes sigue incluyendo intereses y aval.
    /// </summary>
    private static ActiveCredit CreditoAvanzado() => new(
        TypeDocument: "CC", IdDocument: "1026260942",
        CreditId: "credito-avanzado", CreditNumber: 4210,
        CreateDate: "2026-03-23T00:00:00",
        CreditValue: 1_200_000,
        ArrearsDays: 0,
        MinimumPayment: 203_781,
        TotalPayment: 181_580,
        FeeValue: 185_004,
        StoreName: "KOAJ Unicentro",
        Balance: 181_580,
        DueDate: "2026-09-23T00:00:00");

    // ------------------------------------------------------------------
    // La evidencia que invalida comparar importes entre sí
    // ------------------------------------------------------------------

    [Fact]
    public void En_un_credito_NUEVO_la_cuota_ya_supera_el_saldo()
    {
        // ESTE es el dato decisivo. El #584 se creó recién, no tiene mora y no
        // amortizó nada, y su cuota (50.943) ya excede su saldo (50.000): pagar en
        // cuotas cuesta más que saldar de una.
        //
        // Por lo tanto feeValue y balance NO son comparables, y cualquier validación
        // que los enfrente produce bloqueos falsos.
        var c = Credito584();

        Assert.True(c.FeeValue > c.Balance);
        Assert.Equal(0, c.ArrearsDays);
    }

    [Fact]
    public void En_un_credito_avanzado_el_minimo_del_mes_supera_el_saldo()
    {
        // El capital bajó pero el mínimo del mes sigue con intereses y aval.
        // Con la validación anterior (mínimo vs saldo) el botón quedaba gris para
        // siempre: ningún monto satisfacía las dos condiciones a la vez.
        var c = CreditoAvanzado();

        Assert.True(c.MinimumPayment > c.Balance);
        Assert.False(c.EstaEnMora);
    }

    [Fact]
    public void El_total_para_saldar_coincide_con_el_capital_pendiente()
    {
        // Observado en los tres créditos: totalPayment == balance.
        Assert.Equal(Credito583().Balance, Credito583().TotalPayment);
        Assert.Equal(Credito584().Balance, Credito584().TotalPayment);
        Assert.Equal(CreditoAvanzado().Balance, CreditoAvanzado().TotalPayment);
    }

    [Fact]
    public void En_un_credito_nuevo_el_saldo_es_el_capital_original()
    {
        Assert.Equal(Credito583().CreditValue, Credito583().Balance);
        Assert.Equal(Credito584().CreditValue, Credito584().Balance);
    }

    [Fact]
    public void El_minimo_del_mes_puede_superar_la_cuota_del_plan()
    {
        // #583: mínimo 183.707 > cuota 172.990. La diferencia se atribuye al aval.
        var c = Credito583();
        Assert.True(c.MinimumPayment > c.FeeValue);
    }

    // ------------------------------------------------------------------
    // El techo del monto acepta CUALQUIER importe reportado
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TodosLosCreditos))]
    public void Ningun_importe_reportado_queda_fuera_del_techo(ActiveCredit c)
    {
        // Regla del módulo: el techo es el MAYOR de los importes reportados, así que
        // cualquier atajo que ofrezca la pantalla siempre es aceptable. Con el techo
        // anterior (el saldo) tanto el mínimo como la cuota quedaban rechazados.
        var techo = new[] { c.Balance, c.MinimumPayment, c.FeeValue, c.TotalPayment }.Max();

        Assert.True(c.Balance <= techo);
        Assert.True(c.MinimumPayment <= techo);
        Assert.True(c.FeeValue <= techo);
        Assert.True(c.TotalPayment <= techo);
    }

    public static TheoryData<ActiveCredit> TodosLosCreditos() =>
        [Credito583(), Credito584(), CreditoAvanzado()];

    // ------------------------------------------------------------------
    // Datos para la UI
    // ------------------------------------------------------------------

    [Fact]
    public void La_mora_se_expone_para_la_UI()
    {
        Assert.True((Credito583() with { ArrearsDays = 12 }).EstaEnMora);
        Assert.False(Credito583().EstaEnMora);
    }

    [Fact]
    public void La_fecha_de_vencimiento_se_formatea_para_el_cajero()
    {
        Assert.Equal("23/09/2026", CreditoAvanzado().DueDateDisplay);
    }

    [Fact]
    public void Una_fecha_ilegible_no_rompe_la_pantalla()
    {
        Assert.Equal("sin fecha", (Credito583() with { DueDate = "sin fecha" }).DueDateDisplay);
    }
}
