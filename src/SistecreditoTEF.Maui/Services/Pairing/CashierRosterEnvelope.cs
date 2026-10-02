using System.Text.Json;
using System.Text.Json.Serialization;
using SistecreditoTEF.Maui.Services.Auth;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// Un cajero tal como viaja entre cajas.
///
/// Viaja el HASH, nunca la clave: en este modulo la clave en claro no existe en
/// ningun lado —se deriva al crearla (ver [PasswordHasher])— asi que el cajero
/// entra en la caja nueva con la misma de siempre sin que nadie la vuelva a
/// teclear.
///
/// <c>CreadoEn</c> se copia para no inventar una fecha de alta: el padron es el
/// mismo, no uno nuevo.
/// </summary>
public sealed record ReplicatedCajero(
    string Id,
    string Usuario,
    string Nombre,
    string ClaveHash,
    bool Activo,
    DateTime CreadoEn);

/// <summary>
/// Lo que una caja ya configurada le pasa a otra: el PIN de administrador y el
/// padron de cajeros.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE EL SOBRE ES TAN CHICO EN ESTE MODULO
/// ─────────────────────────────────────────────────────────────────────────────
/// La regla del diseño original es "si es de la tienda se copia, si es de la caja
/// no". Aca esa regla deja casi todo afuera, y no por descuido: la credencial de
/// Credinet, la URL, el ambiente, el nombre de tienda y los ids de medio de pago
/// NO se configuran en el terminal. Bajan de CloudLicense en el INITIALIZE de
/// HioPos (ver [ICloudConfig] y [ApiConfigProvider]), que es por definicion
/// configuracion central: ya llega sola a las 1.536 cajas.
///
/// Lo unico que hoy se teclea caja por caja —y por eso lo unico que este sobre
/// lleva— es el PIN de administrador y el alta de cada cajero con su clave.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// NO EXISTE NI DEBE EXISTIR UN CAMPO DE IDENTIDAD DE LA *CAJA*
/// ─────────────────────────────────────────────────────────────────────────────
/// El diseño original tiene que esquivar con cuidado el identificador de terminal,
/// porque dos cajas con el mismo id firman igual sus operaciones y el descuadre se
/// descubre semanas despues. Ese riesgo sigue vigente: si alguien agrega
/// <c>TerminalId</c> o <c>Source</c> "por conveniencia", dos cajas quedarian
/// firmando igual. Hay un test que lo verifica por reflexion.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA TIENDA SI VIAJA, Y POR QUE ESO NO CONTRADICE LO ANTERIOR
/// ─────────────────────────────────────────────────────────────────────────────
/// La regla es "si es de la TIENDA se copia, si es de la CAJA no". El StoreId es
/// de la tienda: la hoja de Sistecredito lo dice con todas las letras
/// ("SitC_WsStoreId, dato especifico por tienda"), y las tres cajas de un local
/// comparten el mismo.
///
/// Antes este sobre no lo llevaba porque la tienda bajaba de CloudLicense y ya
/// llegaba sola a cada terminal. Eso dejo de ser cierto: ahora se ELIGE en la caja
/// (ver [CatalogoDeTiendas]), asi que sin esto habria que elegirla tres veces por
/// local — tres oportunidades de equivocarse donde alcanza con una, y el error se
/// paga como creditos a nombre de otra tienda.
///
/// Dos condiciones que lo hacen seguro, y las dos importan:
///
///   · Solo se aplica en el MONTAJE COMPLETO de una caja nueva. En "actualizar
///     cajeros" se ignora: esa operacion es deliberadamente angosta y no toca nada
///     mas que el padron. Lo garantiza [ReplicacionViewModel], no este registro.
///
///   · Es OPCIONAL y la version del sobre NO sube. Un APK viejo emite sobres sin
///     el campo y se siguen aceptando; un APK viejo que reciba uno nuevo ignora la
///     propiedad que no conoce. Las tiendas no se actualizan todas el mismo dia, y
///     romper esa convivencia dejaria cajas sin poder montarse.
/// </summary>
/// <param name="AdminPinHash">PIN de administrador, ya derivado.</param>
/// <param name="Cajeros">Padron completo.</param>
/// <param name="StoreId">
/// Tienda del local, para que las cajas 2 y 3 no haya que asociarlas a mano.
/// Null en sobres de APK viejo: la caja receptora entonces pide elegirla.
/// </param>
public sealed record CashierRosterEnvelope(
    string? AdminPinHash,
    IReadOnlyList<ReplicatedCajero> Cajeros,
    string? StoreId = null)
{
    /// <summary>
    /// Version del sobre. Las tiendas no se actualizan el mismo dia: una caja con
    /// el APK nuevo se va a encontrar cajas con el viejo, y un sobre de otra
    /// version se rechaza ENTERO en vez de interpretarse a medias.
    /// </summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")]
    public int Version { get; init; } = CurrentVersion;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Interpreta un sobre recibido. Devuelve <c>null</c> ante cualquier problema:
    /// NUNCA lanza.
    ///
    /// Lo que llega aca viene de la red durante el montaje de una tienda. Un
    /// formato roto tiene que terminar en "no se pudo copiar, configura a mano", no
    /// en una app que se cierra a mitad de la instalacion.
    ///
    /// Un padron vacio tambien se rechaza: una caja que recibe configuracion se
    /// esta montando, y quedarse con cero cajeros la deja sin nadie que pueda
    /// operar, pero creyendose configurada.
    /// </summary>
    public static CashierRosterEnvelope? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<CashierRosterEnvelope>(json, Json);

            if (envelope is null || envelope.Version != CurrentVersion) return null;
            if (string.IsNullOrWhiteSpace(envelope.AdminPinHash)) return null;
            if (envelope.Cajeros is null || envelope.Cajeros.Count == 0) return null;

            // Un cajero sin usuario o sin derivacion no podria ingresar nunca. Si
            // viene uno asi, el sobre esta corrupto: mejor rechazarlo entero que
            // dejar la caja con un padron a medias.
            foreach (var c in envelope.Cajeros)
            {
                if (c is null) return null;
                if (string.IsNullOrWhiteSpace(c.Id)) return null;
                if (string.IsNullOrWhiteSpace(c.Usuario)) return null;
                if (string.IsNullOrWhiteSpace(c.ClaveHash)) return null;
            }

            return envelope;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Cajeros que van a poder ingresar en la caja receptora.</summary>
    public int CajerosActivos => Cajeros.Count(c => c.Activo);

    public static ReplicatedCajero From(Cajero c) =>
        new(c.Id, c.Usuario, c.Nombre, c.ClaveHash, c.Activo, c.CreadoEn);

    public static Cajero To(ReplicatedCajero c) =>
        new(c.Id, c.Usuario, c.Nombre, c.ClaveHash, c.Activo, c.CreadoEn);
}
