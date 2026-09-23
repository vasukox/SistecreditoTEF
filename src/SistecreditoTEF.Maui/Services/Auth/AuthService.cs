using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Reglas de ingreso: configuracion inicial, alta de cajeros e identificacion
/// antes de un abono.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QUE PIDE IDENTIFICACION Y QUE NO
/// ─────────────────────────────────────────────────────────────────────────────
/// Solo los ABONOS. La venta a credito entra por HioPos y ahi el cajero ya se
/// identifico en el POS: pedirle la clave otra vez seria pedirle lo mismo dos
/// veces y frenar la facturacion. Los abonos, en cambio, se hacen abriendo el APK
/// desde el icono, fuera de HioPos, sin nadie que haya validado quien es.
///
/// Ademas el nombre del cajero identificado viaja a Credinet en <c>userName</c>,
/// asi que la traza del recaudo deja de decir "Cajero Permoda" para todos.
///
/// Logica pura: no toca Android ni la base de datos (eso es [IAuthStore]), asi que
/// se testea completa.
/// </summary>
public class AuthService(IAuthStore store)
{
    // ═══════════════════════════════════════════════════════════════════════════
    // NO HAY LIMITE DE INTENTOS
    // ═══════════════════════════════════════════════════════════════════════════
    // Habia un bloqueo de 2 minutos a los 5 intentos fallidos. Se quito por pedido
    // explicito: un cajero que olvida la clave frenaba la caja, y en plena atencion
    // al publico esperar dos minutos es peor que el riesgo que el bloqueo evitaba.
    //
    // Lo que SI queda como freno natural: cada verificacion cuesta 210.000
    // iteraciones de PBKDF2, o sea cientos de milisegundos de CPU. Eso acota por si
    // solo a unos pocos intentos por segundo — no es un limite de politica, pero
    // hace que probar claves a ciegas sea lento igual.
    //
    // Si en algun momento se quiere reponer el limite, la barrera iba indexada por
    // el TEXTO del usuario (no por el id), para que probar nombres al azar tambien
    // contara. Ver el historial de este archivo.

    // ------------------------------------------------------------------
    // Configuracion inicial
    // ------------------------------------------------------------------

    /// <summary>
    /// true si el terminal todavia no tiene PIN de administrador, o sea que la app
    /// se acaba de instalar. Es lo que decide si se abre la configuracion inicial.
    /// </summary>
    public async Task<bool> RequiereConfiguracionInicialAsync() =>
        string.IsNullOrWhiteSpace(await store.GetAdminPinHashAsync());

    /// <summary>
    /// Fija el PIN de administrador. Solo se permite si NO habia uno: cambiarlo es
    /// [CambiarPinAdminAsync], que exige el anterior. Sin esa distincion, cualquiera
    /// que llegue a esta pantalla podria reemplazar el PIN y quedarse con el
    /// terminal.
    /// </summary>
    public async Task<bool> ConfigurarPinAdminAsync(string pin)
    {
        if (!EsClaveValida(pin)) return false;
        if (!await RequiereConfiguracionInicialAsync())
        {
            AppLogger.W("AuthService",
                "Se intento configurar el PIN de administrador cuando ya existia uno.");
            return false;
        }

        await store.SetAdminPinHashAsync(await PasswordHasher.HashAsync(pin));
        AppLogger.I("AuthService", "PIN de administrador configurado.");
        return true;
    }

    public async Task<bool> VerificarPinAdminAsync(string pin)
    {
        var hash = await store.GetAdminPinHashAsync();
        var ok = await PasswordHasher.VerifyAsync(pin, hash);
        AppLogger.I("AuthService", $"Verificacion de PIN de administrador: {(ok ? "OK" : "FALLIDA")}.");
        return ok;
    }

    public async Task<bool> CambiarPinAdminAsync(string pinActual, string pinNuevo)
    {
        if (!EsClaveValida(pinNuevo)) return false;
        if (!await VerificarPinAdminAsync(pinActual)) return false;

        await store.SetAdminPinHashAsync(await PasswordHasher.HashAsync(pinNuevo));
        AppLogger.I("AuthService", "PIN de administrador cambiado.");
        return true;
    }

    // ------------------------------------------------------------------
    // Cajeros
    // ------------------------------------------------------------------

    /// <summary>
    /// Cajeros que pueden ingresar, ordenados por nombre.
    ///
    /// Ordena por [Cajero.NombreVisible]: el nombre es opcional al dar de alta, y
    /// ordenar por Nombre amontonaba a todos los que solo tienen usuario al principio
    /// de la lista, como si no tuvieran identidad.
    /// </summary>
    public async Task<IReadOnlyList<Cajero>> ObtenerCajerosActivosAsync() =>
        (await store.GetCajerosAsync())
            .Where(c => c.Activo)
            .OrderBy(c => c.NombreVisible, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<Cajero>> ObtenerTodosLosCajerosAsync() =>
        (await store.GetCajerosAsync())
            .OrderBy(c => c.NombreVisible, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Largo minimo del usuario.</summary>
    public const int MinLargoUsuario = 3;

    /// <summary>
    /// Da de alta un cajero con usuario, nombre y clave.
    ///
    /// El USUARIO es el identificador de ingreso y tiene que ser unico entre los
    /// activos; el NOMBRE es solo para mostrar y para el comprobante, y puede
    /// repetirse (dos "Juan Perez" distintos existen en la vida real, pero
    /// "jperez" no puede estar dos veces).
    ///
    /// Devuelve null si algo no sirve: usuario corto, clave corta, o usuario ya
    /// tomado.
    /// </summary>
    public async Task<Cajero?> AgregarCajeroAsync(string usuario, string nombre, string clave)
    {
        var usuarioLimpio = (usuario ?? string.Empty).Trim();
        var nombreLimpio = (nombre ?? string.Empty).Trim();

        if (usuarioLimpio.Length < MinLargoUsuario || !EsClaveValida(clave)) return null;

        // Sin espacios en el usuario: se escribe en un teclado tactil y un espacio
        // invisible al final es un "usuario o clave incorrectos" imposible de
        // diagnosticar para el cajero.
        if (usuarioLimpio.Any(char.IsWhiteSpace))
        {
            AppLogger.W("AuthService", "Alta rechazada: el usuario no puede tener espacios.");
            return null;
        }

        var existentes = await store.GetCajerosAsync();
        if (existentes.Any(c => c.Activo &&
                string.Equals(c.UsuarioNormalizado,
                              usuarioLimpio.ToLowerInvariant(),
                              StringComparison.Ordinal)))
        {
            AppLogger.W("AuthService", "Alta de cajero rechazada: el usuario ya existe.");
            return null;
        }

        var cajero = new Cajero(
            Id: Guid.NewGuid().ToString("N"),
            Usuario: usuarioLimpio,
            Nombre: nombreLimpio,
            ClaveHash: await PasswordHasher.HashAsync(clave),
            Activo: true,
            CreadoEn: DateTime.UtcNow);

        await store.GuardarCajeroAsync(cajero);
        AppLogger.I("AuthService",
            $"Cajero dado de alta: usuario={PiiMask.Name(usuarioLimpio)}.");
        return cajero;
    }

    /// <summary>Cambia la clave de un cajero. Lo hace el administrador.</summary>
    public async Task<bool> CambiarClaveCajeroAsync(string id, string claveNueva)
    {
        if (!EsClaveValida(claveNueva)) return false;

        var cajero = await store.GetCajeroAsync(id);
        if (cajero is null) return false;

        await store.GuardarCajeroAsync(cajero with { ClaveHash = await PasswordHasher.HashAsync(claveNueva) });

        AppLogger.I("AuthService",
            $"Clave cambiada para el cajero {PiiMask.Name(cajero.NombreVisible)}.");
        return true;
    }

    /// <summary>Da de baja a un cajero. Deja de poder ingresar.</summary>
    public async Task DesactivarCajeroAsync(string id)
    {
        await store.EstablecerActivoAsync(id, activo: false);
        AppLogger.I("AuthService", "Cajero desactivado.");
    }

    /// <summary>
    /// Vuelve a habilitar a un cajero dado de baja.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE HACIA FALTA
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes solo existia la baja. Un cajero desactivado por error quedaba
    /// inservible para siempre: no aparecia para ingresar y tampoco se podia
    /// recrear, porque el alta rechaza nombres repetidos. La unica salida era
    /// borrar los datos de la aplicacion, o sea perder tambien la lista completa y
    /// el historial de abonos.
    ///
    /// Devuelve false si ya hay OTRO cajero activo con el mismo USUARIO: reactivar
    /// no puede saltearse la regla que impide dos usuarios iguales operando, porque
    /// entonces el ingreso seria ambiguo y no se sabria cual de los dos cobro.
    /// </summary>
    public async Task<bool> ReactivarCajeroAsync(string id)
    {
        var cajero = await store.GetCajeroAsync(id);
        if (cajero is null) return false;
        if (cajero.Activo) return true;

        var todos = await store.GetCajerosAsync();
        var chocaElUsuario = todos.Any(c =>
            c.Activo
            && !string.Equals(c.Id, id, StringComparison.Ordinal)
            && string.Equals(c.UsuarioNormalizado, cajero.UsuarioNormalizado,
                             StringComparison.Ordinal));

        if (chocaElUsuario)
        {
            AppLogger.W("AuthService",
                "Reactivacion rechazada: ya hay un cajero activo con ese usuario.");
            return false;
        }

        await store.EstablecerActivoAsync(id, activo: true);

        AppLogger.I("AuthService", $"Cajero reactivado: {PiiMask.Name(cajero.NombreVisible)}.");
        return true;
    }

    // ------------------------------------------------------------------
    // Ingreso
    // ------------------------------------------------------------------

    /// <summary>
    /// Ingreso con USUARIO y CLAVE.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE UN USUARIO ESCRITO Y NO UNA LISTA PARA ELEGIR
    /// ─────────────────────────────────────────────────────────────────────────
    /// La version anterior mostraba la lista de cajeros y el cajero se elegia de
    /// ahi. Eso publicaba en pantalla quienes trabajan en la tienda ante cualquiera
    /// que agarre el terminal, y dejaba el ingreso a un solo dato secreto: la
    /// clave, con el usuario ya resuelto. Con usuario escrito hay que saber las dos
    /// cosas.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// UN USUARIO INEXISTENTE RESPONDE COMO CLAVE INCORRECTA
    /// ─────────────────────────────────────────────────────────────────────────
    /// A proposito. Si un usuario que no existe diera un mensaje distinto de uno
    /// que si existe, se podrian descubrir los usuarios validos probando nombres.
    ///
    /// <paramref name="ahora"/> ya no cambia el resultado (no hay bloqueo temporal);
    /// se conserva el parametro para no romper las firmas de las pruebas.
    /// </summary>
    public async Task<ResultadoIngreso> IngresarAsync(
        string usuario, string clave, DateTime? ahora = null)
    {
        var usuarioNormalizado = (usuario ?? string.Empty).Trim().ToLowerInvariant();

        if (usuarioNormalizado.Length == 0)
            return new ResultadoIngreso.ClaveIncorrecta(IntentosRestantesIlimitados);

        var cajero = await BuscarPorUsuarioAsync(usuarioNormalizado);

        // Existe pero esta de baja: eso SI se dice, porque no revela nada que el
        // administrador no sepa y le ahorra al cajero probar la clave en vano.
        if (cajero is not null && !cajero.Activo)
            return new ResultadoIngreso.NoHabilitado();

        var claveOk = cajero is not null
                   && await PasswordHasher.VerifyAsync(clave, cajero.ClaveHash);

        if (claveOk)
        {
            AppLogger.I("AuthService", $"Ingreso OK: {PiiMask.Name(cajero!.NombreVisible)}.");
            return new ResultadoIngreso.Ok(cajero);
        }

        // Sin limite de intentos: el cajero puede reintentar todas las veces que
        // necesite. Ver el bloque al inicio de la clase para el porque.
        AppLogger.W("AuthService", "Ingreso fallido.");
        return new ResultadoIngreso.ClaveIncorrecta(IntentosRestantesIlimitados);
    }

    /// <summary>
    /// Valor que viaja en <c>ClaveIncorrecta</c> cuando no hay tope de intentos.
    ///
    /// Es -1 y no 0 a proposito: 0 se leeria como "no te quedan intentos", que es
    /// justo lo contrario. La pantalla lo interpreta como "ilimitados" y no muestra
    /// ninguna cuenta.
    /// </summary>
    public const int IntentosRestantesIlimitados = -1;

    /// <summary>
    /// Busca por usuario normalizado.
    ///
    /// Incluye un respaldo por NOMBRE para los cajeros dados de alta antes de que
    /// existiera el campo usuario: sus registros quedaron con usuario vacio y sin
    /// esto no podrian ingresar nunca, ni recrearse (el alta rechaza duplicados).
    /// </summary>
    private async Task<Cajero?> BuscarPorUsuarioAsync(string usuarioNormalizado)
    {
        var todos = await store.GetCajerosAsync();

        var porUsuario = todos.FirstOrDefault(c =>
            string.Equals(c.UsuarioNormalizado, usuarioNormalizado, StringComparison.Ordinal));

        if (porUsuario is not null) return porUsuario;

        return todos.FirstOrDefault(c =>
            string.IsNullOrWhiteSpace(c.Usuario)
            && string.Equals((c.Nombre ?? string.Empty).Trim(), usuarioNormalizado,
                             StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Largo minimo. No se exige complejidad: en un POS el cajero escribe esto
    /// decenas de veces al dia en un teclado tactil, y una regla de complejidad
    /// termina en la clave anotada en un papel pegado al monitor.
    /// </summary>
    public static bool EsClaveValida(string? clave) =>
        !string.IsNullOrWhiteSpace(clave) && clave.Trim().Length >= PasswordHasher.MinLength;
}
