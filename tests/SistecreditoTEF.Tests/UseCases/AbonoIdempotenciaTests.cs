using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;
using Xunit;
// Alias explícito: dentro de este namespace de tests, "Services.Hiopos" resolvería
// al namespace de los tests, no al de producción.
using HioposTransaction = SistecreditoTEF.Maui.Services.Hiopos.HioposTransaction;

namespace SistecreditoTEF.Maui.Tests.UseCases;

/// <summary>
/// QA C-5 — DOBLE COBRO EN ABONOS.
///
/// El flujo de crédito tenía doble barrera (idempotencia local por SaleId +
/// <c>invoice</c> remoto). El de abono tenía UNA sola y era volátil: un guard en
/// memoria de 60 s dentro de un singleton. Y <c>payCredit</c> viaja sin ningún
/// campo de idempotencia, así que Credinet tampoco puede deduplicar.
///
/// Escenario reproducible del defecto:
///   1. El cajero cobra $200.000.
///   2. Credinet procesa el pago; la respuesta se pierde (timeout / el POS se
///      reinicia / la app se mata por presión de memoria).
///   3. El proceso arranca de nuevo → estado en memoria vacío → el guard no existe.
///   4. El cajero, que vio un error, vuelve a cobrar → el cliente paga dos veces.
///
/// Ahora el intento se persiste ANTES de llamar a Credinet, así que sobrevive al
/// reinicio.
/// </summary>
public class AbonoIdempotenciaTests
{
    // ------------------------------------------------------------------
    // Camino feliz
    // ------------------------------------------------------------------

    [Fact]
    public async Task Un_abono_exitoso_queda_registrado_como_completado()
    {
        var idem = new FakePaymentStore();
        var service = Build(idem, OnPagar: (_, _, _) =>
            new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 5001)));

        var outcome = await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");

        var ok = Assert.IsType<PaymentOutcome.Ok>(outcome);
        Assert.Equal("pay-1", ok.Payment.PaymentId);

        // Pendiente primero, Completado después: el orden importa, porque es lo que
        // hace que un reinicio entre medio deje rastro.
        Assert.Equal(2, idem.History.Count);
        Assert.Equal(PaymentAttemptStatus.Pending, idem.History[0].Status);
        Assert.Equal(PaymentAttemptStatus.Completed, idem.History[1].Status);
    }

    [Fact]
    public async Task El_intento_se_persiste_ANTES_de_llamar_a_Credinet()
    {
        // Si se persistiera después, un corte justo durante la llamada no dejaría
        // evidencia y el reintento cobraría de nuevo.
        var idem = new FakePaymentStore();
        var pendienteAlLlamar = false;

        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            pendienteAlLlamar = idem.History.Any(p => p.Status == PaymentAttemptStatus.Pending);
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");

        Assert.True(pendienteAlLlamar,
            "El intento debe estar persistido como Pending antes del POST a Credinet.");
    }

    // ------------------------------------------------------------------
    // El defecto central: reintento tras perder la respuesta
    // ------------------------------------------------------------------

    [Fact]
    public async Task Un_reintento_del_mismo_abono_NO_vuelve_a_cobrar()
    {
        var idem = new FakePaymentStore();
        var llamadas = 0;
        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 5001));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        var segundo = await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");

        Assert.Equal(1, llamadas);   // Credinet se llamó UNA sola vez
        var already = Assert.IsType<PaymentOutcome.AlreadyPaid>(segundo);
        Assert.Equal("pay-1", already.Payment.PaymentId);
    }

    // ==================================================================
    // El userName nunca sale vacio hacia Credinet
    // ==================================================================

    /// <summary>
    /// Credinet rechaza <c>payCredit</c> con HTTP 400 "[REP-E-003] El campo UserName
    /// es obligatorio en la peticion" si el nombre llega vacio, y el abono no se
    /// cobra. Eso ya dejo una caja entera sin poder recaudar, y el 400 apuntaba al
    /// proveedor en lugar de a nosotros. Ver [AbonoUserNameTests] para la causa.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task Un_userName_vacio_no_llega_a_Credinet(string userNameVacio)
    {
        string? enviado = null;
        var service = Build(new FakePaymentStore(), (_, _, userName) =>
        {
            enviado = userName;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, userNameVacio);

        Assert.False(string.IsNullOrWhiteSpace(enviado),
            "Con userName vacio Credinet devuelve REP-E-003 y el abono no se cobra.");
    }

    /// <summary>
    /// El abono se COBRA igual, con un rotulo de respaldo. Es una decision explicita:
    /// dejar a la caja sin poder recaudar por un defecto nuestro de nombres es peor
    /// que registrar el abono con un rotulo generico y dejarlo en la auditoria.
    /// </summary>
    [Fact]
    public async Task Con_userName_vacio_el_abono_igual_se_cobra()
    {
        var service = Build(new FakePaymentStore(),
            (_, _, _) => new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1)));

        var outcome = await service.PagarCreditoAsync("credit-1", 200_000m, "");

        Assert.IsType<PaymentOutcome.Ok>(outcome);
    }

    [Fact]
    public async Task Un_userName_valido_se_manda_tal_cual()
    {
        string? enviado = null;
        var service = Build(new FakePaymentStore(), (_, _, userName) =>
        {
            enviado = userName;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "Felipe Arango");

        Assert.Equal("Felipe Arango", enviado);
    }

    /// <summary>
    /// Un nombre tipeado en el POS con un espacio de sobra quedaba asi en el registro
    /// de Credinet.
    /// </summary>
    [Fact]
    public async Task El_userName_se_recorta()
    {
        string? enviado = null;
        var service = Build(new FakePaymentStore(), (_, _, userName) =>
        {
            enviado = userName;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "  felipe  ");

        Assert.Equal("felipe", enviado);
    }

    [Fact]
    public async Task La_barrera_sobrevive_al_reinicio_del_proceso()
    {
        // El store persiste; el servicio y el estado en memoria se recrean, como
        // ocurre cuando Android mata el proceso del POS.
        var idem = new FakePaymentStore();
        var llamadas = 0;
        Func<string, double, string, ApiResult<Payment>> pagar = (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 5001));
        };

        await Build(idem, pagar).PagarCreditoAsync("credit-1", 200_000m, "Cajero");

        // "Reinicio": servicio nuevo, estado nuevo, MISMO store en disco.
        var trasReinicio = await Build(idem, pagar)
            .PagarCreditoAsync("credit-1", 200_000m, "Cajero");

        Assert.Equal(1, llamadas);
        Assert.IsType<PaymentOutcome.AlreadyPaid>(trasReinicio);
    }

    [Fact]
    public async Task Un_error_de_red_deja_el_abono_EN_DUDA_y_bloquea_el_reintento()
    {
        var idem = new FakePaymentStore();
        var service = Build(idem, OnPagar: (_, _, _) =>
            new ApiResult<Payment>.Failure<Payment>(
                new ApiError.Network(new TimeoutException("se corto"))));

        var primero = await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        Assert.IsType<PaymentOutcome.NetworkUncertain>(primero);

        // El intento queda Pending: NO se cierra como fallido, porque el cobro
        // pudo haberse aplicado.
        Assert.Equal(PaymentAttemptStatus.Pending, idem.History[^1].Status);

        // Y el siguiente intento se bloquea en vez de cobrar a ciegas.
        var segundo = await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        Assert.IsType<PaymentOutcome.InDoubt>(segundo);
    }

    [Fact]
    public async Task Un_rechazo_de_NEGOCIO_permite_reintentar()
    {
        // Si Credinet dijo "monto fuera de rango", NO cobró: bloquear al cajero
        // sería un falso positivo que le impide operar.
        var idem = new FakePaymentStore();
        var llamadas = 0;
        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Failure<Payment>(
                new ApiError.Business(231, "Monto fuera de rango"));
        });

        Assert.IsType<PaymentOutcome.Failure>(
            await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero"));
        Assert.Equal(PaymentAttemptStatus.Failed, idem.History[^1].Status);

        Assert.IsType<PaymentOutcome.Failure>(
            await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero"));
        Assert.Equal(2, llamadas);   // se permitió reintentar
    }

    // ------------------------------------------------------------------
    // Que la barrera no sea excesiva: cobros legítimos deben pasar
    // ------------------------------------------------------------------

    [Fact]
    public async Task Un_abono_de_MONTO_distinto_al_mismo_credito_si_se_cobra()
    {
        var idem = new FakePaymentStore();
        var llamadas = 0;
        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment($"pay-{llamadas}", llamadas));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        var segundo = await service.PagarCreditoAsync("credit-1", 150_000m, "Cajero");

        Assert.Equal(2, llamadas);
        Assert.IsType<PaymentOutcome.Ok>(segundo);
    }

    [Fact]
    public async Task Un_abono_a_OTRO_credito_si_se_cobra()
    {
        var idem = new FakePaymentStore();
        var llamadas = 0;
        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment($"pay-{llamadas}", llamadas));
        });

        await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        Assert.IsType<PaymentOutcome.Ok>(
            await service.PagarCreditoAsync("credit-2", 200_000m, "Cajero"));
        Assert.Equal(2, llamadas);
    }

    [Fact]
    public async Task Fuera_de_la_ventana_de_idempotencia_se_cobra_normal()
    {
        // Un cliente puede abonar el mismo monto al mismo crédito la semana
        // siguiente: la barrera no debe ser eterna.
        var idem = new FakePaymentStore();
        var viejo = DateTime.UtcNow - SistecreditoService.PaymentIdempotencyWindow - TimeSpan.FromMinutes(5);
        await idem.SavePaymentAsync(new CachedPayment(
            PaymentKey: "viejo", CreditId: "credit-1",
            AmountCents: Money.ToCents(200_000m),
            Status: PaymentAttemptStatus.Completed,
            PaymentId: "pay-old", PaymentNumber: 1, PaymentJson: "{}",
            CreatedAt: viejo, CompletedAt: viejo));

        var llamadas = 0;
        var service = Build(idem, OnPagar: (_, _, _) =>
        {
            llamadas++;
            return new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-new", 2));
        });

        Assert.IsType<PaymentOutcome.Ok>(
            await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero"));
        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task Si_el_store_local_falla_el_recaudo_NO_se_bloquea()
    {
        // Un problema de la BD local no puede impedir cobrar: se pierde la barrera
        // local (queda registrado en el log) pero la operación sigue.
        var service = Build(new ThrowingPaymentStore(), OnPagar: (_, _, _) =>
            new ApiResult<Payment>.Ok<Payment>(MakePayment("pay-1", 1)));

        Assert.IsType<PaymentOutcome.Ok>(
            await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero"));
    }

    [Fact]
    public async Task Resolver_un_abono_en_duda_lo_cierra()
    {
        var idem = new FakePaymentStore();
        var service = Build(idem, OnPagar: (_, _, _) =>
            new ApiResult<Payment>.Failure<Payment>(new ApiError.Network(new TimeoutException())));

        await service.PagarCreditoAsync("credit-1", 200_000m, "Cajero");
        var pendiente = idem.History[^1];

        await service.ResolverAbonoEnDudaAsync(pendiente, fuePagado: false);

        Assert.Equal(PaymentAttemptStatus.Failed, idem.History[^1].Status);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static SistecreditoService Build(
        IIdempotencyStore idem,
        Func<string, double, string, ApiResult<Payment>> OnPagar) =>
        new(new StubRepo { OnPagar = OnPagar },
            new NoOpAudit(),
            idem,
            new StubState(),
            new ApiConfig { SubscriptionKey = "test", BaseUrl = "https://api.test/" });

    private static Payment MakePayment(string paymentId, int number) => new(
        TypeDocument: "CC", IdDocument: "123", CreditId: "credit-1",
        PaymentId: paymentId, PaymentNumber: number,
        CreditValuePaid: 200_000, InterestValuePaid: 0, ArrearsValuePaid: 0,
        AssuranceValuePaid: 0, ChargeValuePaid: 0, Balance: 1_300_000,
        NextDueDate: "2026-08-28", NextMinimumPayment: 145_000);

    private sealed class FakePaymentStore : IIdempotencyStore
    {
        private readonly Dictionary<string, CachedPayment> _payments = new();
        public List<CachedPayment> History { get; } = new();

        public Task<CachedPayment?> FindRecentPaymentAsync(
            string creditId, long amountCents, TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            return Task.FromResult(_payments.Values
                .Where(p => p.CreditId == creditId && p.AmountCents == amountCents && p.CreatedAt > cutoff)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault());
        }

        public Task SavePaymentAsync(CachedPayment payment)
        {
            _payments[payment.PaymentKey] = payment;
            History.Add(payment);
            return Task.CompletedTask;
        }

        public Task<CachedTransaction?> FindBySaleIdAsync(string saleId) =>
            Task.FromResult<CachedTransaction?>(null);
        public Task SaveAsync(CachedTransaction transaction) => Task.CompletedTask;
        public Task PurgeOlderThanAsync(TimeSpan retention) => Task.CompletedTask;
    }

    private sealed class ThrowingPaymentStore : IIdempotencyStore
    {
        public Task<CachedPayment?> FindRecentPaymentAsync(string c, long a, TimeSpan w) =>
            throw new InvalidOperationException("BD local caida");
        public Task SavePaymentAsync(CachedPayment p) =>
            throw new InvalidOperationException("BD local caida");
        public Task<CachedTransaction?> FindBySaleIdAsync(string s) =>
            Task.FromResult<CachedTransaction?>(null);
        public Task SaveAsync(CachedTransaction t) => Task.CompletedTask;
        public Task PurgeOlderThanAsync(TimeSpan r) => Task.CompletedTask;
    }

    private sealed class NoOpAudit : IAuditLogger
    {
        public void Log(string action, string comment, string? documentId = null) { }
    }

    private sealed class StubState : ITransactionStateStore
    {
        public HioposTransaction? ActiveTransaction => null;
        public SaleDocument? ActiveDocument => null;
        public Client? ValidatedClient => null;
        public ActiveCredit? SelectedCredit => null;
        public Credit? CreatedCredit => null;
        public Payment? LastPayment => null;
        public decimal CreditValue { get; set; }
        public int Months { get; set; }
        public bool HioposTransactionActive { get; set; }
        public string? LastPaymentAttemptCreditId { get; set; }
        public DateTime? LastPaymentAttemptAt { get; set; }
        public void SetActiveTransaction(HioposTransaction tx) { }
        public void SetActiveDocument(SaleDocument doc) { }
        public void SetValidatedClient(Client client) { }
        public void SetSelectedCredit(ActiveCredit credit) { }
        public void SetCreatedCredit(Credit credit) { }
        public void SetLastPayment(Payment payment) { }
        public void Clear() { }
    }

    private sealed class StubRepo : ICredinetRepository
    {
        public Func<string, double, string, ApiResult<Payment>>? OnPagar { get; set; }

        public Task<ApiResult<Payment>> PagarCreditoAsync(string c, double v, string u) =>
            Task.FromResult(OnPagar!(c, v, u));

        public Task<ApiResult<Client>> GetCreditLimitClientAsync(string t, string i) =>
            throw new NotSupportedException();
        public Task<ApiResult<CreditDetails>> GetCreditDetailsAsync(double c, int f, int m, string t, string i) =>
            throw new NotSupportedException();
        public Task<ApiResult<CreditToken>> SolicitarClaveDinamicaAsync(double c, int f, int m, string t, string i, int? d) =>
            throw new NotSupportedException();
        public Task<ApiResult<Credit>> CrearCreditoAsync(double c, int f, int m, string t, string i, string tok, string s, int a, string? invoice = null, string? seller = null, string? products = null) =>
            throw new NotSupportedException();
        public Task<ApiResult<List<ActiveCredit>>> GetActiveCreditsAsync(string t, string i) =>
            throw new NotSupportedException();
        public Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(double c) =>
            throw new NotSupportedException();
    }
}
