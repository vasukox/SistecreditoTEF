using System.Xml.Linq;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973 (Opción A): parámetros que ICG carga en CloudLicense y que HioPosCloud
/// entrega en el Intent INITIALIZE como XML en el extra "Parameters":
///
///   &lt;Configuration&gt;&lt;Parameters&gt;
///     &lt;Param Key="API_BASE_URL"&gt;https://api.credinet.co/posprod/&lt;/Param&gt;
///     &lt;Param Key="SUBSCRIPTION_KEY"&gt;...&lt;/Param&gt;
///     &lt;Param Key="STORE_ID"&gt;...&lt;/Param&gt;
///     &lt;Param Key="ENVIRONMENT"&gt;production&lt;/Param&gt;
///     &lt;Param Key="OTP_DESTINATION"&gt;0&lt;/Param&gt;
///   &lt;/Parameters&gt;&lt;/Configuration&gt;
///
/// Se persisten en Preferences (almacenamiento privado de la app) para que la
/// configuración sobreviva entre el INITIALIZE (arranque del POS) y las
/// transacciones posteriores. [ApiConfig] los superpone sobre appsettings.json.
///
/// Nota de seguridad: Preferences es privado de la app pero NO cifrado. La
/// SUBSCRIPTION_KEY vive aquí; en un POS controlado es aceptable. Si se requiere
/// mayor protección, migrar a SecureStorage (implica una ruta async en el arranque).
/// </summary>
public static class CloudConfigStore
{
    private const string Prefix = "cloudparam_";

    public const string ApiBaseUrl      = "API_BASE_URL";
    public const string SubscriptionKey = "SUBSCRIPTION_KEY";
    public const string StoreId         = "STORE_ID";
    public const string Environment     = "ENVIRONMENT";
    public const string OtpDestination  = "OTP_DESTINATION";

    /// <summary>Parsea el XML de Parameters (INITIALIZE) y lo persiste.</summary>
    public static void SaveFromXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        try
        {
            var doc = XDocument.Parse(xml);
            var count = 0;
            foreach (var p in doc.Descendants("Param"))
            {
                var key = (string?)p.Attribute("Key");
                if (string.IsNullOrWhiteSpace(key)) continue;
                Preferences.Set(Prefix + key.Trim(), p.Value?.Trim() ?? string.Empty);
                count++;
            }
            AppLogger.I("CloudConfigStore", $"Parámetros Cloud guardados: {count}");
        }
        catch (Exception ex)
        {
            // XML malformado o ausente: se ignora y se usan los defaults de appsettings.
            AppLogger.W("CloudConfigStore", $"Parameters XML inválido, se ignora: {ex.Message}");
        }
    }

    /// <summary>Valor persistido de un parámetro Cloud, o null si no existe.</summary>
    public static string? Get(string key)
    {
        var v = Preferences.Get(Prefix + key, string.Empty);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
}
