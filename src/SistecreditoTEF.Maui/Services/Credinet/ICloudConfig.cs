namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Acceso a los parámetros que ICG provisiona en CloudLicense y que HioPosCloud
/// entrega en el Intent INITIALIZE.
///
/// QA C-3 / M-2: antes [ApiConfig] llamaba directamente al estático
/// <c>CloudConfigStore</c>, que usa <c>Preferences</c> de MAUI. Eso ataba la
/// precedencia de configuración —la regla de negocio más delicada del módulo,
/// la que decide si el POS habla con producción o con sandbox— a una API de
/// plataforma imposible de testear. Con esta interfaz la precedencia se prueba
/// en xUnit con un doble.
/// </summary>
public interface ICloudConfig
{
    /// <summary>Valor del parámetro, o null si no llegó.</summary>
    string? Get(string key);

    /// <summary>Claves reconocidas (nombres tal como los define ICG).</summary>
    public const string ApiBaseUrl      = "API_BASE_URL";
    public const string SubscriptionKey = "SUBSCRIPTION_KEY";
    public const string StoreId         = "STORE_ID";
    public const string Environment     = "ENVIRONMENT";
    public const string OtpDestination  = "OTP_DESTINATION";
    public const string CertificatePins = "CERTIFICATE_PINS";
    public const string Frequency       = "FREQUENCY";
    public const string Source          = "SOURCE";
    public const string AuthMethod      = "AUTH_METHOD";
    public const string TimeoutSeconds  = "TIMEOUT_SECONDS";
    public const string OtpMaxResends   = "OTP_MAX_RESENDS";
    public const string StoreName       = "STORE_NAME";

    /// <summary>
    /// Id del medio de pago con el que se registra un RECAUDO (entrada de caja).
    ///
    /// Un abono se cobra en EFECTIVO: la plata entra al cajón, no a una tarjeta.
    /// Este id es el que se manda en <c>FixedPaymentMeanId</c> para que HioPos
    /// deje la entrada de caja en el medio correcto.
    ///
    /// Antes iba fijo en "2" —el medio tarjeta de las ventas— hardcodeado en dos
    /// ViewModels. Con un id equivocado HioPos no actualiza el importe y la
    /// entrada queda con el "1" que el cajero escribió para habilitar el botón.
    ///
    /// Default "1": la hoja PARAMETROS-SISTECREDITO del Excel de credenciales
    /// declara <c>SitC_RecMedioPago = 1</c> para el grupo "SisteCredito
    /// Recaudos". Si en una tienda el efectivo tiene otro id, se corrige por acá
    /// sin recompilar.
    /// </summary>
    public const string PaymentMeanIdRecaudo = "PAYMENT_MEAN_ID_RECAUDO";

    /// <summary>
    /// Id del medio de pago sobre el que se consolida Sistecredito en una VENTA.
    /// Default "2" (el medio "Tarjeta"), que es el valor con el que se validó.
    /// </summary>
    public const string PaymentMeanIdVenta = "PAYMENT_MEAN_ID_VENTA";
}

/// <summary>Implementación vacía para tests y para plataformas sin CloudLicense.</summary>
public sealed class EmptyCloudConfig : ICloudConfig
{
    public static readonly EmptyCloudConfig Instance = new();
    public string? Get(string key) => null;
}
