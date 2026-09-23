using System.Text.Json;
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
/// Concentra las preocupaciones transversales:
///   - Auditoria: llama a [IAuditLogger] en cada operacion clave.
///   - Idempotencia: creditos por SaleId y abonos por credito+monto.
///
/// SOLID-SRP: delega I/O al repo, persistencia a [IIdempotencyStore], auditoria
/// a [IAuditLogger].
/// </summary>
public class SistecreditoService
{
    /// <summary>
    /// Ventana en la que un abono del mismo crédito y monto se considera un
    /// posible reintento y no un cobro nuevo.
    /// </summary>
    public static readonly TimeSpan PaymentIdempotencyWindow = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
                // getCreditLimitClient NO devuelve idDocument/typeDocument, asi que
                // Client.DocumentId queda vacio. Los inyectamos desde la entrada
                // para que el resto del flujo pueda enviarlos; sin esto,
                // getCreditDetails responde 223 RequestValuesInvalid.
                var cliente = ok.Data with { DocumentId = idDocument, DocumentType = typeDocument };
                // QA A-2: cedula ENMASCARADA. Este comment viaja al POS por
                // broadcast (fuera del sandbox de la app) y se persiste en la BD.
                _audit.Log(
                    AuditActions.ValidateOk,
                    $"cedula={PiiMask.Document(cliente.DocumentId)}, " +
                    $"disponibles=${cliente.AvailableCreditLimit:N0}");
                return new ApiResult<Client>.Ok<Client>(cliente);
            case ApiResult<Client>.Failure<Client> f:
                _audit.Log(
                    AuditActions.ValidateFail,
                    $"cedula={PiiMask.Document(idDocument)}, " +
                    $"errorCode={(f.Cause as ApiError.Business)?.Code}, msg={f.Cause.UserMessage}");
                break;
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Calcular cuota / simular
    // ------------------------------------------------------------------

    public Task<ApiResult<CreditDetails>> CalcularCuotaAsync(
        double creditValue, int months,
        DocumentType typeDocument, string idDocument) =>
        _repo.GetCreditDetailsAsync(
            creditValue, frequency: _config.Frequency, months,
            typeDocument.Code(), idDocument);

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

        if (result is ApiResult<CreditToken>.Ok<CreditToken>)
            _audit.Log(
                AuditActions.TokenRequest,
                $"destino={DescribeOtpChannel(destination ?? _config.OtpDestination)}");

        return result;
    }

    /// <summary>
    /// QA M-15: el canal se describe desde el valor real de configuración.
    /// 1 = WhatsApp (el confirmado por Sistecrédito para test y producción).
    /// </summary>
    public static string DescribeOtpChannel(int destination) =>
        destination == 1 ? "WhatsApp" : "SMS";

    // ------------------------------------------------------------------
    // Crear credito (con idempotencia)
    // ------------------------------------------------------------------

    /// <summary>
    /// Crea el credito en Sistecredito. Antes de pegarle a la API consulta
    /// [IIdempotencyStore] por SaleId: si ya existe, devuelve el credito
    /// cacheado sin re-crearlo (evita el error 252 DuplicatedCredit).
    ///
    /// QA C-4: el replay ahora devuelve el credito COMPLETO. Antes reconstruia un
    /// [Credit] con todos los importes en 0.0, y esos campos alimentan
    /// directamente el voucher: el cliente se llevaba un comprobante con cuota
    /// mensual $0, cuota inicial $0 y Tasa E.A. 0,00% para un credito real.
    /// </summary>
    public async Task<ApiResult<Credit>> CrearCreditoAsync(
        string saleId, DocumentType typeDocument, string idDocument,
        double creditValue, int months, string token)
    {
        var cached = !string.IsNullOrEmpty(saleId)
            ? await _idempotency.FindBySaleIdAsync(saleId)
            : null;

        if (cached is not null)
        {
            var replay = RebuildCredit(cached, typeDocument, idDocument);
            if (replay is not null)
            {
                AppLogger.I("SistecreditoService",
                    $"Idempotencia hit: SaleId={saleId} -> CreditId={cached.CreditId} " +
                    "(credito completo recuperado del cache).");
                _audit.Log(AuditActions.CreditCreated,
                    $"REPLAY creditId={cached.CreditId} (idempotencia)", documentId: saleId);
                return new ApiResult<Credit>.Ok<Credit>(replay);
            }

            // Cache de una version anterior, sin datos financieros. Imprimir
            // ceros seria entregar un documento de credito falso, asi que se
            // reporta un fallo accionable en vez de un voucher incorrecto.
            AppLogger.E("SistecreditoService",
                $"Idempotencia hit para SaleId={saleId} pero el cache NO tiene los datos " +
                "financieros del credito (registro de una version anterior). No se puede " +
                "reimprimir el voucher correcto.");
            _audit.Log(AuditActions.CreditFail,
                $"REPLAY incompleto para creditId={cached.CreditId}", documentId: saleId);
            return new ApiResult<Credit>.Failure<Credit>(new ApiError.Local(
                $"El credito {cached.CreditNumber} ya fue creado para esta venta, pero no se " +
                "pueden recuperar sus datos para el comprobante. Consultalo en Sistecredito " +
                "antes de reintentar."));
        }

        // invoice=SaleId da idempotencia del lado CREDINET (evita 252
        // DuplicatedCredit y sirve de conciliacion).
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
                    await SaveCreditForReplayAsync(saleId, ok.Data);
                _audit.Log(
                    AuditActions.CreditCreated,
                    $"creditId={ok.Data.CreditId}, monto=${ok.Data.CreditValue:N0}, " +
                    $"cedula={PiiMask.Document(idDocument)}",
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

    /// <summary>
    /// Persiste el credito COMPLETO para poder reimprimir el voucher si HioPos
    /// reintenta la misma venta (QA C-4).
    /// </summary>
    private async Task SaveCreditForReplayAsync(string saleId, Credit credit)
    {
        try
        {
            await _idempotency.SaveAsync(new CachedTransaction(
                SaleId:             saleId,
                CreditId:           credit.CreditId,
                CreditNumber:       credit.CreditNumber,
                TransactionData:    credit.CreditId,
                AuthorizationId:    credit.CreditId,
                CardHolder:         string.Empty,
                CardNum:            string.Empty,
                MerchantReceiptXml: string.Empty,
                CustomerReceiptXml: string.Empty,
                CreatedAt:          DateTime.UtcNow,
                CreditJson:         JsonSerializer.Serialize(credit, CacheJson)));
        }
        catch (Exception ex)
        {
            // No se puede fallar la venta por un problema de cache local: el
            // credito YA existe en Credinet. Se pierde la barrera local, pero
            // queda la remota (invoice=SaleId).
            AppLogger.E("SistecreditoService",
                $"No se pudo cachear el credito para idempotencia (SaleId={saleId}).", ex);
        }
    }

    /// <summary>
    /// Reconstruye el [Credit] desde el cache. null si el registro no trae los
    /// datos financieros (cache de una version anterior).
    /// </summary>
    private static Credit? RebuildCredit(
        CachedTransaction cached, DocumentType typeDocument, string idDocument)
    {
        if (string.IsNullOrWhiteSpace(cached.CreditJson)) return null;

        try
        {
            var credit = JsonSerializer.Deserialize<Credit>(cached.CreditJson, CacheJson);
            if (credit is null) return null;

            // Se reafirman los datos del titular por si el cache viene de otro
            // intento con la misma venta.
            return credit with
            {
                TypeDocument = typeDocument.Code(),
                IdDocument = idDocument
            };
        }
        catch (JsonException ex)
        {
            AppLogger.E("SistecreditoService",
                "El credito cacheado no se pudo deserializar.", ex);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Listar creditos activos (flujo de abono)
    // ------------------------------------------------------------------

    public Task<ApiResult<List<ActiveCredit>>> ObtenerCreditosActivosAsync(
        DocumentType typeDocument, string idDocument) =>
        _repo.GetActiveCreditsAsync(typeDocument.Code(), idDocument);

    // ------------------------------------------------------------------
    // Pagar credito (abono) — con idempotencia persistente (QA C-5)
    // ------------------------------------------------------------------

    /// <summary>
    /// Registra un abono, protegido contra doble cobro incluso si el proceso
    /// muere entre el envio y la confirmacion.
    ///
    /// Secuencia:
    ///   1. Busca un intento reciente para el mismo credito y monto.
    ///      - Completado -> [PaymentOutcome.AlreadyPaid] (se reimprime, no se cobra).
    ///      - Pendiente  -> [PaymentOutcome.InDoubt] (hay que verificar, no reintentar).
    ///   2. Marca el intento como Pendiente ANTES de llamar a Credinet.
    ///   3. Segun la respuesta lo cierra como Completado o Fallido; si hubo error
    ///      de red lo DEJA pendiente (estado en duda).
    /// </summary>
    public async Task<PaymentOutcome> PagarCreditoAsync(
        string creditId, decimal amount, string userName)
    {
        // ─────────────────────────────────────────────────────────────────────
        // BARRERA DE userName
        // ─────────────────────────────────────────────────────────────────────
        // Credinet exige userName en payCredit y rechaza la peticion completa con
        // HTTP 400 "[REP-E-003] El campo UserName es obligatorio" si llega vacio.
        // Eso ya dejo la caja SIN PODER COBRAR ningun abono, y el sintoma —un 400
        // del proveedor— apuntaba a Credinet en lugar de a nosotros.
        //
        // El llamador ya arma el nombre con sus respaldos; esto es la red de
        // seguridad para cualquier llamador futuro. Se REEMPLAZA en vez de abortar
        // a proposito: dejar a un cajero sin poder recaudar por un defecto nuestro
        // de nombres es peor que registrar el abono con un rotulo generico. Queda
        // logueado como error y en la auditoria para que se pueda encontrar.
        if (string.IsNullOrWhiteSpace(userName))
        {
            AppLogger.E("SistecreditoService",
                $"Abono para creditId={creditId} sin userName: Credinet rechazaria la " +
                "peticion. Se usa un rotulo generico; hay un defecto en quien llama.");
            _audit.Log(AuditActions.PaymentFail,
                $"userName vacio en el abono de creditId={creditId}; se sustituyo por respaldo");
            userName = "Cajero Permoda";
        }
        else
        {
            userName = userName.Trim();
        }

        var amountCents = Money.ToCents(amount);

        var previous = await FindRecentPaymentSafeAsync(creditId, amountCents);
        if (previous is not null)
        {
            switch (previous.Status)
            {
                case PaymentAttemptStatus.Completed:
                    var cachedPayment = DeserializePayment(previous.PaymentJson);
                    if (cachedPayment is not null)
                    {
                        AppLogger.W("SistecreditoService",
                            $"Abono ya registrado para creditId={creditId} " +
                            $"(paymentId={previous.PaymentId}); no se vuelve a cobrar.");
                        _audit.Log(AuditActions.Payment,
                            $"REPLAY abono paymentId={previous.PaymentId} (idempotencia)");
                        return new PaymentOutcome.AlreadyPaid(cachedPayment);
                    }

                    // ─────────────────────────────────────────────────────────
                    // COMPLETED PERO SIN PODER RECONSTRUIR EL COMPROBANTE
                    // ─────────────────────────────────────────────────────────
                    // Antes esto hacia `break` y la ejecucion seguia hasta el POST:
                    // un abono MARCADO COMO COBRADO se volvia a cobrar solo porque
                    // no se pudo deserializar su JSON. Pasa cuando el cache lo
                    // escribio una version anterior de la app con otro formato de
                    // Payment: justo despues de una actualizacion, que es cuando
                    // menos se lo espera.
                    //
                    // El estado Completed es la evidencia de que Credinet YA cobro.
                    // No poder rearmar el comprobante es un problema de
                    // presentacion; cobrar de nuevo es plata del cliente. Se
                    // devuelve InDoubt para que el cajero verifique en vez de
                    // recobrar.
                    AppLogger.E("SistecreditoService",
                        $"Abono COMPLETADO para creditId={creditId} " +
                        $"(paymentId={previous.PaymentId}) pero no se pudo leer su " +
                        "comprobante guardado. NO se vuelve a cobrar.");
                    _audit.Log(AuditActions.PaymentFail,
                        $"abono completado ilegible paymentId={previous.PaymentId}, " +
                        "se bloquea el recobro");
                    return new PaymentOutcome.InDoubt(previous);

                case PaymentAttemptStatus.Pending:
                    AppLogger.E("SistecreditoService",
                        $"Existe un abono EN DUDA para creditId={creditId} " +
                        $"(key={previous.PaymentKey}). No se reintenta a ciegas.");
                    _audit.Log(AuditActions.PaymentFail,
                        $"abono en duda creditId={creditId}, se bloquea el reintento");
                    return new PaymentOutcome.InDoubt(previous);
            }
        }

        // Clave estable del intento: se persiste antes del POST para que un
        // reinicio del POS no borre la evidencia de que ya se envio.
        var key = $"{creditId}|{amountCents}|{DateTime.UtcNow:yyyyMMddHHmmss}";
        var attempt = new CachedPayment(
            PaymentKey: key,
            CreditId: creditId,
            AmountCents: amountCents,
            Status: PaymentAttemptStatus.Pending,
            PaymentId: string.Empty,
            PaymentNumber: 0,
            PaymentJson: string.Empty,
            CreatedAt: DateTime.UtcNow,
            CompletedAt: null);

        await SavePaymentSafeAsync(attempt);

        var result = await _repo.PagarCreditoAsync(creditId, (double)amount, userName);

        switch (result)
        {
            case ApiResult<Payment>.Ok<Payment> ok:
                await SavePaymentSafeAsync(attempt with
                {
                    Status = PaymentAttemptStatus.Completed,
                    PaymentId = ok.Data.PaymentId ?? string.Empty,
                    PaymentNumber = ok.Data.PaymentNumber,
                    PaymentJson = JsonSerializer.Serialize(ok.Data, CacheJson),
                    CompletedAt = DateTime.UtcNow
                });
                _audit.Log(
                    AuditActions.Payment,
                    $"creditId={creditId}, monto=${ok.Data.CreditValuePaid:N0}, " +
                    $"paymentId={ok.Data.PaymentId}");
                return new PaymentOutcome.Ok(ok.Data);

            case ApiResult<Payment>.Failure<Payment> f:
                // Un rechazo de NEGOCIO significa que Credinet no cobro: el
                // intento se cierra como fallido y el cajero puede reintentar.
                // Un error de RED deja el intento PENDIENTE: no sabemos si cobro.
                var isBusiness = f.Cause is ApiError.Business;
                if (isBusiness)
                {
                    await SavePaymentSafeAsync(attempt with
                    {
                        Status = PaymentAttemptStatus.Failed,
                        CompletedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    AppLogger.E("SistecreditoService",
                        $"Abono con error de red para creditId={creditId}: queda EN DUDA " +
                        $"(key={key}). Hay que verificar el saldo antes de reintentar.");
                }

                _audit.Log(
                    AuditActions.PaymentFail,
                    $"creditId={creditId}, errorCode={(f.Cause as ApiError.Business)?.Code}, " +
                    $"msg={f.Cause.UserMessage}, enDuda={!isBusiness}");

                return isBusiness
                    ? new PaymentOutcome.Failure(f.Cause)
                    : new PaymentOutcome.NetworkUncertain(f.Cause, key);
        }

        return new PaymentOutcome.Failure(new ApiError.Local(
            "No se pudo determinar el resultado del abono. Verifica el saldo antes de reintentar."));
    }

    /// <summary>
    /// Cierra manualmente un abono en duda tras verificar en Credinet.
    /// Lo usa la UI cuando el cajero confirma que el pago sí quedó (o no).
    /// </summary>
    public async Task ResolverAbonoEnDudaAsync(CachedPayment pending, bool fuePagado)
    {
        await SavePaymentSafeAsync(pending with
        {
            Status = fuePagado ? PaymentAttemptStatus.Completed : PaymentAttemptStatus.Failed,
            CompletedAt = DateTime.UtcNow
        });
        _audit.Log(AuditActions.Payment,
            $"abono en duda resuelto manualmente: creditId={pending.CreditId}, pagado={fuePagado}");
    }

    private async Task<CachedPayment?> FindRecentPaymentSafeAsync(string creditId, long amountCents)
    {
        try
        {
            return await _idempotency.FindRecentPaymentAsync(
                creditId, amountCents, PaymentIdempotencyWindow);
        }
        catch (Exception ex)
        {
            // Si el store local falla no se puede bloquear el recaudo, pero hay
            // que dejarlo registrado: en esta operacion no hay barrera local.
            AppLogger.E("SistecreditoService",
                "No se pudo consultar la idempotencia de abonos; se continua SIN barrera local.", ex);
            return null;
        }
    }

    private async Task SavePaymentSafeAsync(CachedPayment payment)
    {
        try
        {
            await _idempotency.SavePaymentAsync(payment);
        }
        catch (Exception ex)
        {
            AppLogger.E("SistecreditoService",
                $"No se pudo persistir el intento de abono (key={payment.PaymentKey}).", ex);
        }
    }

    private static Payment? DeserializePayment(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Payment>(json, CacheJson); }
        catch (JsonException) { return null; }
    }

    // ------------------------------------------------------------------
    // Limite de meses
    // ------------------------------------------------------------------

    public Task<ApiResult<SimulatedMonthLimit>> ObtenerLimiteMesesAsync(double creditValue) =>
        _repo.GetSimulatedMonthLimitAsync(creditValue);

    /// <summary>
    /// errorCode de Credinet para "el numero de meses no es valido para este monto".
    /// Observado en terminal: HTTP 400 con
    /// <c>errorCode=222 message=MonthsNumberNotValid</c>.
    /// </summary>
    public const int ErrorCodeMonthsNumberNotValid = 222;

    /// <summary>
    /// errorCode de Credinet para "el cliente no tiene una oferta de credito que
    /// cubra este monto". Verificado contra el sandbox:
    ///
    ///   getSimulatedMonthLimit?creditValue=110000 -> 200 {"data":{"months":3}}
    ///   getSimulatedMonthLimit?creditValue=120000 -> 400 errorCode=1104 NoOfferAvailable
    /// </summary>
    public const int ErrorCodeNoOfferAvailable = 1104;

    /// <summary>
    /// errorCode con el que <c>getCreditToken</c> rechaza un monto sin oferta.
    /// Es la MISMA causa que [ErrorCodeNoOfferAvailable], reportada con otro nombre
    /// por el endpoint que manda el OTP:
    ///
    ///   getCreditToken?creditValue=219500&amp;months=3 -> errorCode=220 InvalidAmountCredit
    ///
    /// Existe como constante para que quede escrito que 220 y 1104 son lo mismo: si
    /// aparece el 220 en el terminal, la barrera de oferta no corrio o fallo abierta.
    /// </summary>
    public const int ErrorCodeInvalidAmountCredit = 220;

    /// <summary>Un plazo confirmado por Credinet, con su cuota ya calculada.</summary>
    public sealed record PlazoSimulado(int Months, CreditDetails Detalles);

    /// <summary>
    /// Resultado de consultar que plazos acepta Credinet para un monto.
    /// <paramref name="Plazos"/> son los CONFIRMADOS. <paramref name="ErrorTecnico"/>
    /// se llena si algun candidato no se pudo verificar por un fallo tecnico (red,
    /// 500), para poder avisar que la lista puede estar incompleta.
    /// <paramref name="SinOferta"/> es distinto de "ningun plazo sirve": significa
    /// que Credinet no financia ESTE MONTO para este cliente, con ningun plazo.
    /// </summary>
    public sealed record PlazosValidos(
        IReadOnlyList<PlazoSimulado> Plazos, ApiError? ErrorTecnico, bool SinOferta = false);

    /// <summary>
    /// Pregunta a Credinet, plazo por plazo, cuales acepta para este monto.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE NO ALCANZA CON getSimulatedMonthLimit
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Ese endpoint devuelve un numero de meses que NO se puede interpretar como
    /// "todos los valores hasta aca son validos". Capturado en terminal con un
    /// credito de $99.900: la pantalla ofrecia 6, 3 y 2 meses —o sea el limite venia
    /// en 6 o mas— y Credinet rechazo los tres:
    ///
    ///   getCreditDetails?creditValue=99900&amp;months=6 -> errorCode=222 MonthsNumberNotValid
    ///   getCreditDetails?creditValue=99900&amp;months=3 -> errorCode=222 MonthsNumberNotValid
    ///   getCreditDetails?creditValue=99900&amp;months=2 -> errorCode=222 MonthsNumberNotValid
    ///   getCreditDetails?creditValue=99900&amp;months=1 -> OK, cuota $113.672
    ///
    /// Solo 1 mes era valido. El cajero tenia que descubrirlo a fuerza de errores.
    /// La regla real de Credinet parece ser un minimo por cuota, pero no esta
    /// documentada, asi que no se puede calcular del lado del modulo.
    ///
    /// La UNICA autoridad sobre si un plazo es valido es getCreditDetails. Esto le
    /// pregunta por los candidatos en PARALELO y devuelve solo los que acepto, con
    /// la cuota ya calculada —asi elegir un plazo despues es instantaneo, sin la
    /// espera de una segunda llamada—.
    ///
    /// Un 222 NO es un error: es la respuesta "ese plazo no aplica", y ese plazo
    /// simplemente no se ofrece. Un fallo tecnico si se reporta, porque significa
    /// que la lista puede estar incompleta y no hay que hacerla pasar por completa.
    /// </summary>
    public async Task<PlazosValidos> ObtenerPlazosValidosAsync(
        double creditValue, IReadOnlyList<int> candidatos,
        DocumentType typeDocument, string idDocument,
        CancellationToken cancellationToken = default)
    {
        var codigo = typeDocument.Code();

        // ─────────────────────────────────────────────────────────────────────────
        // BARRERA DE OFERTA — VA ANTES QUE TODO
        // ─────────────────────────────────────────────────────────────────────────
        // getCreditDetails es una CALCULADORA: simula cualquier monto sin mirar si
        // el cliente tiene una oferta de credito que lo cubra. getSimulatedMonthLimit
        // y getCreditToken si la miran.
        //
        // Sin esta barrera, el cajero llegaba hasta la pantalla del OTP y ahi
        // reventaba, con el texto crudo de Credinet en ingles:
        //
        //   OtpViewModel: Solicitando OTP: monto=219500, meses=3
        //   OtpViewModel: getCreditToken FAILURE: CREDINET: InvalidAmountCredit
        //
        // Y volvia a fallar con meses=2 y meses=1, porque el plazo nunca fue el
        // problema. Comprobado contra el sandbox con el mismo cliente:
        //
        //   getCreditDetails      219500 / 3 meses -> 200 OK, cuota $84.650
        //   getSimulatedMonthLimit 110000          -> 200 {"months":3}
        //   getSimulatedMonthLimit 120000          -> 400 NoOfferAvailable
        //   getCreditToken         110000          -> 200 token generado
        //   getCreditToken         120000          -> 400 InvalidAmountCredit
        //
        // La calculadora decia que si y la autoridad decia que no. Ahora se pregunta
        // primero a la autoridad, en la pantalla del monto, donde el cajero todavia
        // puede hacer algo.
        //
        // FALLA ABIERTA a proposito ante un error TECNICO: si esta consulta se cae
        // por red, se sigue con la simulacion normal. Bloquear una venta que podria
        // ser valida por un problema de conexion es peor que dejar que el error
        // aparezca mas adelante, donde ya esta traducido.
        var oferta = await _repo.GetSimulatedMonthLimitAsync(creditValue);

        if (oferta is ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit> fallo
            && fallo.Cause is ApiError.Business negocio
            && negocio.Code == ErrorCodeNoOfferAvailable)
        {
            AppLogger.W("SistecreditoService",
                $"Sin oferta de credito para ${creditValue:N0} (NoOfferAvailable). " +
                "No se consultan plazos: ninguno seria valido.");
            _audit.Log(AuditActions.Simulate,
                $"sin oferta de credito para ${creditValue:N0}");

            return new PlazosValidos([], null, SinOferta: true);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PODA POR EL TECHO DE MESES — LA MISMA RESPUESTA YA LO TRAE
        // ─────────────────────────────────────────────────────────────────────────
        // La llamada de arriba, cuando SI hay oferta, devuelve un techo de meses. No
        // se puede usar para armar la lista —ya se comprobo que es demasiado
        // permisivo: para $110.000 dice 3 y getCreditDetails solo acepta 1 y 2— pero
        // en todas las mediciones resulto un LIMITE SUPERIOR valido:
        //
        //     monto      techo    plazos realmente validos
        //      50.000      2       {1}
        //     110.000      3       {1, 2}
        //     119.000      3       {1, 2}
        //
        // Sondear los 8 candidatos cuando el techo dice 3 son 5 llamadas que solo
        // pueden devolver 222. Y no son gratis: con MaxConnectionsPerServer=4 los 8
        // candidatos van en DOS oleadas, asi que la pantalla esperaba 3 idas y
        // vueltas (esta consulta + dos oleadas). Podando queda en 2, y de 9 llamadas
        // se baja a 3 o 4 — que sobre red movil tambien se nota.
        //
        // La poda es una OPTIMIZACION, nunca una autoridad: si el techo no vino, o
        // no deja ningun candidato, o el sondeo podado no encuentra nada, se consulta
        // la lista completa. Un techo equivocado puede costar una llamada extra, pero
        // NUNCA puede esconderle un plazo valido al cajero.
        var techo = oferta is ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit> ok
            ? ok.Data.Months
            : 0;

        var podados = techo > 0
            ? candidatos.Where(m => m <= techo).ToList()
            : candidatos.ToList();

        if (podados.Count == 0) podados = candidatos.ToList();

        var sePodo = podados.Count < candidatos.Count;
        if (sePodo)
            AppLogger.I("SistecreditoService",
                $"Techo de {techo} meses para ${creditValue:N0}: se sondean " +
                $"{podados.Count} plazos en vez de {candidatos.Count}.");

        var resultado = await SondearPlazosAsync(
            creditValue, podados, codigo, idDocument, cancellationToken);

        // Red de seguridad de la poda: si no quedo ningun plazo Y habiamos recortado,
        // se reintenta con todos. Asi un techo mal reportado cuesta latencia, no una
        // venta.
        if (resultado.Plazos.Count == 0 && sePodo)
        {
            AppLogger.W("SistecreditoService",
                $"El sondeo podado por el techo ({techo}) no encontro ningun plazo para " +
                $"${creditValue:N0}; se reintenta con los {candidatos.Count} candidatos.");
            resultado = await SondearPlazosAsync(
                creditValue, candidatos, codigo, idDocument, cancellationToken);
        }

        _audit.Log(
            AuditActions.Simulate,
            $"plazos validos para ${creditValue:N0}: " +
            (resultado.Plazos.Count == 0
                ? "ninguno"
                : string.Join(",", resultado.Plazos.Select(v => v.Months))));

        return resultado;
    }

    /// <summary>
    /// Le pregunta a getCreditDetails, plazo por plazo y en paralelo, cuales acepta.
    ///
    /// Un 222 NO es un error: es la respuesta "ese plazo no aplica", y ese plazo
    /// simplemente no se ofrece. Un fallo tecnico si se reporta, porque significa que
    /// la lista puede estar incompleta y no hay que hacerla pasar por completa.
    /// </summary>
    private async Task<PlazosValidos> SondearPlazosAsync(
        double creditValue, IReadOnlyList<int> candidatos,
        string codigoTipoDocumento, string idDocument,
        CancellationToken cancellationToken)
    {
        var consultas = candidatos.Select(async meses =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var r = await _repo.GetCreditDetailsAsync(
                creditValue, frequency: _config.Frequency, meses,
                codigoTipoDocumento, idDocument);

            return r switch
            {
                // El tipo se anota nullable a proposito: los otros brazos devuelven
                // Plazo null, y sin esto el compilador infiere la tupla como no
                // nullable y pierde la capacidad de avisarle a quien consume que
                // tiene que filtrar. En una ruta que decide plazos de credito,
                // conviene que el compilador siga vigilando.
                ApiResult<CreditDetails>.Ok<CreditDetails> ok =>
                    (Plazo: (PlazoSimulado?)new PlazoSimulado(meses, ok.Data), Error: (ApiError?)null),

                // 222: el plazo no aplica para este monto. Es informacion, no falla.
                ApiResult<CreditDetails>.Failure<CreditDetails> f
                    when f.Cause is ApiError.Business b && b.Code == ErrorCodeMonthsNumberNotValid =>
                    (Plazo: null, Error: null),

                ApiResult<CreditDetails>.Failure<CreditDetails> f => (Plazo: null, Error: f.Cause),

                _ => (Plazo: null, Error: (ApiError?)null)
            };
        });

        var resultados = await Task.WhenAll(consultas);

        var validos = resultados
            .Select(r => r.Plazo)
            .Where(p => p is not null)
            .Select(p => p!)
            .OrderBy(p => p.Months)
            .ToList();

        var primerError = resultados.Select(r => r.Error).FirstOrDefault(e => e is not null);

        return new PlazosValidos(validos, primerError);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Nombre del cajero desde el XML de SellerData de HioPos.
    ///
    /// QA B-3: se compartia por copia con [ConfirmacionViewModel]. Ahora es el
    /// unico punto y es agnostico al namespace (antes un regex sobre
    /// <c>&lt;name&gt;</c> fallaba si el XML traia prefijo).
    /// </summary>
    public static string? ExtractSellerName(string? sellerData)
    {
        if (string.IsNullOrWhiteSpace(sellerData)) return null;

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(sellerData);
            var name = doc.Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "name",
                    StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch (System.Xml.XmlException)
        {
            // No es XML valido: se intenta el regex como ultimo recurso.
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            sellerData, @"<(?:\w+:)?name>([^<]+)</(?:\w+:)?name>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>
/// Resultado de un intento de abono. Distingue los cuatro desenlaces que la UI
/// debe tratar distinto (QA C-5 / A-10).
/// </summary>
public abstract record PaymentOutcome
{
    /// <summary>El abono se registró ahora.</summary>
    public sealed record Ok(Payment Payment) : PaymentOutcome;

    /// <summary>Ya estaba registrado: se reimprime el comprobante, no se cobra otra vez.</summary>
    public sealed record AlreadyPaid(Payment Payment) : PaymentOutcome;

    /// <summary>
    /// Hay un intento previo cuyo resultado no se conoce. NO se debe reintentar
    /// sin verificar el saldo en Credinet.
    /// </summary>
    public sealed record InDoubt(CachedPayment Pending) : PaymentOutcome;

    /// <summary>Credinet rechazó el abono por regla de negocio: se puede reintentar.</summary>
    public sealed record Failure(ApiError Error) : PaymentOutcome;

    /// <summary>
    /// Error de red: el cobro pudo haberse aplicado. Queda registrado como
    /// pendiente para que el próximo intento lo detecte.
    /// </summary>
    public sealed record NetworkUncertain(ApiError Error, string PaymentKey) : PaymentOutcome;
}
