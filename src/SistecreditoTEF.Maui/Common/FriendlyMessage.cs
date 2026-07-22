using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Traduce [ApiError] (HTTP/red/business) a mensajes amigables para el
/// cajero en la pantalla. Evita jerga tecnica, codigos HTTP, prefijos
/// como "CREDINET:" y deja saber que hacer al usuario.
///
/// DRY: se usa desde todos los VMs que muestran error al usuario
/// (CapturaCedula, Pago, Otp, CreditosActivos, etc.).
/// </summary>
public static class FriendlyMessage
{
    public static string FromApiError(ApiError error, string? contexto = null)
    {
        var prefix = contexto is null ? "" : $"{Capitalize(contexto)}: ";

        return error switch
        {
            ApiError.Network =>
                $"{prefix}Sin conexion. Verifica la red e intenta de nuevo.",

            ApiError.Http { Code: 401 } =>
                $"{prefix}No autorizado. La clave del servicio no es valida. Contacta al supervisor.",

            ApiError.Http { Code: 403 } =>
                $"{prefix}Acceso denegado. No tienes permisos para esta operacion.",

            ApiError.Http { Code: 404 } =>
                $"{prefix}Recurso no encontrado. Intenta de nuevo o contacta soporte.",

            ApiError.Http { Code: 429 } =>
                $"{prefix}Demasiadas solicitudes. Espera unos segundos e intenta de nuevo.",

            ApiError.Http { Code: 500 or 502 or 503 or 504 } =>
                $"{prefix}El servidor no responde. Intenta en un momento.",

            ApiError.Http http =>
                $"{prefix}Error de comunicacion con el servidor (codigo {http.Code}). " +
                $"Si persiste, contacta soporte.",

            ApiError.Business { Code: 224 } =>
                $"{prefix}No encontramos un cliente con ese documento. " +
                $"Verifica el numero o prueba con otro tipo de documento.",

            ApiError.Business { Code: 225 } =>
                $"{prefix}El documento ingresado no es valido. Revisa e intenta de nuevo.",

            ApiError.Business { Code: 229 } =>
                $"{prefix}El codigo OTP no es valido o ya expiro. " +
                $"Solicita uno nuevo con el boton Reenviar.",

            ApiError.Business { Code: 230 } =>
                $"{prefix}El cliente no tiene cupo disponible para este monto.",

            ApiError.Business { Code: 231 } =>
                $"{prefix}El monto excede el limite del cliente. " +
                $"Prueba con un monto menor.",

            ApiError.Business { Code: 252 } =>
                $"{prefix}Esta solicitud ya fue procesada. Revisa la pantalla de confirmacion.",

            ApiError.Business { Code: 404 } =>
                $"{prefix}No encontramos el credito. Verifica que el numero este bien escrito.",

            // Cualquier otro error de negocio: limpia el prefijo "CREDINET:" que
            // viene del mensaje crudo y capitaliza.
            ApiError.Business biz => $"{prefix}{Clean(biz.UserMessage)}",

            _ => $"{prefix}Algo salio mal. Intenta de nuevo."
        };
    }

    /// <summary>
    /// Mensajes de validacion local (antes de pegarle al backend). Mas cortos,
    /// en imperativo: "Ingresa el documento", etc.
    /// </summary>
    public static string Validation(string key) => key switch
    {
        "doc.empty"      => "Ingresa el numero de documento",
        "doc.short"      => "El documento es muy corto. Revisa e intenta de nuevo.",
        "doc.invalid"    => "El documento no es valido. Solo numeros, por favor.",
        "monto.empty"    => "Ingresa el monto a pagar",
        "monto.range"    => "El monto esta fuera del rango permitido para este credito",
        "cedula.empty"   => "Ingresa la cedula del cliente",
        "cedula.short"   => "La cedula es muy corta. Revisa e intenta de nuevo.",
        "cedula.invalid" => "La cedula no es valida. Solo numeros, por favor.",
        _                => Capitalize(key)
    };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>
    /// Quita prefijos tecnicos que Credinet antepone: "CREDINET:",
    /// "HTTP 400 -", etc. Capitaliza la primera letra.
    /// </summary>
    private static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Algo salio mal. Intenta de nuevo.";

        var cleaned = raw;
        // Quita prefijos conocidos.
        foreach (var prefix in new[] { "CREDINET:", "Credinet:", "HTTP ", "ERROR:" })
        {
            var idx = cleaned.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                cleaned = cleaned[(idx + prefix.Length)..].TrimStart();
        }
        // Quita prefijos tipo "228 -" o "[228]".
        if (cleaned.Length > 0 && char.IsDigit(cleaned[0]))
        {
            var i = 0;
            while (i < cleaned.Length && (char.IsDigit(cleaned[i]) || cleaned[i] == '-' || cleaned[i] == ']' || cleaned[i] == '[' || cleaned[i] == ' '))
                i++;
            if (i > 0 && i < cleaned.Length)
                cleaned = cleaned[i..].TrimStart(' ', '-', ':');
        }
        return Capitalize(cleaned);
    }
}