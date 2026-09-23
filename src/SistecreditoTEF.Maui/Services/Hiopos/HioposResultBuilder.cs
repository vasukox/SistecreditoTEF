using System.Globalization;
using SistecreditoTEF.Maui.Services.Hiopos.Models;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Construye el POCO [HioposResponse] que [MainActivity] luego
/// materializa en [Android.Content.Intent].
///
/// Libre de dependencias Android (V8): se testea en xUnit puro.
///
/// Doc §2-§4: cubre las 11 actions que HioPosCloud puede disparar.
/// Para cada una hay un metodo dedicado con la forma exacta del
/// response esperada por el POS.
/// </summary>
public class HioposResultBuilder
{
    private readonly ReceiptBuilder _receiptBuilder;

    public HioposResultBuilder(ReceiptBuilder receiptBuilder)
    {
        _receiptBuilder = receiptBuilder;
    }

    // ------------------------------------------------------------------
    // ACTIONS SINCRONAS (responder rapido + Finish)
    // ------------------------------------------------------------------

    /// <summary>Responde a INITIALIZE / FINALIZE con resultado OK vacio.</summary>
    public HioposResponse BuildOk(string action) =>
        new(action, new Dictionary<string, string?>());

    /// <summary>
    /// Responde a GET_VERSION.
    ///
    /// La version va como ENTERO, no como cadena. HioPos la lee con getIntExtra;
    /// mandarla como String hacia que se quedara con el default (-1) y pidiera
    /// reinstalar el modulo en cada arranque. Ver [HioposResponse.IntExtras].
    /// </summary>
    public HioposResponse BuildVersion(string action, int version)
    {
        return new HioposResponse(
            action,
            new Dictionary<string, string?>(),
            ResultCode: Hiopos.Result.OkValue,
            IntExtras: new Dictionary<string, int>
            {
                [HioposExtras.Version] = version
            });
    }

    /// <summary>
    /// Responde a GET_BEHAVIOR con las capacidades del modulo (doc §3).
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// VAN COMO BOOLEAN. COMO CADENA NO LLEGABA NINGUNA.
    /// ───────────────────────────────────────────────────────────────────────────
    /// Esto se emitia como <c>"true"</c> / <c>"false"</c> en extras de texto, y
    /// HioPos las lee asi — ExternalModule.getNormalizedBooleanBehaviour(), APK
    /// icg.android.start 15.9.0.0:
    ///
    ///     for (String key : intent.getExtras().keySet())
    ///         if (key.equalsIgnoreCase(nombre))
    ///             return intent.getBooleanExtra(key, porDefecto);
    ///
    /// <c>getBooleanExtra</c> sobre un extra String devuelve el default, asi que
    /// HioPos nunca recibio una sola capacidad: para el, el modulo no soportaba
    /// credito, no tenia parametros propios y no atendia transacciones de caja.
    /// Es el MISMO defecto que el de <c>Version</c> (§HioposActions.ModuleVersion),
    /// con boolean en lugar de int, y por eso no daba error: un tipo equivocado en
    /// un Bundle se descarta en silencio.
    ///
    /// Efecto medido en el terminal: con <c>supportsCashTransaction=false</c>,
    /// <c>CashTransactionActivity</c> no llama a
    /// <c>executePaymentGatewayCashTransaction()</c> —la que arma el request con
    /// <c>TransactionType=CASH_IN</c>— y manda el recaudo por la rama de venta. De
    /// ahi que una entrada de caja llegara como <c>SALE</c> (ver §4.2 de docs/04).
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// CUALES LEE HIOPOS
    /// ───────────────────────────────────────────────────────────────────────────
    /// <c>PaymentGateway$Behavior</c> declara 18 claves, y la comparacion es
    /// <c>equalsIgnoreCase</c>, asi que los nombres que ya usabamos sirven. De las
    /// que emitimos, estas seis NO estan en esa lista: <c>canAudit</c>,
    /// <c>SaveLoyaltyCardNum</c>, <c>CanPrint</c>, <c>SupportsSale</c>,
    /// <c>SupportsVoid</c> y <c>SupportOverPayment</c>. Se siguen enviando —el
    /// manual documenta canAudit y CanPrint, y otros subsistemas podrian leerlas—
    /// pero ahora se sabe que el gateway de cobro no las mira.
    ///
    /// Las tres que HioPos acepta y NO declaramos, a proposito:
    /// <c>OnlyCreditForTipAdjustment</c>, <c>AskOperationsOnCashCount</c> y
    /// <c>ReplaceSecondScreen</c>. Declarar una capacidad que no se implementa
    /// hace que el POS pida algo que el modulo no sabe responder.
    /// </summary>
    public HioposResponse BuildBehavior(string action)
    {
        var extras = new Dictionary<string, bool>
        {
            [HioposExtras.SupportsTransactionVoid]    = HioposCapabilities.SupportsTransactionVoid,
            [HioposExtras.SupportsTransactionQuery]   = HioposCapabilities.SupportsTransactionQuery,
            [HioposExtras.SupportsNegativeSales]      = HioposCapabilities.SupportsNegativeSales,
            [HioposExtras.SupportsPartialRefund]      = HioposCapabilities.SupportsPartialRefund,
            [HioposExtras.SupportsBatchClose]         = HioposCapabilities.SupportsBatchClose,
            [HioposExtras.SupportsTipAdjustment]      = HioposCapabilities.SupportsTipAdjustment,
            [HioposExtras.SupportsCredit]             = HioposCapabilities.SupportsCredit,
            [HioposExtras.SupportsDebit]              = HioposCapabilities.SupportsDebit,
            [HioposExtras.SupportsEBTFoodstamp]       = HioposCapabilities.SupportsEBTFoodstamp,
            [HioposExtras.HasCustomParams]            = HioposCapabilities.HasCustomParams,
            [HioposExtras.CanChargeCard]              = HioposCapabilities.CanChargeCard,
            [HioposExtras.CanAudit]                   = HioposCapabilities.CanAudit,
            [HioposExtras.ExecuteVoidWhenAvailable]   = HioposCapabilities.ExecuteVoidWhenAvailable,
            [HioposExtras.SaveLoyaltyCardNum]         = HioposCapabilities.SaveLoyaltyCardNum,
            [HioposExtras.CanPrint]                   = HioposCapabilities.CanPrint,
            [HioposExtras.ReadCardFromApi]            = HioposCapabilities.ReadCardFromApi,
            [HioposExtras.OnlyUseDocumentPath]        = HioposCapabilities.OnlyUseDocumentPath,

            // LA IMPORTANTE PARA EL RECAUDO. Es la que decide, en
            // CashTransactionActivity, si el POS usa la rama de caja —que manda
            // TransactionType=CASH_IN y toma el importe del medio de pago— o la de
            // venta. El modulo SI atiende abonos por Caja > Entradas de caja.
            [HioposExtras.SupportsCashTransaction]    = true,

            // El modulo cobra ventas: es su uso principal.
            [HioposExtras.SupportsSale]               = true,

            // Coherente con SupportsTransactionVoid: Credinet no expone reverso.
            [HioposExtras.SupportsVoid]               = false,

            // No se acepta sobrepago: el importe del abono lo define Credinet, no
            // el cajero, asi que no hay excedente legitimo que gestionar.
            [HioposExtras.SupportOverPayment]         = false
        };

        return new HioposResponse(
            action,
            new Dictionary<string, string?>(),
            ResultCode: Hiopos.Result.OkValue,
            BoolExtras: extras);
    }

    /// <summary>
    /// Responde a GET_CUSTOM_PARAMS (doc §9) con el logo y nombre del modulo.
    /// Sin esto, el cajero ve un logo generico en la pantalla de medios de pago.
    /// </summary>
    public HioposResponse BuildCustomParams(string action, byte[] logoPng)
    {
        var stringExtras = new Dictionary<string, string?>
        {
            [HioposExtras.Name] = "Sistecredito"
        };
        var binaryExtras = new Dictionary<string, byte[]>
        {
            [HioposExtras.Logo] = logoPng
        };
        return new HioposResponse(action, stringExtras, binaryExtras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a GET_PRINT_INFO (doc §10.2). Aunque en MVP
    /// [HioposCapabilities.CanPrint=false], se implementa igual por si
    /// en el futuro se activa.
    /// </summary>
    public HioposResponse BuildPrintInfo(string action, string packageName, int horizontalDots)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.PackageName]    = packageName,
            [HioposExtras.HorizontalDots] = horizontalDots.ToString()
        };
        return new HioposResponse(action, extras, ResultCode: Hiopos.Result.OkValue);
    }

    // ------------------------------------------------------------------
    // TRANSACTION: ACCEPTED / FAILED
    // ------------------------------------------------------------------

    /// <summary>
    /// Responde a TRANSACTION con ACCEPTED + comprobantes + datos
    /// del credito creado. Doc §4 y §5.
    /// </summary>
    public HioposResponse BuildTransactionAccepted(
        string merchantReceiptXml,
        string customerReceiptXml,
        string authorizationId,
        string? cardHolder = null,
        string? cardNum = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
            [HioposExtras.MerchantReceipt]   = merchantReceiptXml,
            [HioposExtras.CustomerReceipt]   = customerReceiptXml,
            [HioposExtras.AuthorizationId]    = authorizationId,
            [HioposExtras.CardType]          = "Sistecredito",
            [HioposExtras.CardHolder]        = cardHolder,
            [HioposExtras.CardNum]           = cardNum
        };
        return new HioposResponse(HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// ENTRADA DE CAJA: deja el movimiento con el importe realmente abonado, en vez
    /// del "$1" que el cajero escribio para habilitar el boton.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// ESTO NO SALE DEL MANUAL. SALE DEL APK DE HIOPOS, Y HAY QUE SABERLO
    /// ───────────────────────────────────────────────────────────────────────────
    /// El manual documenta [FixedPaymentMeanId] y [FixedPaymentMeanAmount] como
    /// parametros de salida de TRANSACTION (v6.1, 09/09/2025, requiere HioPos
    /// 12.34.0.0) y dicen exactamente lo que necesitamos: fijar el medio de pago y
    /// su importe. Se implementaron al pie de la letra y NO PASO NADA.
    ///
    /// El porque esta en el propio HioPos instalado en el terminal
    /// (icg.android.start 15.9.0.0). Los unicos que leen esos dos campos son:
    ///
    ///   icg.android.totalization.TotalizationActivity.processPaymentGatewayResponse()
    ///   icg.android.kiosk.controller.KioskSaleController.initialize*Payment()
    ///   icg.android.external.module.DocumentApiBase.applyChangesToDocument()
    ///
    /// Ninguno es la entrada de caja. <c>icg.android.cashTransaction</c> no los
    /// menciona NI UNA VEZ: de los 15 campos que lee la pantalla de total, la
    /// entrada de caja lee cinco (TransactionResult, TransactionType,
    /// TransactionData, ErrorMessageTitle y Amount). La frase del manual "modificara
    /// el medio de pago en la pantalla de TOTAL" es literal, no una forma de hablar.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// LO QUE SI FUNCIONA: CASH_IN
    /// ───────────────────────────────────────────────────────────────────────────
    /// De esos cinco campos, el importe entra por <c>Amount</c>, y lo que se hace
    /// con el depende del <c>TransactionType</c> que devolvamos —
    /// <c>CashTransactionActivity.onExternalModuleResult()</c>, classes6.dex:
    ///
    ///     paymentMean.setAmount(response.getAmount());          // siempre
    ///     if (type.equals("CASH_IN") || type.equals("CASH_OUT"))
    ///     {
    ///         paymentMean.setNetAmount(response.getAmount());
    ///         controller.sendDocumentChange();
    ///     }
    ///
    /// <c>Amount</c> es el importe ENTREGADO y <c>NetAmount</c> el APLICADO; el
    /// vuelto es la resta. Contestando SALE solo corria el primero, y de ahi salia
    /// la pantalla del cajero: importe $1, entregado $99.900, cambio $99.899.
    ///
    /// Por eso aca el TransactionType deja de ser el eco del que pidio el POS. Es
    /// la unica respuesta del modulo donde eso pasa, y la excepcion es deliberada:
    /// el guard de ese mismo metodo acepta CASH_IN explicitamente.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// POR QUE YA NO SE MANDA EL MEDIO FIJADO
    /// ───────────────────────────────────────────────────────────────────────────
    /// Porque en este flujo nadie lo lee, y porque el mecanismo que SI funciona
    /// necesita el <c>Amount</c> que el manual prohibe mandar junto al medio fijado.
    /// Mandar los dos seria apostar a un comportamiento indefinido para conseguir
    /// algo que igual no pasa. Dejar el medio en efectivo sigue siendo un pendiente
    /// con ICG (ver docs/04 §4.6).
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// ESTA LOGICA VIVE ACA Y NO EN EL ViewModel A PROPOSITO
    /// ───────────────────────────────────────────────────────────────────────────
    /// El proyecto de tests compila Services\Hiopos pero NO ViewModels\ (ver
    /// SistecreditoTEF.Tests.csproj). Mientras la regla estuvo dentro de
    /// [ReciboPagoViewModel] no habia forma de cubrirla.
    /// </summary>
    /// <param name="extras">Diccionario de extras de la respuesta, que se MUTA.</param>
    /// <param name="amountCents">
    /// Importe realmente abonado, en centavos. El manual exige el importe sin punto
    /// ni coma: $99.900 viaja como "9990000".
    /// </param>
    public static void AplicarRespuestaDeEntradaDeCaja(
        IDictionary<string, string?> extras, long amountCents)
    {
        ArgumentNullException.ThrowIfNull(extras);

        // La clave: sin esto HioPos trata el importe como plata entregada y calcula
        // un vuelto que nadie va a sacar del cajon.
        extras[HioposExtras.TransactionType] = HioposTransactionTypes.CashIn;

        extras[HioposExtras.Amount] = amountCents.ToString(CultureInfo.InvariantCulture);

        // El medio fijado no lo lee nadie en este flujo, y convive mal con el
        // Amount. Se quitan por si vinieran de una construccion previa.
        extras.Remove(HioposExtras.FixedPaymentMeanId);
        extras.Remove(HioposExtras.FixedPaymentMeanAmount);

        // Tip/Tax/Surcharge tampoco los lee la entrada de caja (de los 15 campos de
        // la pantalla de total, aca solo llegan cinco). Se omiten para que la
        // respuesta diga exactamente lo que este flujo entiende.
        extras.Remove(HioposExtras.TipAmount);
        extras.Remove(HioposExtras.TaxAmount);
        extras.Remove(HioposExtras.SurchargeAmount);
    }

    /// <summary>
    /// Responde a TRANSACTION con FAILED (doc §4). Solo expone
    /// ErrorMessage y ErrorMessageTitle - el error code interno se
    /// loguea aparte, no viaja al POS (B5).
    /// </summary>
    public HioposResponse BuildTransactionFailed(
        string errorMessage, string? errorTitle = null, string? transactionType = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult]    = TransactionResult.Failed.ToWire(),

            // Eco del tipo, con default: sin este campo el POS no da la operacion por
            // cerrada y el cajero ve el error generico de modulo externo en lugar del
            // mensaje que le estamos mandando.
            [HioposExtras.TransactionType]      =
                string.IsNullOrWhiteSpace(transactionType) ? "SALE" : transactionType,

            [HioposExtras.ErrorMessage]         = errorMessage,
            [HioposExtras.ErrorMessageTitle]    = errorTitle ?? "No se pudo completar el cobro"
        };
        return new HioposResponse(HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Respuesta del FAIL-SAFE: un handler lanzo una excepcion inesperada.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// POR QUE NO RESULT_CANCELED
    /// ───────────────────────────────────────────────────────────────────────────
    /// Este camino devolvia RESULT_CANCELED sin un solo extra, y eso es exactamente
    /// lo que HioPos muestra como "Error en modulo externo": una Activity que vuelve
    /// sin decir nada es indistinguible de un modulo que se murio. El cajero se
    /// quedaba con un cartel tecnico y sin saber si cobro o no.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// POR QUE EL MENSAJE NO PROMETE QUE NO SE COBRO
    /// ───────────────────────────────────────────────────────────────────────────
    /// A diferencia de "Volver a HioPos" —donde sabemos con certeza que no hubo
    /// cobro porque se sale antes de llamar a Credinet— aca la excepcion pudo
    /// ocurrir en cualquier punto, incluso despues de crear el credito. Decir
    /// "no se realizo ningun cobro" seria comodo y podria ser falso, asi que el
    /// texto manda a verificar. Es la unica version honesta.
    /// </summary>
    public HioposResponse BuildUnexpectedFailure(string? transactionType) =>
        BuildTransactionFailed(
            "Sistecredito no pudo completar la operacion. Si el cobro ya se habia " +
            "hecho, verificalo antes de reintentar.",
            "No se pudo continuar",
            transactionType);

    /// <summary>
    /// Responde a un REFUND que en realidad es "solta la linea de pago de esta
    /// venta" (DocumentTypeId 1 o 2). Ver [RefundClassifier].
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// ESTO ES UNA SIMULACION, Y HAY QUE SABERLO
    /// ───────────────────────────────────────────────────────────────────────────
    /// Se contesta ACCEPTED como si el abono se hubiera pagado, porque es lo unico
    /// que el POS necesita para habilitar el desmarcado de la linea. Pero NO se
    /// devuelve dinero: Credinet no expone ninguna operacion de reverso, asi que
    /// el credito que se creo para esa venta SIGUE EXISTIENDO.
    ///
    /// Consecuencia concreta: la venta deja de estar pagada con Sistecredito en el
    /// POS, y el cliente queda igual con su credito vivo. Eso se resuelve por fuera
    /// del modulo (con Sistecredito), no aca.
    ///
    /// Por eso cada uso de este camino se audita con la referencia y el importe:
    /// es el unico rastro para cuadrarlo despues.
    /// </summary>
    /// <param name="transactionType">El TransactionType recibido, para hacerle eco literal.</param>
    /// <param name="amount">
    /// El importe que mando el POS, TAL CUAL vino. Se hace eco en lugar de
    /// recalcularlo: viene en la escala del contrato (los dos ultimos digitos son
    /// decimales, $80.000 llega como "8000000") y reconvertirlo es una oportunidad
    /// gratuita de equivocarse en un factor de 100.
    /// </param>
    /// <param name="originalReference">
    /// La referencia de la operacion que se esta soltando, que llega en el extra
    /// TransactionData del intent. Se devuelve la MISMA, no una nueva: es con la que
    /// el POS cruza las dos al conciliar.
    /// </param>
    public HioposResponse BuildPaymentLineRelease(
        string? transactionType, string? amount, string? originalReference)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),

            [HioposExtras.TransactionType] =
                string.IsNullOrWhiteSpace(transactionType) ? "REFUND" : transactionType,

            [HioposExtras.Amount] = amount,

            // Las dos con la referencia original, no una nueva.
            [HioposExtras.AuthorizationId] = originalReference,
            [HioposExtras.TransactionData] = originalReference
        };

        return new HioposResponse(
            HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Rechaza una NOTA DE CREDITO (TransactionType=REFUND) sin abrir ninguna
    /// pantalla.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// POR QUE SE RECHAZA
    /// ───────────────────────────────────────────────────────────────────────────
    /// Credinet no expone ninguna operacion de reverso o anulacion, asi que el
    /// modulo no puede devolverle plata a un credito. Eso ya esta declarado en
    /// GET_BEHAVIOR: [HioposCapabilities.SupportsTransactionVoid],
    /// [SupportsPartialRefund] y [SupportsNegativeSales] son todos false.
    ///
    /// Antes el REFUND se trataba como un RECAUDO y abria la lista de creditos del
    /// cliente. Eso contradecia nuestras propias capacidades y, sobre todo, no le
    /// devolvia nada al POS hasta que el cajero completara un abono: la nota de
    /// credito quedaba sin respuesta, sin mensaje y sin poder avanzar.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// LOS TRES DETALLES QUE HACEN QUE HIOPOS MUESTRE EL MENSAJE
    /// ───────────────────────────────────────────────────────────────────────────
    /// 1. El campo es <c>TransactionResult</c>. En la API TEF 4.0 no existe un
    ///    extra llamado <c>Result</c>; un modulo que manda ese nombre hace que el
    ///    POS no reconozca la respuesta como transaccion fallida y descarte el
    ///    ErrorMessage, y el cajero ve una ventana en blanco.
    ///
    /// 2. El ResultCode va en RESULT_OK, NO en RESULT_CANCELED. El codigo de la
    ///    Activity solo dice "te respondi"; el rechazo viaja en TransactionResult.
    ///    Con RESULT_CANCELED el POS entiende "el cajero se salio" y vuelve a su
    ///    pantalla sin mostrar nada.
    ///
    /// 3. El eco de <c>TransactionType</c> es obligatorio. Si el POS recibe un tipo
    ///    distinto del que pidio —o ninguno— no da la operacion por cerrada y
    ///    relanza el Intent: se ve como si el modulo se abriera en bucle. Es el
    ///    mismo defecto que ya se habia visto en los abonos, donde se contestaba
    ///    REFUND a un SALE y HioPos relanzaba con un TransactionId nuevo.
    /// </summary>
    /// <param name="transactionType">
    /// El TransactionType recibido, para hacerle eco LITERAL. Si HioPos no lo
    /// mandara, se responde "REFUND" para no dejar el campo vacio.
    /// </param>
    public HioposResponse BuildRefundNotSupported(string? transactionType)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),

            // Eco literal: lo que pidio el POS es lo que se le contesta.
            [HioposExtras.TransactionType] =
                string.IsNullOrWhiteSpace(transactionType) ? "REFUND" : transactionType,

            [HioposExtras.ErrorMessageTitle] = "Forma de pago no valida",
            [HioposExtras.ErrorMessage] =
                "Sistecredito no admite notas de credito. Usa otro medio de pago."
        };

        return new HioposResponse(
            HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a TRANSACTION cuando el CAJERO se sale del modulo sin cobrar:
    /// "Volver a HioPos" o el boton atras del POS.
    ///
    /// ───────────────────────────────────────────────────────────────────────────
    /// POR QUE NO SE DEVUELVE RESULT_CANCELED
    /// ───────────────────────────────────────────────────────────────────────────
    /// Se devolvia, y en la terminal HioPos mostraba un error de modulo externo.
    /// Tiene sentido desde el POS: una Activity que vuelve con RESULT_CANCELED y
    /// sin un solo extra es indistinguible de un modulo que se murio. El cajero no
    /// habia hecho nada raro —solo se arrepintio del medio de pago— y ese cartel
    /// tecnico lo asustaba.
    ///
    /// El contrato de ICG (doc §4) NO tiene un TransactionResult "CANCELED": los
    /// unicos tres valores son ACCEPTED, FAILED y UNKNOWN_RESULT. Asi que la forma
    /// correcta de decir "no cobre nada, segui con lo tuyo" es RESULT_OK + FAILED
    /// con NUESTRO titulo y mensaje. HioPos aborta el medio de pago igual, pero el
    /// cartel que ve el cajero dice que el modulo se cerro y que no hubo cobro.
    ///
    /// FAILED es tambien el unico valor seguro de los tres: UNKNOWN_RESULT le dice
    /// al POS que el cobro PUDO haber ocurrido, y en este camino sabemos con
    /// certeza que no ocurrio (se sale antes de llamar a Credinet).
    /// </summary>
    /// <param name="transactionType">
    /// El TransactionType que HioPos envio (SALE / REFUND). Se hace eco porque el
    /// POS lo usa para saber que operacion esta cerrando; si no vino, se omite.
    /// </param>
    public HioposResponse BuildTransactionCanceledByUser(string? transactionType = null)
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),

            // El eco NUNCA se omite.
            //
            // Antes este campo se agregaba solo si venia un tipo, y [HioposExit] lo
            // lee de ActiveTransaction: si el estado ya estaba limpio, la respuesta
            // salia SIN TransactionType. Para el POS eso es igual de malo que un tipo
            // equivocado —no da la operacion por cerrada— y el cajero terminaba viendo
            // el error generico de modulo externo justo despues de tocar
            // "Volver a HioPos". El default es SALE porque este boton solo existe en
            // el flujo de venta.
            [HioposExtras.TransactionType] =
                string.IsNullOrWhiteSpace(transactionType) ? "SALE" : transactionType,

            // Texto para el cajero, no para el que programa: dice que paso y que
            // puede hacer. "Modulo cerrado" describia nuestra implementacion.
            [HioposExtras.ErrorMessageTitle] = "Pago cancelado",
            [HioposExtras.ErrorMessage] =
                "Volviste a HioPos sin realizar el cobro. Podes elegir otro medio de " +
                "pago o intentarlo de nuevo."
        };

        return new HioposResponse(HioposActions.Transaction, extras, ResultCode: Hiopos.Result.OkValue);
    }

    /// <summary>
    /// Responde a una action no soportada (READ_CARD / CHARGE_CARD /
    /// GET_CARD_DATA en Sistecredito) con RESULT_CANCELED (B6).
    ///
    /// OJO: esto NO es lo que se usa cuando el cajero se sale de una venta — para
    /// eso esta [BuildTransactionCanceledByUser]. Aca RESULT_CANCELED es correcto
    /// porque la action ni siquiera existe en este modulo.
    /// </summary>
    public HioposResponse BuildCanceled(string action) =>
        new(action, new Dictionary<string, string?>(), ResultCode: Hiopos.Result.CanceledValue);
}
