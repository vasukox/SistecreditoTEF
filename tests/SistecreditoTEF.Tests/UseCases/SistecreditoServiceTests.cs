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

        // QA A-2: la cedula va ENMASCARADA. Este test antes exigia lo contrario
        // (Assert.Contains("1234567890")), o sea que consagraba la fuga: el
        // comment de auditoria viaja al POS por broadcast, fuera del sandbox de
        // la app, y se persiste en la BD local.
        Assert.DoesNotContain("1234567890", audit.comment);
        Assert.Contains("******7890", audit.comment);
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
    public async Task CrearCredito_replay_devuelve_el_credito_COMPLETO_no_ceros()
    {
        // QA C-4: el replay de idempotencia reconstruia el Credit con TODOS los
        // importes en 0.0, y esos campos alimentan directamente el voucher: el
        // cliente se llevaba un comprobante con cuota mensual $0, cuota inicial
        // $0 y Tasa E.A. 0,00% para un credito real y vigente.
        var idem = new FakeIdempotencyStore();
        var original = MakeCredit("credit-cached", 42);

        // Primero se crea de verdad, para que quede cacheado como en produccion.
        var service = BuildService(
            repo: new FakeRepo { OnCrear = (_, _, _, _, _, _, _, _) => new ApiResult<Credit>.Ok<Credit>(original) },
            audit: new FakeAudit(),
            idem: idem);

        await service.CrearCreditoAsync("sale-1", DocumentType.CedulaCiudadania, "123", 500_000, 12, "tok");

        // Segundo intento con el mismo SaleId: no debe pegarle a la API y debe
        // devolver los MISMOS importes.
        var replayService = BuildService(new FakeRepo(), new FakeAudit(), idem);
        var result = await replayService.CrearCreditoAsync(
            "sale-1", DocumentType.CedulaCiudadania, "123", 500_000, 12, "tok");

        var ok = Assert.IsType<ApiResult<Credit>.Ok<Credit>>(result);
        Assert.Equal("credit-cached", ok.Data.CreditId);
        Assert.Equal(42, ok.Data.CreditNumber);
        Assert.Equal(original.TotalFeeValue, ok.Data.TotalFeeValue);
        Assert.Equal(original.EffectiveAnnualRate, ok.Data.EffectiveAnnualRate);
        Assert.Equal(original.TotalDownPayment, ok.Data.TotalDownPayment);
        Assert.Equal(original.CreditValue, ok.Data.CreditValue);
        Assert.Equal(original.Fees, ok.Data.Fees);
    }

    [Fact]
    public async Task CrearCredito_replay_sin_datos_financieros_falla_en_vez_de_imprimir_ceros()
    {
        // Registro de una version anterior (sin CreditJson). Imprimir ceros seria
        // entregar un documento de credito con informacion falsa, asi que se
        // devuelve un fallo accionable.
        var idem = new FakeIdempotencyStore();
        await idem.SaveAsync(new CachedTransaction(
            SaleId: "sale-legacy", CreditId: "credit-cached",
            CreditNumber: 42, TransactionData: "td", AuthorizationId: "auth",
            CardHolder: "", CardNum: "",
            MerchantReceiptXml: "", CustomerReceiptXml: "", CreatedAt: DateTime.UtcNow));

        var service = BuildService(new FakeRepo(), new FakeAudit(), idem);

        var result = await service.CrearCreditoAsync(
            "sale-legacy", DocumentType.CedulaCiudadania, "123", 500_000, 12, "tok");

        var fail = Assert.IsType<ApiResult<Credit>.Failure<Credit>>(result);
        Assert.IsType<ApiError.Local>(fail.Cause);
        Assert.Contains("42", fail.Cause.UserMessage);
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

    // ==================================================================
    // Un abono ya cobrado NUNCA se vuelve a cobrar
    // ==================================================================

    [Fact]
    public async Task Un_abono_completado_con_comprobante_ilegible_NO_se_vuelve_a_cobrar()
    {
        // El caso: el intento quedo Completed —Credinet YA cobro— pero su JSON no se
        // puede deserializar, porque lo escribio una version anterior de la app con
        // otro formato de Payment. Es decir, justo despues de una actualizacion.
        //
        // Antes esto hacia `break` y seguia hasta el POST: se cobraba dos veces.
        var idem = new FakeIdempotencyStore();
        await idem.SavePaymentAsync(new CachedPayment(
            PaymentKey: "k1",
            CreditId: "CRED-1",
            AmountCents: 50_000_00,
            Status: PaymentAttemptStatus.Completed,
            PaymentId: "PAY-1",
            PaymentNumber: 7,
            PaymentJson: "{{{ esto no es JSON valido",
            CreatedAt: DateTime.UtcNow,
            CompletedAt: DateTime.UtcNow));

        var seLlamoAlApi = false;
        var service = BuildService(
            repo: new FakeRepo
            {
                OnPagar = (_, _, _) =>
                {
                    seLlamoAlApi = true;
                    return new ApiResult<Payment>.Ok<Payment>(MakePayment("PAY-2"));
                }
            },
            audit: new FakeAudit(),
            idem: idem);

        var outcome = await service.PagarCreditoAsync("CRED-1", 50_000m, "cajero");

        Assert.False(seLlamoAlApi, "Un abono ya cobrado no puede volver a enviarse a Credinet.");
        Assert.IsType<PaymentOutcome.InDoubt>(outcome);
    }

    [Fact]
    public async Task Un_abono_completado_y_legible_se_reimprime_sin_cobrar_de_nuevo()
    {
        var pago = MakePayment("PAY-1");
        var idem = new FakeIdempotencyStore();
        await idem.SavePaymentAsync(new CachedPayment(
            PaymentKey: "k1",
            CreditId: "CRED-1",
            AmountCents: 50_000_00,
            Status: PaymentAttemptStatus.Completed,
            PaymentId: "PAY-1",
            PaymentNumber: 7,
            PaymentJson: System.Text.Json.JsonSerializer.Serialize(pago),
            CreatedAt: DateTime.UtcNow,
            CompletedAt: DateTime.UtcNow));

        var seLlamoAlApi = false;
        var service = BuildService(
            repo: new FakeRepo
            {
                OnPagar = (_, _, _) =>
                {
                    seLlamoAlApi = true;
                    return new ApiResult<Payment>.Ok<Payment>(pago);
                }
            },
            audit: new FakeAudit(),
            idem: idem);

        var outcome = await service.PagarCreditoAsync("CRED-1", 50_000m, "cajero");

        Assert.False(seLlamoAlApi);
        var already = Assert.IsType<PaymentOutcome.AlreadyPaid>(outcome);
        Assert.Equal("PAY-1", already.Payment.PaymentId);
    }

    private static Payment MakePayment(string paymentId) => new(
        TypeDocument: "CC", IdDocument: "1234567890", CreditId: "CRED-1",
        PaymentId: paymentId, PaymentNumber: 7,
        CreditValuePaid: 50_000, InterestValuePaid: 0, ArrearsValuePaid: 0,
        AssuranceValuePaid: 0, ChargeValuePaid: 0, Balance: 100_000,
        NextDueDate: "2026-09-23", NextMinimumPayment: 50_000);

    // ==================================================================
    // Plazos validos: se le pregunta a Credinet plazo por plazo
    // ==================================================================
    //
    // getSimulatedMonthLimit devuelve un numero que NO se puede leer como "todos
    // los plazos hasta aca son validos". Capturado en terminal con $99.900: la
    // pantalla ofrecia 6, 3 y 2 meses y Credinet rechazo los tres con
    // errorCode=222 MonthsNumberNotValid; solo 1 mes era valido. El cajero lo
    // descubria a fuerza de errores.
    //
    // La unica autoridad es getCreditDetails, y estos tests fijan como se
    // interpreta su respuesta.

    /// <summary>Los meses que Credinet acepta en el escenario observado en terminal.</summary>
    private static SistecreditoService BuildServiceConPlazosValidos(params int[] validos)
    {
        var permitidos = new HashSet<int>(validos);
        return BuildService(
            repo: new FakeRepo
            {
                OnGetCreditDetails = (creditValue, _, months, _, _) =>
                    permitidos.Contains(months)
                        ? new ApiResult<CreditDetails>.Ok<CreditDetails>(
                            MakeDetails(months, creditValue))
                        : new ApiResult<CreditDetails>.Failure<CreditDetails>(
                            new ApiError.Business(
                                SistecreditoService.ErrorCodeMonthsNumberNotValid,
                                "MonthsNumberNotValid"))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());
    }

    private static CreditDetails MakeDetails(int meses, double creditValue) => new(
        DownPayment: 0,
        TotalFeeValue: creditValue / meses,
        CreditValue: creditValue,
        Fees: meses,
        AssuranceValue: 0,
        InterestRate: 2.1,
        TotalInterestValue: 0,
        TotalDownPayment: 0,
        FeeCreditValue: creditValue / meses,
        AssuranceFeeValue: 0,
        AssuranceTotalValue: 0,
        AssuranceTaxFeeValue: 0,
        AssuranceTaxValue: 0,
        DownPaymentPercentage: 0,
        AssurancePercentage: 0,
        AssuranceTotalFeeValue: 0,
        TotalPaymentValue: creditValue,
        CustomerAllowPhotoSignature: false);

    [Fact]
    public async Task Solo_se_devuelven_los_plazos_que_Credinet_acepta()
    {
        // El caso real del terminal: credito de $99.900, unico plazo valido 1 mes.
        var service = BuildServiceConPlazosValidos(1);

        var r = await service.ObtenerPlazosValidosAsync(
            99_900, [1, 2, 3, 6, 9, 12, 18, 24],
            DocumentType.CedulaCiudadania, "1234567890");

        Assert.Equal([1], r.Plazos.Select(p => p.Months));
    }

    [Fact]
    public async Task Un_errorCode_222_no_es_un_fallo_tecnico()
    {
        // Es la respuesta "ese plazo no aplica". Si se tratara como fallo, la
        // pantalla mostraria un error en vez de simplemente no ofrecer el plazo.
        var service = BuildServiceConPlazosValidos(1);

        var r = await service.ObtenerPlazosValidosAsync(
            99_900, [1, 6], DocumentType.CedulaCiudadania, "x");

        Assert.Null(r.ErrorTecnico);
        Assert.Single(r.Plazos);
    }

    [Fact]
    public async Task Los_plazos_vienen_ordenados_y_con_su_cuota_ya_calculada()
    {
        // La cuota se trae en esta misma consulta para que elegir un plazo sea
        // instantaneo despues, sin una segunda llamada de ~4 segundos.
        var service = BuildServiceConPlazosValidos(12, 3, 6);

        var r = await service.ObtenerPlazosValidosAsync(
            600_000, [1, 2, 3, 6, 9, 12], DocumentType.CedulaCiudadania, "x");

        Assert.Equal([3, 6, 12], r.Plazos.Select(p => p.Months));
        Assert.All(r.Plazos, p => Assert.True(p.Detalles.TotalFeeValue > 0));
    }

    [Fact]
    public async Task Si_ningun_plazo_aplica_la_lista_queda_vacia_sin_error()
    {
        var service = BuildServiceConPlazosValidos();

        var r = await service.ObtenerPlazosValidosAsync(
            1_000, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.Empty(r.Plazos);
        Assert.Null(r.ErrorTecnico);
    }

    [Fact]
    public async Task Un_fallo_de_red_se_reporta_para_no_hacer_pasar_la_lista_por_completa()
    {
        // Sin esto, un plazo valido que no se pudo verificar desapareceria en
        // silencio y la pantalla afirmaria que Credinet no lo acepta.
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetCreditDetails = (creditValue, _, months, _, _) => months == 1
                    ? new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(1, creditValue))
                    : new ApiResult<CreditDetails>.Failure<CreditDetails>(
                        new ApiError.Network(new HttpRequestException("sin red")))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            99_900, [1, 6], DocumentType.CedulaCiudadania, "x");

        Assert.Single(r.Plazos);
        Assert.NotNull(r.ErrorTecnico);
    }

    [Fact]
    public async Task Un_plazo_solo_se_ofrece_si_Credinet_lo_confirmo_positivamente()
    {
        // La propiedad que importa para la correccion del credito: nunca se ofrece
        // un plazo que no haya devuelto Ok. Ante cualquier otra respuesta, fuera.
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetCreditDetails = (_, _, _, _, _) =>
                    new ApiResult<CreditDetails>.Failure<CreditDetails>(
                        new ApiError.Http(500, "boom"))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            500_000, [1, 2, 3, 6, 9, 12, 18, 24], DocumentType.CedulaCiudadania, "x");

        Assert.Empty(r.Plazos);
        Assert.NotNull(r.ErrorTecnico);
    }

    // ==================================================================
    // Barrera de oferta: el monto se valida ANTES de ofrecer plazos
    // ==================================================================
    //
    // El defecto que protege: getCreditDetails es una CALCULADORA y simula cualquier
    // monto sin mirar si el cliente tiene una oferta que lo cubra. El cajero llegaba
    // hasta la pantalla del OTP y ahi reventaba:
    //
    //   OtpViewModel: Solicitando OTP: monto=219500, meses=3
    //   OtpViewModel: getCreditToken FAILURE: CREDINET: InvalidAmountCredit
    //
    // y volvia a fallar con 2 y con 1 mes, porque el plazo nunca fue el problema.
    // Comprobado contra el sandbox: getSimulatedMonthLimit devuelve 1104
    // NoOfferAvailable para el mismo monto que getCreditDetails simula sin chistar.

    [Fact]
    public async Task Sin_oferta_para_el_monto_no_se_ofrece_ningun_plazo()
    {
        var detallesConsultados = 0;
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Business(
                            SistecreditoService.ErrorCodeNoOfferAvailable, "NoOfferAvailable")),
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    detallesConsultados++;
                    return new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 219_500));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            219_500, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.True(r.SinOferta);
        Assert.Empty(r.Plazos);
        Assert.Null(r.ErrorTecnico);

        // Y no se gasta una ronda de llamadas preguntando por plazos que ninguno
        // podria ser valido: son ~4 segundos de espera en el POS.
        Assert.Equal(0, detallesConsultados);
    }

    /// <summary>
    /// "Sin oferta" NO es lo mismo que "ningun plazo aplica": la causa es el monto y
    /// el mensaje al cajero tiene que ser distinto, si no prueba plazo por plazo un
    /// monto que nunca va a pasar.
    /// </summary>
    [Fact]
    public async Task Ningun_plazo_valido_no_se_confunde_con_sin_oferta()
    {
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetCreditDetails = (_, _, _, _, _) =>
                    new ApiResult<CreditDetails>.Failure<CreditDetails>(
                        new ApiError.Business(
                            SistecreditoService.ErrorCodeMonthsNumberNotValid,
                            "MonthsNumberNotValid"))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            219_500, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.False(r.SinOferta);
        Assert.Empty(r.Plazos);
    }

    [Fact]
    public async Task Con_oferta_disponible_se_simulan_los_plazos_normalmente()
    {
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(
                        new SimulatedMonthLimit(3)),
                OnGetCreditDetails = (_, _, m, _, _) =>
                    new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 219_500))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            110_000, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.False(r.SinOferta);
        Assert.Equal(3, r.Plazos.Count);
    }

    /// <summary>
    /// La barrera FALLA ABIERTA ante un problema tecnico. Bloquear una venta que
    /// podria ser perfectamente valida porque se cayo la red un segundo es peor que
    /// dejar que el rechazo aparezca mas adelante, donde ya esta traducido.
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Un_fallo_tecnico_en_la_barrera_no_bloquea_la_venta(int codigoHttp)
    {
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Http(codigoHttp, "boom")),
                OnGetCreditDetails = (_, _, m, _, _) =>
                    new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 219_500))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            219_500, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.False(r.SinOferta);
        Assert.Equal(3, r.Plazos.Count);
    }

    /// <summary>
    /// Sin red tampoco se bloquea: mismo criterio que el fallo tecnico.
    /// </summary>
    [Fact]
    public async Task Sin_red_en_la_barrera_tampoco_se_bloquea_la_venta()
    {
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Network(new HttpRequestException("sin red"))),
                OnGetCreditDetails = (_, _, m, _, _) =>
                    new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 219_500))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            219_500, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.False(r.SinOferta);
        Assert.Equal(3, r.Plazos.Count);
    }

    /// <summary>
    /// Otro error de negocio del endpoint de oferta NO se interpreta como "sin
    /// oferta": solo el 1104. Tratar cualquier 400 como "sin oferta" bloquearia
    /// ventas validas por una causa que no conocemos.
    /// </summary>
    [Fact]
    public async Task Otro_error_de_negocio_en_la_barrera_no_es_sin_oferta()
    {
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Business(999, "AlgoDesconocido")),
                OnGetCreditDetails = (_, _, m, _, _) =>
                    new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 219_500))
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            219_500, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.False(r.SinOferta);
        Assert.Equal(3, r.Plazos.Count);
    }

    // ==================================================================
    // Poda por el techo de meses: es optimizacion, NUNCA autoridad
    // ==================================================================
    //
    // El techo que devuelve getSimulatedMonthLimit resulto ser un limite superior
    // valido en todas las mediciones, pero DEMASIADO PERMISIVO (para $110.000 dice 3
    // y getCreditDetails solo acepta 1 y 2). Sirve para no gastar llamadas, no para
    // decidir la lista. Estos tests fijan las dos mitades de eso.

    [Fact]
    public async Task El_techo_recorta_las_consultas_que_no_pueden_servir()
    {
        var consultados = new List<int>();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(
                        new SimulatedMonthLimit(3)),
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    consultados.Add(m);
                    return m <= 2
                        ? new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 110_000))
                        : new ApiResult<CreditDetails>.Failure<CreditDetails>(
                            new ApiError.Business(
                                SistecreditoService.ErrorCodeMonthsNumberNotValid,
                                "MonthsNumberNotValid"));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            110_000, [1, 2, 3, 6, 9, 12, 18, 24], DocumentType.CedulaCiudadania, "x");

        // Solo se preguntan los que caben bajo el techo.
        Assert.Equal([1, 2, 3], consultados.OrderBy(m => m));

        // Y la lista sigue saliendo de getCreditDetails, no del techo.
        Assert.Equal([1, 2], r.Plazos.Select(p => p.Months));
    }

    /// <summary>
    /// La red de seguridad: si el techo recorto y el sondeo podado no encontro NADA,
    /// se reintenta con la lista completa. Un techo mal reportado puede costar
    /// latencia, nunca esconderle un plazo valido al cajero.
    /// </summary>
    [Fact]
    public async Task Si_la_poda_no_deja_nada_se_reintenta_con_todos()
    {
        var consultados = new List<int>();
        var service = BuildService(
            repo: new FakeRepo
            {
                // Techo absurdamente bajo: recorta a {1}.
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(
                        new SimulatedMonthLimit(1)),
                // Pero el plazo que de verdad sirve es 6.
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    consultados.Add(m);
                    return m == 6
                        ? new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 500_000))
                        : new ApiResult<CreditDetails>.Failure<CreditDetails>(
                            new ApiError.Business(
                                SistecreditoService.ErrorCodeMonthsNumberNotValid,
                                "MonthsNumberNotValid"));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            500_000, [1, 2, 3, 6, 9, 12], DocumentType.CedulaCiudadania, "x");

        // El plazo valido NO se perdio, aunque el techo lo hubiera descartado.
        Assert.Equal([6], r.Plazos.Select(p => p.Months));

        // Y se ve que hubo dos rondas: el 1 podado, y despues la lista completa.
        Assert.Contains(6, consultados);
        Assert.True(consultados.Count > 1);
    }

    /// <summary>
    /// Sin techo utilizable no se poda nada: se consultan todos los candidatos.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Un_techo_no_utilizable_no_recorta(int techo)
    {
        var consultados = new List<int>();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(
                        new SimulatedMonthLimit(techo)),
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    consultados.Add(m);
                    return new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 300_000));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        await service.ObtenerPlazosValidosAsync(
            300_000, [1, 2, 3], DocumentType.CedulaCiudadania, "x");

        Assert.Equal([1, 2, 3], consultados.OrderBy(m => m));
    }

    /// <summary>
    /// Si la barrera de oferta falla por red, tampoco se poda: se sondea completo.
    /// Es coherente con que la barrera falle abierta.
    /// </summary>
    [Fact]
    public async Task Si_la_barrera_falla_por_red_no_se_poda()
    {
        var consultados = new List<int>();
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Network(new HttpRequestException("sin red"))),
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    consultados.Add(m);
                    return new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 300_000));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            300_000, [1, 2, 3, 6], DocumentType.CedulaCiudadania, "x");

        Assert.Equal([1, 2, 3, 6], consultados.OrderBy(m => m));
        Assert.Equal(4, r.Plazos.Count);
    }

    /// <summary>
    /// Sin oferta no se sondea NINGUN plazo: son llamadas que no pueden servir, y en
    /// un POS eso son segundos de espera para nada.
    /// </summary>
    [Fact]
    public async Task Sin_oferta_no_se_gasta_ninguna_consulta_de_plazos()
    {
        var consultados = 0;
        var service = BuildService(
            repo: new FakeRepo
            {
                OnGetSimulatedMonthLimit = _ =>
                    new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                        new ApiError.Business(
                            SistecreditoService.ErrorCodeNoOfferAvailable, "NoOfferAvailable")),
                OnGetCreditDetails = (_, _, m, _, _) =>
                {
                    consultados++;
                    return new ApiResult<CreditDetails>.Ok<CreditDetails>(MakeDetails(m, 199_900));
                }
            },
            audit: new FakeAudit(),
            idem: new FakeIdempotencyStore());

        var r = await service.ObtenerPlazosValidosAsync(
            199_900, [1, 2, 3, 6, 9, 12, 18, 24], DocumentType.CedulaCiudadania, "x");

        Assert.True(r.SinOferta);
        Assert.Equal(0, consultados);
    }

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
        /// <summary>
        /// Por defecto responde "HAY oferta para este monto".
        ///
        /// [ObtenerPlazosValidosAsync] consulta este endpoint como barrera de oferta
        /// antes de simular plazos, asi que sin un default los tests de plazos
        /// —que no tienen nada que decir sobre la oferta— reventaban con NRE. El
        /// default correcto es el camino feliz: los tests que prueban la barrera
        /// asignan el hook explicitamente.
        /// </summary>
        public Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(double c)
            => Task.FromResult(OnGetSimulatedMonthLimit is null
                ? new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(new SimulatedMonthLimit(24))
                : OnGetSimulatedMonthLimit(c));
    }

    private sealed class FakeAudit : IAuditLogger
    {
        public Action<string, string, string?>? OnLog { get; set; }
        public void Log(string action, string comment, string? documentId = null)
            => OnLog?.Invoke(action, comment, documentId);
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, CachedPayment> _payments = new();

        /// <summary>Intentos de pago guardados, en orden de escritura.</summary>
        public List<CachedPayment> PaymentHistory { get; } = new();

        public Task<CachedPayment?> FindRecentPaymentAsync(
            string creditId, long amountCents, TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            var match = _payments.Values
                .Where(p => p.CreditId == creditId
                            && p.AmountCents == amountCents
                            && p.CreatedAt > cutoff)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(match);
        }

        public Task SavePaymentAsync(CachedPayment payment)
        {
            _payments[payment.PaymentKey] = payment;
            PaymentHistory.Add(payment);
            return Task.CompletedTask;
        }

        public Task PurgeOlderThanAsync(TimeSpan retention) => Task.CompletedTask;

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
}
