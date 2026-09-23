namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Constantes del contrato HioPosCloud (ICG Software).
///
/// CONVENCION: action = "icg.actions.electronicpayment.{apk_name}.XXX".
///
/// {apk_name} = "permoda" = "APK Name" registrado en ICG CloudLicense
/// (confirmado por Permoda el 2026-07-23). DEBE ser identico al apk_name
/// del alta en HioPosCloud: HioPos enruta los intents con ese nombre;
/// si no coincide, el modulo NUNCA recibe la accion, HI-POS hace
/// timeout, y se "saca la factura sin hacer nada" porque interpreta
/// que el modulo TEF no respondio.
///
/// Si ICG reasigna el apk_name, cambiar SOLO esta constante y
/// rebuildear; el AndroidManifest, MainActivity y este archivo se
/// generan desde aqui.
///
/// Convencion para evitar drift: NO escribir literales como
/// "icg.actions.electronicpayment.permoda.TRANSACTION" en otros
/// archivos. Siempre usar HioposActions.Transaction.
///
/// Ademas se incluye el nombre del Broadcast de auditoria, que NO usa
/// {apk_name} (doc §14.2).
/// </summary>
public static class HioposActions
{
    /// <summary>
    /// apk_name registrado en ICG CloudLicense. Es la LLAVE DE ENRUTAMIENTO: HioPos
    /// despacha los intents con este nombre embebido en la accion.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// Otro modulo del mismo cliente, <c>com.permoda.tefogloba</c>, declara las
    /// MISMAS acciones <c>icg.actions.electronicpayment.permoda.*</c>. Verificado en
    /// el terminal: los intents de HioPos resolvian a uno u otro de forma
    /// impredecible segun cual se hubiera instalado ultimo, porque Android tiene dos
    /// candidatos para el mismo intent implicito.
    ///
    /// Con un apk_name propio cada modulo tiene su espacio de acciones y pueden
    /// convivir en la misma terminal.
    ///
    /// REQUISITO: ICG tiene que registrar este apk_name en HioPosCloud. Si no
    /// coincide con el alta, el intent NUNCA llega, HioPos hace timeout y la venta
    /// sale sin cobrar.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE HOY DICE "permoda" Y NO "sistecredito"
    /// ─────────────────────────────────────────────────────────────────────────
    /// Se cambio a "sistecredito" y el modulo dejo de levantarse. Verificado en el
    /// terminal:
    ///
    ///   icg.actions.electronicpayment.permoda.TRANSACTION       -> nadie
    ///   icg.actions.electronicpayment.sistecredito.TRANSACTION  -> este modulo
    ///
    /// HioPos sigue despachando a "permoda" porque es lo que tiene registrado en
    /// HioPosCloud, asi que disparaba al vacio y hacia timeout.
    ///
    /// Se vuelve a "permoda" para poder seguir validando en terminal. CAMBIAR A
    /// "sistecredito" RECIEN CUANDO ICG CONFIRME EL ALTA: es un cambio de una
    /// linea y las 11 acciones se recalculan solas.
    ///
    /// OJO mientras siga en "permoda": com.permoda.tefogloba declara las MISMAS
    /// acciones. Con los dos instalados, cual atiende lo decide Android de forma
    /// impredecible. En la terminal de pruebas tiene que estar solo uno.
    /// </summary>
    public const string ApkName = "permoda";

    /// <summary>
    /// Version del modulo que se reporta en GET_VERSION.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// ES UN VALOR DE CONTRATO, NO LA VERSION DEL BUILD
    /// ─────────────────────────────────────────────────────────────────────────
    /// HioPos guarda la version del modulo y, cuando GET_VERSION le devuelve algo
    /// distinto de lo que tiene registrado, ofrece "actualizar el modulo" al
    /// arrancar. Y lo pide en CADA arranque, porque el desajuste no se puede
    /// resolver: el modulo es side-loaded, HioPos no tiene de donde bajar un APK,
    /// asi que aceptar el dialogo no cambia lo que tiene anotado.
    ///
    /// Antes esta constante era solo un fallback: HandleGetVersion prefería
    /// IAppInfo.VersionString, o sea el versionName del APK. Como el versionName
    /// sube en cada release, la version que HioPos veia cambiaba en cada release y
    /// el dialogo de actualizacion aparecia para siempre. Se observo en terminal al
    /// pasar de versionName 1.0.8 a 1.0.
    ///
    /// Por eso ahora es la UNICA fuente y no se deriva del build: el versionName y
    /// el versionCode pueden moverse libremente sin molestar a HioPos.
    ///
    /// Para cambiarla hay que coordinar con ICG: tiene que coincidir con la version
    /// registrada para el modulo "permoda" en HioPosCloud.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ES UN int Y NO UN string
    /// ─────────────────────────────────────────────────────────────────────────
    /// Porque HioPos lo lee con getIntExtra. Mientras se envio como cadena, HioPos
    /// se quedaba con el valor por defecto y creia que el modulo estaba en la
    /// version -1. Capturado en logcat del terminal:
    ///
    ///   W/Bundle: Key Version expected Integer but value was a java.lang.String.
    ///             The default value -1 was returned.
    ///
    /// Ese era el verdadero motivo del dialogo de "actualizar el modulo" en cada
    /// arranque, y explica por que cambiar el CONTENIDO no servia de nada: se
    /// probo "1.0.0", "1.0" y "1", y las tres fallaban igual porque el problema
    /// era el TIPO.
    ///
    /// El valor 1 es el que ICG tiene registrado para el modulo "permoda" en
    /// HioPosCloud. El versionCode del APK sigue subiendo aparte; es interno de
    /// Android y HioPos no lo compara contra el registro.
    /// </summary>
    public const int ModuleVersion = 1;

    private const string Prefix = "icg.actions.electronicpayment." + ApkName + ".";

    public const string Initialize      = Prefix + "INITIALIZE";
    public const string Finalize        = Prefix + "FINALIZE";
    public const string GetBehavior     = Prefix + "GET_BEHAVIOR";
    public const string GetVersion      = Prefix + "GET_VERSION";
    public const string ShowSetupScreen = Prefix + "SHOW_SETUP_SCREEN";
    public const string Transaction     = Prefix + "TRANSACTION";
    public const string GetCustomParams = Prefix + "GET_CUSTOM_PARAMS";
    public const string GetPrintInfo    = Prefix + "GET_PRINT_INFO";
    public const string ReadCard        = Prefix + "READ_CARD";
    public const string ChargeCard      = Prefix + "CHARGE_CARD";
    public const string GetCardData     = Prefix + "GET_CARD_DATA";

    /// <summary>
    /// LA OTRA PUERTA POR LA QUE SE CUELAN LOS ABONOS.
    ///
    /// Con <c>ExecuteVoidWhenAvailable = true</c> en GET_BEHAVIOR, HioPos manda los
    /// abonos como VOID_TRANSACTION en lugar de REFUND — y el rechazo, que vive en el
    /// caso REFUND, deja de correr. Ya paso: se colaron cuatro abonos por ahi y se
    /// respondieron ACCEPTED en silencio.
    ///
    /// Nuestra bandera esta en false, pero mientras quede una sola puerta abierta
    /// basta un cambio de configuracion del POS para que todo se cuele. Asi que se
    /// declara la accion y se RECHAZA SIEMPRE, sin depender de la bandera.
    /// </summary>
    public const string VoidTransaction = Prefix + "VOID_TRANSACTION";

    public const string ExternalAudit = "icg.actions.externalApi.AUDIT";
}

/// <summary>
/// Valores del extra <c>TransactionType</c> que el modulo DEVUELVE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CASH_IN NO ESTA EN EL MANUAL. SALIO DEL APK DE HIOPOS.
/// ─────────────────────────────────────────────────────────────────────────────
/// El manual de Cobro Electronico 4.0 lista siete tipos (SALE, NEGATIVE_SALE,
/// REFUND, ADJUST_TIPS, VOID_TRANSACTION, QUERY_TRANSACTION, BATCH_CLOSE) y
/// ninguno sirve para que una ENTRADA DE CAJA quede con el importe abonado.
///
/// Desensamblando el APK instalado en el terminal (icg.android.start 15.9.0.0,
/// classes6.dex) aparece el que si sirve, en
/// <c>icg.android.cashTransaction.CashTransactionActivity.onExternalModuleResult()</c>:
///
///     paymentMean.setAmount(response.getAmount());          // siempre
///     if (type.equals("CASH_IN") || type.equals("CASH_OUT"))
///     {
///         paymentMean.setNetAmount(response.getAmount());   // el importe REAL
///         controller.sendDocumentChange();                  // refresca la pantalla
///     }
///
/// Con SALE —que es lo que el POS pregunta y lo que se le contestaba— solo corre
/// el primer setAmount, que es el importe ENTREGADO. De ahi salia la pantalla que
/// el cajero veia: importe $1, entregado $99.900 y un vuelto de $99.899 que el
/// arqueo esperaba del cajon.
///
/// El guard de arriba del mismo metodo acepta explicitamente CASH_IN, asi que no
/// es un valor que se cuele: es el que ese flujo espera.
/// </summary>
public static class HioposTransactionTypes
{
    public const string Sale   = "SALE";
    public const string CashIn = "CASH_IN";
}

/// <summary>
/// Nombres exactos de los extras (string keys) del Intent de HioPosCloud.
/// 1:1 con doc §4. Cualquier cambio aqui debe acompanarse de un cambio
/// en [HioposIntentParser] y [HioposResultBuilder].
/// </summary>
public static class HioposExtras
{
    // ---- Input extras (INITIALIZE / SHOW_SETUP_SCREEN) ----
    public const string Parameters        = "Parameters";
    public const string Token             = "Token";
    public const string LanguageIso       = "LanguageISO";
    public const string IsReadOnly        = "isReadOnly";

    // ---- Input extras (TRANSACTION) ----
    public const string TransactionType        = "TransactionType";
    public const string TenderType             = "TenderType";
    public const string CurrencyIso            = "CurrencyISO";
    public const string Amount                 = "Amount";
    public const string TipAmount              = "TipAmount";
    public const string TaxAmount              = "TaxAmount";
    public const string TaxDetail              = "TaxDetail";
    public const string TransactionId          = "TransactionId";
    public const string TransactionData        = "TransactionData";
    public const string ReceiptPrinterColumns  = "ReceiptPrinterColumns";
    public const string ShopData               = "ShopData";
    public const string SellerData             = "SellerData";
    public const string DocumentData           = "DocumentData";
    public const string DocumentPath           = "DocumentPath";
    public const string IsAdvancedPayment      = "IsAdvancedPayment";
    public const string OverPaymentType        = "OverPaymentType";
    public const string SurchargeAmount        = "SurchargeAmount";

    // ---- Output extras (GET_BEHAVIOR) ----
    public const string SupportsTransactionVoid    = "SupportsTransactionVoid";
    public const string SupportsTransactionQuery   = "SupportsTransactionQuery";
    public const string SupportsNegativeSales      = "SupportsNegativeSales";
    public const string SupportsPartialRefund      = "SupportsPartialRefund";
    public const string SupportsBatchClose         = "SupportsBatchClose";
    public const string SupportsTipAdjustment      = "SupportsTipAdjustment";
    public const string SupportsCredit             = "SupportsCredit";
    public const string SupportsDebit              = "SupportsDebit";
    public const string SupportsEBTFoodstamp       = "SupportsEBTFoodstamp";
    public const string HasCustomParams            = "HasCustomParams";
    public const string CanChargeCard              = "CanChargeCard";
    public const string CanAudit                   = "canAudit";
    public const string ExecuteVoidWhenAvailable   = "ExecuteVoidWhenAvailable";
    public const string SaveLoyaltyCardNum         = "SaveLoyaltyCardNum";
    public const string CanPrint                   = "CanPrint";
    public const string ReadCardFromApi            = "ReadCardFromApi";
    public const string OnlyUseDocumentPath        = "OnlyUseDocumentPath";

    // ══════════════════════════════════════════════════════════════════════════
    // CAPACIDADES QUE HIOPOS RECONOCE Y EL MODULO NO DECLARABA
    // ══════════════════════════════════════════════════════════════════════════
    // Estas cuatro NO estan en el manual de Cobro Electronico 4.0 que tenemos,
    // pero SI estan en el propio HioPos: se leyeron del APK instalado en el
    // terminal (icg.android.start), en el mismo bloque de cadenas donde viven las
    // 17 que ya declarabamos:
    //
    //   SupportsBatchClose      SupportsCashTransaction   SupportsCredit
    //   SupportsDebit           SupportsEBTFoodstamp      SupportsNegativeSales
    //   SupportsPartialRefund   SupportsSale              SupportsTipAdjustment
    //   SupportsTransactionQuery SupportsTransactionVoid  SupportsVoid
    //
    // Se agregan porque [SupportsCashTransaction] es la unica pista concreta que
    // encontramos para el flujo de ENTRADA DE CAJA: HioPos tiene un subsistema
    // completo icg.android.cashTransaction (Activity, Controller, Editor,
    // DefaultValuesLoader, Generator) y esta bandera es la que le dice que el
    // modulo sabe atender transacciones de caja. Sin declararla, el modulo se
    // comporta como un medio de pago comun y el POS no le ofrece nada distinto.
    //
    // ES UN EXPERIMENTO, y por eso queda anotado: si HioPos no cambia de
    // comportamiento, estas cuatro no molestan (una capacidad declarada que el POS
    // no usa es inerte). Si empieza a mandar una accion nueva, el 'default' de
    // [MainActivity] la responde Canceled en vez de colgarse.
    public const string SupportsCashTransaction    = "SupportsCashTransaction";
    public const string SupportsSale               = "SupportsSale";
    public const string SupportsVoid               = "SupportsVoid";
    public const string SupportOverPayment         = "SupportOverPayment";

    // ---- Output extras (GET_VERSION) ----
    public const string Version = "Version";

    // ---- Output extras (GET_CUSTOM_PARAMS) ----
    public const string Name = "Name";
    public const string Logo = "Logo";

    // ---- Output extras (GET_PRINT_INFO) ----
    public const string PackageName     = "PackageName";
    public const string HorizontalDots  = "HorizontalDots";

    // ---- Output extras (TRANSACTION) ----
    public const string TransactionResult      = "TransactionResult";
    public const string ErrorMessage           = "ErrorMessage";
    public const string ErrorMessageTitle      = "ErrorMessageTitle";
    public const string MerchantReceipt        = "MerchantReceipt";
    public const string CustomerReceipt        = "CustomerReceipt";
    public const string BatchNumber            = "BatchNumber";
    public const string BatchReceipt           = "BatchReceipt";
    public const string ReceiptFailed          = "ReceiptFailed";
    public const string AuthorizationId        = "AuthorizationId";
    public const string CardHolder             = "CardHolder";
    public const string CardType               = "CardType";
    public const string CardNum                = "CardNum";
    public const string SignatureImage         = "SignatureImage";
    public const string ModifyDocumentResult   = "ModifyDocumentResult";
    public const string FixedPaymentMeanId     = "FixedPaymentMeanId";
    public const string FixedPaymentMeanAmount = "FixedPaymentMeanAmount";

    // ---- Audit Broadcast extras (doc §7.1) ----
    public const string AuditAction    = "Action";
    public const string AuditComment   = "Comment";
    public const string AuditToken     = "Token";
    public const string AuditDocumentId = "DocumentId";
}

/// <summary>
/// Capacidades declaradas por el modulo TEF en GET_BEHAVIOR (doc §3).
/// El POS consulta estas flags antes de mostrar opciones al cajero.
/// </summary>
public static class HioposCapabilities
{
    public const bool SupportsTransactionVoid    = false;
    public const bool SupportsTransactionQuery   = false;
    public const bool SupportsNegativeSales      = false;
    public const bool SupportsPartialRefund      = false;
    public const bool SupportsBatchClose         = false;
    public const bool SupportsTipAdjustment      = false;
    public const bool SupportsCredit             = true;
    public const bool SupportsDebit              = false;
    public const bool SupportsEBTFoodstamp       = false;
    public const bool HasCustomParams            = true;
    public const bool CanChargeCard              = false;
    public const bool CanAudit                   = true;
    public const bool ExecuteVoidWhenAvailable   = false;
    public const bool SaveLoyaltyCardNum         = false;
    public const bool CanPrint                   = false;
    public const bool ReadCardFromApi            = false;
    // false: HioPos envia el documento INLINE en el extra DocumentData (no por
    // fichero). En Android 13+ (scoped storage) la app NO puede leer la ruta
    // /storage/emulated/0/Documents/... (Permission denied) -> ActiveDocument
    // quedaba null. La doc dice que OnlyUseDocumentPath=true solo hace falta si
    // el XML supera 1 MB; una factura normal no lo hace, asi que leemos inline.
    public const bool OnlyUseDocumentPath        = false;
}
