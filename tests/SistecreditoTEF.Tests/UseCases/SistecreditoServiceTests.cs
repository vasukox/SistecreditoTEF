using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;
using Xunit;
using HioposTransaction = SistecreditoTEF.Maui.Services.Hiopos.HioposTransaction;

namespace SistecreditoTEF.Maui.Tests.UseCases;

/// <summary>
/// Tests del SistecreditoService (antes 7 UseCases anemicas - V10).
/// Cubre audit log (B9) e idempotencia (B8).
/// </summary>
public class SistecreditoServiceTests
{
    [Fact]
    public async Task ValidarCliente_audit_log_OK_cuando_response_es_Ok()
    {
        var captured = new List<(string action, string comment)>();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetCreditLimitClient = (_, _) => new ApiResult<Client>.Ok<Client>(
                    MakeClient("1234567890", availableCredit: 5_000_000))
            },
            audit: new FakeAudit { OnLog = (a, c, _) => captured.Add((a, c)) },
            idem: new FakeIdempotencyStore());

        var result = await service.ValidarClienteAsync(DocumentType.CedulaCiudadania, "1234567890");

        Assert.IsType<ApiResult<Client>.Ok<Client>>(result);
        var audit = Assert.Single(captured);
        Assert.Equal(AuditActions.ValidateOk, audit.action);
        Assert.Contains("1234567890", audit.comment);
    }

    [Fact]
    public async Task ValidarCliente_audit_log_FAIL_cuando_response_es_Failure()
    {
        var captured = new List<(string action, string comment)>();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetCreditLimitClient = (_, _) => new ApiResult<Client>.Failure<Client>(
                    new ApiError.Business(221, "No tiene cupo"))
            },
            audit: new FakeAudit { OnLog = (a, c, _) => captured.Add((a, c)) },
            idem: new FakeIdempotencyStore());

        var result = await service.ValidarClienteAsync(DocumentType.CedulaCiudadania, "x");

        Assert.IsType<ApiResult<Client>.Failure<Client>>(result);
        var audit = Assert.Single(captured);
        Assert.Equal(AuditActions.ValidateFail, audit.action);
        Assert.Contains("221", audit.comment);
    }

    [Fact]
    public async Task CrearCredito_devuelve_cache_si_saleId_ya_existe()
    {
        var idem = new FakeIdempotencyStore();
        await idem.SaveAsync(new CachedTransaction(
            SaleId: "sale-1", CreditId: "credit-cached",
            CreditNumber: 42, TransactionData: "td", AuthorizationId: "auth",
            CardHolder: "", CardNum: "",
            MerchantReceiptXml: "", CustomerReceiptXml: "", CreatedAt: DateTime.UtcNow));

        var service = BuildService(new FakeRepo(), new FakeAudit(), idem);

        var result = await service.CrearCreditoAsync(
            saleId: "sale-1", DocumentType.CedulaCiudadania, "123", 500_000, 12, "tok");

        var ok = Assert.IsType<ApiResult<Credit>.Ok<Credit>>(result);
        Assert.Equal("credit-cached", ok.Data.CreditId);
    }

    [Fact]
    public async Task CrearCredito_persiste_en_cache_despues_de_crear()
    {
        var idem = new FakeIdempotencyStore();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnCrear = (_, _, _, _, _, _, _, _) => new ApiResult<Credit>.Ok<Credit>(
                    MakeCredit("credit-new", 7))
            },
            audit: new FakeAudit(),
            idem: idem);

        await service.CrearCreditoAsync(
            saleId: "sale-1", DocumentType.CedulaCiudadania, "123", 500_000, 12, "tok");

        var cached = await idem.FindBySaleIdAsync("sale-1");
        Assert.NotNull(cached);
        Assert.Equal("credit-new", cached!.CreditId);
    }

    // ---- helpers ----

    private static SistecreditoService BuildService(
        ICredinetRepository repo, IAuditLogger audit, IIdempotencyStore idem,
        ITransactionStateStore? state = null) =>
        new(repo, audit, idem, state ?? new FakeStateStore(),
            new ApiConfig { SubscriptionKey = "test", BaseUrl = "https://api.test/" });

    private static Client MakeClient(string id, double availableCredit) => new(
        DocumentType.CedulaCiudadania, id, CreditLimit: 5_000_000,
        AvailableCreditLimit: availableCredit, ValidatedMail: false,
        NewCreditButtonEnabled: true, Email: "", Mobile: "", FullName: "Stub",
        Defaulter: false, CreditLimitIncrease: false,
        IsAvailableCreditLimit: true, IsActive: true, Status: 1,
        StatusName: "OK", FirstName: "Stub", SecondName: "");

    private static Credit MakeCredit(string creditId, int number) => new(
        TypeDocument: "CC", IdDocument: "x", CreditId: creditId, CreditNumber: number,
        EffectiveAnnualRate: 28.5, DownPayment: 100_000, TotalFeeValue: 150_000,
        CreditValue: 500_000, Fees: 12, AssuranceValue: 10_000,
        InterestRate: 5, TotalInterestValue: 50_000, TotalDownPayment: 100_000,
        FeeCreditValue: 150_000, AssuranceFeeValue: 5_000,
        AssuranceTotalValue: 10_000, AssuranceTaxFeeValue: 1_900);

    // ---- fakes ----

    private sealed class FakeRepo : ICredinetRepository
    {
        public Func<string, string, ApiResult<Client>>? OnGetCreditLimitClient { get; set; }
        public Func<double, int, int, string, string, ApiResult<CreditDetails>>? OnGetCreditDetails { get; set; }
        public Func<double, int, int, string, string, int?, ApiResult<CreditToken>>? OnSolicitarClave { get; set; }
        public Func<double, int, int, string, string, string, string, int, ApiResult<Credit>>? OnCrear { get; set; }
        public Func<string, string, ApiResult<List<ActiveCredit>>>? OnGetActiveCredits { get; set; }
        public Func<string, double, string, ApiResult<Payment>>? OnPagar { get; set; }
        public Func<double, ApiResult<SimulatedMonthLimit>>? OnGetSimulatedMonthLimit { get; set; }

        public Task<ApiResult<Client>> GetCreditLimitClientAsync(string t, string i)
            => Task.FromResult(OnGetCreditLimitClient!(t, i));
        public Task<ApiResult<CreditDetails>> GetCreditDetailsAsync(double c, int f, int m, string t, string i)
            => Task.FromResult(OnGetCreditDetails!(c, f, m, t, i));
        public Task<ApiResult<CreditToken>> SolicitarClaveDinamicaAsync(double c, int f, int m, string t, string i, int? d)
            => Task.FromResult(OnSolicitarClave!(c, f, m, t, i, d));
        public Task<ApiResult<Credit>> CrearCreditoAsync(double c, int f, int m, string t, string i, string tok, string s, int a, string? invoice = null, string? seller = null, string? products = null)
            => Task.FromResult(OnCrear!(c, f, m, t, i, tok, s, a));
        public Task<ApiResult<List<ActiveCredit>>> GetActiveCreditsAsync(string t, string i)
            => Task.FromResult(OnGetActiveCredits!(t, i));
        public Task<ApiResult<Payment>> PagarCreditoAsync(string c, double v, string u)
            => Task.FromResult(OnPagar!(c, v, u));
        public Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(double c)
            => Task.FromResult(OnGetSimulatedMonthLimit!(c));
    }

    private sealed class FakeAudit : IAuditLogger
    {
        public Action<string, string, string?>? OnLog { get; set; }
        public void Log(string action, string comment, string? documentId = null)
            => OnLog?.Invoke(action, comment, documentId);
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, CachedTransaction> _store = new();

        public Task<CachedTransaction?> FindBySaleIdAsync(string saleId)
            => Task.FromResult(_store.TryGetValue(saleId, out var v) ? v : null);

        public Task SaveAsync(CachedTransaction tx)
        {
            _store[tx.SaleId] = tx;
            return Task.CompletedTask;
        }

        public CachedTransaction? FindBySaleId(string saleId)
            => _store.TryGetValue(saleId, out var v) ? v : null;

        public void Save(CachedTransaction tx) => _store[tx.SaleId] = tx;
    }

    private sealed class FakeStateStore : ITransactionStateStore
    {
        public HioposTransaction? ActiveTransaction => null;
        public SaleDocument? ActiveDocument => null;
        public Client? ValidatedClient => null;
        public ActiveCredit? SelectedCredit => null;
        public Credit? CreatedCredit => null;
        public Payment? LastPayment => null;
        public decimal CreditValue { get; set; }
        public int Months { get; set; }
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
}
