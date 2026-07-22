namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Constantes del contrato HioPosCloud (ICG Software).
///
/// CONVENCION: action = "icg.actions.electronicpayment.{apk_name}.XXX".
///
/// {apk_name} = "sistecredito" = "APK Name (namespace)" registrado en ICG.
/// DEBE ser identico al namespace del alta en HioPosCloud: HioPos enruta los
/// intents con ese nombre; si no coincide, el modulo nunca recibe la accion
/// ("error modulo externo"). Si ICG asigna otro nombre, cambiar SOLO esta
/// constante y rebuildear; el AndroidManifest, MainActivity y este archivo se
/// generan desde aqui.
///
/// Convencion para evitar drift: NO escribir literales como
/// "icg.actions.electronicpayment.sistecredito.TRANSACTION" en otros
/// archivos. Siempre usar HioposActions.Transaction.
///
/// Ademas se incluye el nombre del Broadcast de auditoria, que NO usa
/// {apk_name} (doc §14.2).
/// </summary>
public static class HioposActions
{
    public const string ApkName = "sistecredito";

    /// <summary>
    /// Version del modulo que se reporta en GET_VERSION.
    /// DEBE ser identica a [ApplicationDisplayVersion] del .csproj (versionName).
    /// Se usa como fallback determinista: si HioPos ve un string distinto entre
    /// arranques, cree que el modulo cambio y propone "actualizar". Mantener fija
    /// y solo subirla en un release real (junto con ApplicationVersion=versionCode).
    /// </summary>
    public const string ModuleVersion = "1.0.0";

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

    public const string ExternalAudit = "icg.actions.externalApi.AUDIT";
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
