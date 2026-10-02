namespace SistecreditoTEF.Maui.Services.Tiendas;

/// <summary>Si esta caja sabe o no que tienda es.</summary>
public enum EstadoDeLaTienda
{
    /// <summary>Todavia nadie la eligio. La caja no puede operar.</summary>
    SinElegir,

    /// <summary>Elegida en el terminal. Es el unico camino.</summary>
    Elegida
}

/// <summary>
/// QUE TIENDA ES ESTA CAJA.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// SE ELIGE A MANO EN EL TERMINAL, SIEMPRE
/// ─────────────────────────────────────────────────────────────────────────────
/// El StoreId decide a nombre de que tienda queda cada credito en Sistecredito, y
/// aqui hay UNA sola fuente: la tienda que alguien eligio en esta caja, de la lista
/// de [CatalogoDeTiendas]. Ni el APK ni HioPosCloud la definen.
///
/// Por que se llego a esto:
///
///   · El APK la traia horneada, y por eso creditos hechos en una tienda de Suba
///     quedaron registrados a nombre de la 037. Un valor dentro del paquete es el
///     mismo para las 76 tiendas que lo instalen.
///
///   · <c>STORE_ID</c> por CloudLicense era el camino previsto, pero depende de que
///     ICG provisione terminal por terminal, y mientras no lo haga la caja no puede
///     vender. Se verifico en terminal: no llega.
///
/// Elegirla a mano tiene un costo —un paso mas al instalar— y una ventaja que lo
/// justifica: es EXPLICITA. Alguien mira la lista, reconoce su tienda y la toca. No
/// hay un valor heredado de otro lado que nadie reviso.
///
/// Se elige de una LISTA, nunca se teclea: un ObjectId de 24 caracteres escrito a
/// mano es una errata esperando a pasar, y esa errata se paga igual que el problema
/// de Suba.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QUE PASA CON EL STORE_ID DE CLOUDLICENSE
/// ─────────────────────────────────────────────────────────────────────────────
/// Se sigue LEYENDO, pero solo para dejar constancia si no coincide con la tienda
/// elegida. No manda y no frena: la eleccion del terminal es la autoridad. Esa
/// discrepancia, si aparece, es informacion util —significa que ICG cree otra
/// cosa— y no un motivo para dejar una caja sin vender.
///
/// La funcion es PURA: es la regla que decide a nombre de quien se vende, y tiene
/// que poder probarse sin una terminal.
/// </summary>
/// <param name="Estado">¿Hay tienda elegida?</param>
/// <param name="StoreId">La tienda elegida. NULL mientras no se elija ninguna.</param>
/// <param name="DeHioPos">Lo que dijo CloudLicense, solo para cotejar. No manda.</param>
public sealed record ResolucionDeTienda(
    EstadoDeLaTienda Estado,
    string? StoreId,
    string? DeHioPos)
{
    /// <summary>¿Se puede operar? Solo con una tienda elegida.</summary>
    public bool SePuedeOperar => !string.IsNullOrWhiteSpace(StoreId);

    /// <summary>
    /// HioPosCloud entrego un STORE_ID distinto del elegido. No impide operar —manda
    /// la eleccion del terminal— pero conviene que quede escrito.
    /// </summary>
    public bool DiscrepaConHioPos =>
        !string.IsNullOrWhiteSpace(StoreId)
        && !string.IsNullOrWhiteSpace(DeHioPos)
        && !string.Equals(StoreId, DeHioPos, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cruza la tienda elegida en la caja con lo que haya dicho CloudLicense.
    /// Manda SIEMPRE la elegida; ver el encabezado.
    /// </summary>
    public static ResolucionDeTienda Resolver(string? deHiopos, string? elegidaEnLaCaja)
    {
        var elegida = Limpiar(elegidaEnLaCaja);

        return elegida is null
            ? new ResolucionDeTienda(EstadoDeLaTienda.SinElegir, null, Limpiar(deHiopos))
            : new ResolucionDeTienda(EstadoDeLaTienda.Elegida, elegida, Limpiar(deHiopos));
    }

    /// <summary>
    /// Una linea para el log: que tienda, nombrada, y la discrepancia si la hay.
    ///
    /// Se NOMBRA la tienda ("037 · MULTIMARCA PUNTO CALLE 18") en vez de escribir el
    /// ObjectId pelado: quien lee el log sabe de que tienda se habla sin ir a cotejar
    /// contra la hoja, y el identificador crudo solo aparece cuando NO figura en ella
    /// — que es justo el caso que hay que poder ver.
    /// </summary>
    public string ParaElLog()
    {
        if (Estado == EstadoDeLaTienda.SinElegir)
        {
            return "TIENDA: SIN ELEGIR. Hay que elegirla en el terminal " +
                   "(Configuracion - Tienda de esta caja) para poder operar.";
        }

        var linea = $"TIENDA: {CatalogoDeTiendas.Describir(StoreId)} (elegida en el terminal).";

        return DiscrepaConHioPos
            ? linea + $" HioPosCloud dice {CatalogoDeTiendas.Describir(DeHioPos)}; " +
                      "manda la elegida aca."
            : linea;
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
