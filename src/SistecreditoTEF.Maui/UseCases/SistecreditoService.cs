using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using AuditActions = SistecreditoTEF.Maui.Services.Platform.AuditActions;

namespace SistecreditoTEF.Maui.UseCases;

/// <summary>
/// Fachada de dominio para operaciones Sistecredito.
///
/// V10 (antes 7 UseCases anemicas): este unico servicio reemplaza
/// las 7 clases passthrough y agrega las preocupaciones transversales:
///   - Auditoria (B9): llama a [IAuditLogger] en cada operacion clave.
///   - Idempotencia (B8): chequea [IIdempotencyStore] antes de crear credito.
///
/// DRY: las 7 acciones de negocio viven en un solo archivo, faciles
/// de leer y mockear.
///
/// SOLID-SRP: delega I/O al repo, persistencia a IIdempotencyStore,
/// auditoria a IAuditLogger.
/// </summary>
public class SistecreditoService
{
    private readonly ICredinetRepository _repo;
    private readonly IAuditLogger _audit;
    private readonly IIdempotencyStore _idempotency;
    private readonly ITransactionStateStore _state;
    private readonly ApiConfig _config;

    public SistecreditoService(
        ICredinetRepository repo,
        IAuditLogger audit,
        IIdempotencyStore idempotency,
        ITransactionStateStore state,
        ApiConfig config)
    {
        _repo = repo;
        _audit = audit;
        _idempotency = idempotency;
        _state = state;
        _config = config;
    }

    // ------------------------------------------------------------------
    // Validar cliente
    // ------------------------------------------------------------------

public async Task<ApiResult<Client>> ValidarClienteAsync(
        DocumentType typeDocument, string idDocument)
    {
        var result = await _repo.GetCreditLimitClientAsync(typeDocument.Code(), idDocument);

        switch (result)
        {
            case ApiResult<Client>.Ok<Client> ok:
                // HU8-973: getCreditLimitClient NO devuelve idDocument/typeDocument
                // en su respuesta, asi que Client.DocumentId queda vacio. Los
                // inyectamos desde la entrada para que el resto del flujo
                // (getCreditDetails, getCreditToken, create) pueda enviarlos;
                // sin esto, getCreditDetails responde 223 RequestValuesInvalid.
                var cliente = ok.Data with { DocumentId = idDocument, DocumentType = typeDocument };
                _audit.Log(
                    AuditActions.ValidateOk,
                    $"cedula={cliente.DocumentId}, disponibles=${cliente.AvailableCreditLimit:N0}");
                return new ApiResult<Client>.Ok<Client>(cliente);
            case ApiResult<Client>.Failure<Client> f:
                _audit.Log(
                    AuditActions.ValidateFail,
                    $"errorCode={(f.Cause as ApiError.Business)?.Code}, msg={f.Cause.UserMessage}");
                break;
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Calcular cuota
    // ------------------------------------------------------------------

    public Task<ApiResult<CreditDetails>> CalcularCuotaAsync(
        double creditValue, int months,
        DocumentType typeDocument, string idDocument) =>
        _repo.GetCreditDetailsAsync(
            creditValue, frequency: _config.Frequency, months,
            typeDocument.Code(), idDocument);

    // ------------------------------------------------------------------
    // Simular (cuota estimada antes de crear)
    // ------------------------------------------------------------------

    public async Task<ApiResult<CreditDetails>> SimularAsync(
        double creditValue, int months,
        DocumentType typeDocument, string idDocument)
    {
        var result = await _repo.GetCreditDetailsAsync(
            creditValue, frequency: _config.Frequency, months,
            typeDocument.Code(), idDocument);

        if (result is ApiResult<CreditDetails>.Ok<CreditDetails> ok)
        {
            _audit.Log(
                AuditActions.Simulate,
                $"meses={months}, cuota=${ok.Data.TotalFeeValue:N0}");
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Solicitar clave dinamica (OTP)
    // ------------------------------------------------------------------

    public async Task<ApiResult<CreditToken>> SolicitarClaveAsync(
        double creditValue, int months,
        DocumentType typeDocument, string idDocument, int? destination = null)
    {
        var result = await _repo.SolicitarClaveDinamicaAsync(
            creditValue, frequency: _config.Frequency, months,
            typeDocument.Code(), idDocument, destination);

        if (result is ApiResult<CreditToken>.Ok<CreditToken> ok)
            _audit.Log(
                AuditActions.TokenRequest,
                $"destino={(destination == 1 ? "WhatsApp" : "SMS")}");

        return result;
    }

    // ------------------------------------------------------------------
    // Crear credito (con idempotencia, B8)
    // ------------------------------------------------------------------

    /// <summary>
    /// Crea el credito en Sistecredito. Antes de pegarle a la API,
    /// consulta [IIdempotencyStore] por SaleId: si ya existe, devuelve
    /// el credito cacheado (mismo CreditId) sin re-crearlo (evita
    /// error 252 DuplicatedCredit).
    /// </summary>
    public async Task<ApiResult<Credit>> CrearCreditoAsync(
        string saleId, DocumentType typeDocument, string idDocument,
        double creditValue, int months, string token)
    {
        // B8: idempotencia por SaleId
        var cached = !string.IsNullOrEmpty(saleId)
            ? await _idempotency.FindBySaleIdAsync(saleId)
            : null;
        if (cached is not null)
        {
            AppLogger.I("SistecreditoService",
                $"Idempotencia hit: SaleId={saleId} -> CreditId={cached.CreditId}");
            return new ApiResult<Credit>.Ok<Credit>(new Credit(
                TypeDocument:        typeDocument.Code(),
                IdDocument:          idDocument,
                CreditId:            cached.CreditId,
                CreditNumber:        cached.CreditNumber,
                EffectiveAnnualRate: 0.0,
                DownPayment:         0.0,
                TotalFeeValue:       0.0,
                CreditValue:         creditValue,
                Fees:                months,
                AssuranceValue:      0.0,
                InterestRate:        0.0,
                TotalInterestValue:  0.0,
                TotalDownPayment:    0.0,
                FeeCreditValue:      0.0,
                AssuranceFeeValue:   0.0,
                AssuranceTotalValue: 0.0,
                AssuranceTaxFeeValue: 0.0));
        }

        // HU8-973: enviar invoice=SaleId (idempotencia del lado CREDINET, evita
        // 252 DuplicatedCredit y sirve de conciliacion), + seller y products
        // descriptivos que exige el manual del POST /create.
        var seller = ExtractSellerName(_state.ActiveTransaction?.SellerData);
        var products = _state.ActiveDocument is { } doc
            ? Truncate(string.Join(", ", doc.ProductDescriptions), 200)
            : null;

        var result = await _repo.CrearCreditoAsync(
            creditValue, frequency: _config.Frequency, months,
            typeDocument.Code(), idDocument,
            token, source: _config.Source, authMethod: _config.AuthMethod,
            invoice: string.IsNullOrEmpty(saleId) ? null : saleId,
            seller: seller,
            products: products);

        switch (result)
        {
            case ApiResult<Credit>.Ok<Credit> ok:
                if (!string.IsNullOrEmpty(saleId))
                    await _idempotency.SaveAsync(new CachedTransaction(
                        SaleId:             saleId,
                        CreditId:           ok.Data.CreditId,
                        CreditNumber:       ok.Data.CreditNumber,
                        TransactionData:    ok.Data.CreditId,
                        AuthorizationId:    ok.Data.CreditId,
                        CardHolder:         "",
                        CardNum:            "",
                        MerchantReceiptXml: "",
                        CustomerReceiptXml: "",
                        CreatedAt:          DateTime.UtcNow));
                _audit.Log(
                    AuditActions.CreditCreated,
                    $"creditId={ok.Data.CreditId}, monto=${ok.Data.CreditValue:N0}",
                    documentId: saleId);
                break;
            case ApiResult<Credit>.Failure<Credit> f:
                _audit.Log(
                    AuditActions.CreditFail,
                    $"errorCode={(f.Cause as ApiError.Business)?.Code}, msg={f.Cause.UserMessage}",
                    documentId: saleId);
                break;
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Listar creditos activos (para flujo Pago)
    // ------------------------------------------------------------------

    public Task<ApiResult<List<ActiveCredit>>> ObtenerCreditosActivosAsync(
        DocumentType typeDocument, string idDocument) =>
        _repo.GetActiveCreditsAsync(typeDocument.Code(), idDocument);

    // ------------------------------------------------------------------
    // Pagar credito
    // ------------------------------------------------------------------

    public async Task<ApiResult<Payment>> PagarCreditoAsync(
        string creditId, double totalValuePaid, string userName)
    {
        var result = await _repo.PagarCreditoAsync(creditId, totalValuePaid, userName);

        switch (result)
        {
            case ApiResult<Payment>.Ok<Payment> ok:
                _audit.Log(
                    AuditActions.Payment,
                    $"creditId={creditId}, monto=${ok.Data.CreditValuePaid:N0}");
                break;
            case ApiResult<Payment>.Failure<Payment> f:
                _audit.Log(
                    AuditActions.PaymentFail,
                    $"creditId={creditId}, errorCode={(f.Cause as ApiError.Business)?.Code}, msg={f.Cause.UserMessage}");
                break;
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Limite de meses
    // ------------------------------------------------------------------

    public Task<ApiResult<SimulatedMonthLimit>> ObtenerLimiteMesesAsync(double creditValue) =>
        _repo.GetSimulatedMonthLimitAsync(creditValue);

    // ------------------------------------------------------------------
    // Helpers (HU8-973): seller/products para el POST /create
    // ------------------------------------------------------------------

    private static string? ExtractSellerName(string? sellerData)
    {
        if (string.IsNullOrWhiteSpace(sellerData)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            sellerData, @"<name>([^<]+)</name>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= max ? value : value[..max];
    }
}


