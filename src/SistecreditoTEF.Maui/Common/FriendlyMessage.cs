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

            // ─────────────────────────────────────────────────────────────────────
            // TABLA OFICIAL DEL MANUAL (M-SCL-03 v05, §4.2.4)
            // ─────────────────────────────────────────────────────────────────────
            // Cada caso lleva el NOMBRE del codigo en el comentario para poder
            // auditarlo contra el manual sin adivinar. Dos de estos mapeos estaban
            // MAL y mandaban al cajero a arreglar lo que no era:
            //
            //   225 decia "el documento no es valido" -> es StoreNotFound, o sea un
            //       problema de configuracion de la TIENDA. El cajero reescribia la
            //       cedula del cliente para siempre.
            //   231 decia "el monto excede el limite" -> es CreditsNotFound. A un
            //       cliente sin creditos activos se le pedia bajar el monto.
            //
            // El de cupo insuficiente es el 221, que no estaba mapeado.

            // 224 CustomerNotFound
            ApiError.Business { Code: 224 } =>
                $"{prefix}No encontramos un cliente con ese documento. " +
                $"Verifica el numero o prueba con otro tipo de documento.",

            // 221 NotAvailableCreditLimit
            ApiError.Business { Code: 221 } =>
                $"{prefix}El cliente no tiene cupo suficiente para este monto. " +
                $"Proba con un monto menor.",

            // 222 MonthsNumberNotValid
            ApiError.Business { Code: 222 } =>
                $"{prefix}Ese plazo no aplica para este monto. Elegi uno de los plazos " +
                $"que aparecen en la pantalla.",

            // 223 RequestValuesInvalid
            ApiError.Business { Code: 223 } =>
                $"{prefix}Los datos enviados no son validos. Revisa el monto y el plazo.",

            // 225 StoreNotFound — NO es un problema del documento del cliente.
            ApiError.Business { Code: 225 } =>
                $"{prefix}Esta tienda no esta registrada en Sistecredito. " +
                $"Avisa al area de sistemas: es configuracion del POS, no del cliente.",

            // 226 CustomerProfileInvalid
            ApiError.Business { Code: 226 } =>
                $"{prefix}Los datos del cliente en Sistecredito estan incompletos. " +
                $"El cliente debe actualizarlos con Sistecredito.",

            // 227 SourceNotFound · 228 AuthMethodNotFound — configuracion del modulo.
            ApiError.Business { Code: 227 or 228 } =>
                $"{prefix}El modulo no esta configurado correctamente en este POS. " +
                $"Avisa al area de sistemas.",

            // 231 CreditsNotFound
            ApiError.Business { Code: 231 } =>
                $"{prefix}Este cliente no tiene creditos activos en esta tienda. " +
                $"Verifica el documento o consulta con Sistecredito.",

            // 232 CustomerNotActive
            ApiError.Business { Code: 232 } =>
                $"{prefix}El cliente no esta activo en Sistecredito. " +
                $"No se puede operar hasta que lo reactiven.",

            // 233 CreditNotActive
            ApiError.Business { Code: 233 } =>
                $"{prefix}Ese credito no esta activo. Elegi otro o consulta el estado " +
                $"con Sistecredito.",

            // 245 CustomerIsDefaulter
            ApiError.Business { Code: 245 } =>
                $"{prefix}El cliente esta en mora. No se puede otorgar un credito nuevo " +
                $"hasta que se ponga al dia.",

            // 555 NotControlledException — falla del lado de Sistecredito.
            ApiError.Business { Code: 555 } =>
                $"{prefix}Sistecredito tuvo un error interno. Intenta de nuevo en un " +
                $"momento; si persiste, avisa al area de sistemas.",

            // 220 = InvalidAmountCredit (getCreditToken) y 1104 = NoOfferAvailable
            // (getSimulatedMonthLimit) son LA MISMA causa con dos nombres: el cliente
            // no tiene una oferta de credito que cubra ese monto. Verificado contra el
            // sandbox con el mismo cliente y el mismo monto.
            //
            // El plazo NO tiene nada que ver, y eso importa: el cajero veia
            // "InvalidAmountCredit" en la pantalla del OTP y probaba 3, 2 y 1 mes,
            // fallando las tres veces sin entender por que.
            // NO se culpa al cliente ni a su cupo: getSimulatedMonthLimit, que es
            // quien decide esto, ni recibe la cedula, y el corte cae en el mismo monto
            // exacto para cualquier documento. Un cliente con cupo de sobra es
            // rechazado igual, y decirle "no tiene oferta" mandaba a revisar el cupo.
            ApiError.Business { Code: 220 or 1104 } =>
                $"{prefix}Sistecredito no financia ese monto (no hay plan de credito " +
                $"para ese valor). No es el cupo del cliente, y cambiar el plazo no lo " +
                $"resuelve: proba un monto menor o cobra con otro medio de pago.",

            ApiError.Business { Code: 229 } =>
                $"{prefix}El codigo no es valido o ya expiro. " +
                $"Solicita uno nuevo con el boton Reenviar codigo.",

            // 230 = TokenAlreadyUsed. Confirmado en el terminal:
            //   HTTP 400 {"errorCode":230,"message":"TokenAlreadyUsed"}
            // Antes este codigo se traducia como "el cliente no tiene cupo
            // disponible", que no tiene nada que ver: mandaba al cajero a buscar un
            // problema de cupo cuando lo unico que hacia falta era pedir otro codigo.
            ApiError.Business { Code: 230 } =>
                $"{prefix}Ese codigo ya fue usado. Toca “Reenviar codigo” para " +
                $"recibir uno nuevo.",

            // Aca vivia otro caso para el 231 que decia "el monto excede el limite del
            // cliente. Prueba con un monto menor". Se elimino: el 231 es
            // CreditsNotFound y ya esta mapeado arriba con su significado real. El de
            // cupo insuficiente es el 221.

            ApiError.Business { Code: 252 } =>
                $"{prefix}Esta solicitud ya fue procesada. Revisa la pantalla de confirmacion.",

            ApiError.Business { Code: 404 } =>
                $"{prefix}No encontramos el credito. Verifica que el numero este bien escrito.",

            // Cualquier otro error de negocio: se intenta traducir el mensaje que
            // manda Credinet (viene en ingles, tipo "TokenAlreadyUsed") antes de
            // caer al texto crudo. Asi un codigo que todavia no tenemos mapeado no
            // deja al cajero leyendo ingles tecnico.
            ApiError.Business biz => $"{prefix}{TraducirMensaje(biz.Message) ?? Clean(biz.UserMessage)}",

            // QA: error local de la app. El mensaje ya viene redactado para el
            // cajero, con la accion que debe tomar; no se le agrega jerga.
            ApiError.Local local => $"{prefix}{Capitalize(local.Message)}",

            _ => $"{prefix}Algo salio mal. Intenta de nuevo."
        };
    }

    /// <summary>
    /// Traduce los mensajes que Credinet devuelve en inglés. Es la red de
    /// seguridad para códigos de error que todavía no están mapeados: el cajero lee
    /// una instrucción en español en vez de "TokenAlreadyUsed".
    ///
    /// Devuelve null si el mensaje no se reconoce, para que el llamador use el
    /// texto crudo limpiado.
    /// </summary>
    private static string? TraducirMensaje(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return null;

        var m = mensaje.Trim();

        if (Contiene(m, "TokenAlreadyUsed"))
            return "Ese codigo ya fue usado. Toca “Reenviar codigo” para recibir uno nuevo.";
        if (Contiene(m, "TokenExpired"))
            return "El codigo expiro. Toca “Reenviar codigo” para recibir uno nuevo.";
        if (Contiene(m, "InvalidToken") || Contiene(m, "TokenInvalid"))
            return "El codigo no es valido. Revisalo o pedi uno nuevo.";
        if (Contiene(m, "TokenNotGenerated"))
            return "No se pudo generar el codigo. Intenta de nuevo en unos segundos.";
        if (Contiene(m, "DuplicatedCredit"))
            return "Esta venta ya tiene un credito creado. Revisa antes de reintentar.";
        if (Contiene(m, "CustomerNotFound"))
            return "No encontramos un cliente con ese documento. Verifica el numero y el tipo.";
        if (Contiene(m, "RequestValuesInvalid"))
            return "Los datos enviados no son validos. Revisa el monto y el plazo.";

        // OJO con el orden: "CreditsNotFound" contiene "CreditLimit"? No, pero si
        // contiene "Credit", y la regla de cupo de mas abajo busca "CreditLimit".
        // Las especificas van ANTES de las genericas.
        if (Contiene(m, "CreditsNotFound"))
            return "Este cliente no tiene creditos activos en esta tienda. " +
                   "Verifica el documento o consulta con Sistecredito.";
        if (Contiene(m, "MonthsNumberNotValid"))
            return "Ese plazo no aplica para este monto. Elegi uno de los que aparecen " +
                   "en la pantalla.";
        if (Contiene(m, "CustomerIsDefaulter"))
            return "El cliente esta en mora. No se puede otorgar un credito nuevo hasta " +
                   "que se ponga al dia.";
        if (Contiene(m, "CustomerNotActive"))
            return "El cliente no esta activo en Sistecredito.";
        if (Contiene(m, "CreditNotActive"))
            return "Ese credito no esta activo. Elegi otro o consulta el estado.";
        if (Contiene(m, "StoreNotFound"))
            return "Esta tienda no esta registrada en Sistecredito. Avisa al area de " +
                   "sistemas: es configuracion del POS, no del cliente.";
        if (Contiene(m, "CustomerProfileInvalid"))
            return "Los datos del cliente en Sistecredito estan incompletos.";
        if (Contiene(m, "NotControlledException"))
            return "Sistecredito tuvo un error interno. Intenta de nuevo en un momento.";

        // REP-E-003: falta un campo obligatorio en NUESTRA peticion. No es algo que
        // el cajero pueda arreglar, y decirle "revisa los datos" lo manda a buscar
        // un problema que no existe del lado del mostrador.
        if (Contiene(m, "UserName es obligatorio") || Contiene(m, "REP-E-003"))
            return "El modulo no envio los datos completos de la operacion. " +
                   "Avisa al area de sistemas; no es un problema del cliente.";

        if (Contiene(m, "NotAvailableCreditLimit")
            || Contiene(m, "InsufficientQuota") || Contiene(m, "CreditLimit"))
            return "El cliente no tiene cupo disponible para este monto.";
        // Los dos nombres con los que Credinet reporta "no hay plan para ese monto".
        if (Contiene(m, "InvalidAmountCredit") || Contiene(m, "NoOfferAvailable"))
            return "Sistecredito no financia ese monto (no hay plan de credito para ese " +
                   "valor). No es el cupo del cliente, y cambiar el plazo no lo " +
                   "resuelve: proba un monto menor o cobra con otro medio de pago.";

        return null;
    }

    private static bool Contiene(string texto, string aguja) =>
        texto.Contains(aguja, StringComparison.OrdinalIgnoreCase);

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

    // QA B-7: cultura explicita. char.ToUpper(char) depende del locale del POS
    // (en turco 'i' mayuscula es 'İ'), y estos textos son fijos en espanol.
    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s)
            ? s
            : char.ToUpper(s[0], System.Globalization.CultureInfo.InvariantCulture) + s[1..];

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