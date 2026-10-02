using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Con que tienda esta operando la caja, para mostrarlo en pantalla.
/// </summary>
public interface ITiendaEnOperacion
{
    /// <summary>"nombre  ·  storeId", para el pie de pantalla.</summary>
    string Linea { get; }

    /// <summary>
    /// Version corta para la cabecera: "037 · MULTIMARCA PUNTO CALLE 18".
    ///
    /// La cabecera la ve el cajero TODO el dia, y ahi no caben los 24 caracteres
    /// del StoreId: en una pantalla de POS ese renglon empuja al titulo fuera de
    /// lugar y ademas nadie lo lee. El codigo de tienda si lo reconoce cualquiera
    /// de la operacion, que es lo que hace falta para notar "esta caja dice 037 y
    /// yo estoy en Suba".
    ///
    /// El StoreId completo sigue estando donde se usa de verdad: la pantalla de
    /// tienda, que es donde se coteja contra la hoja.
    /// </summary>
    string Etiqueta { get; }

    /// <summary>
    /// Aviso a mostrar cuando la caja no esta provisionada; vacio cuando si.
    /// </summary>
    string Aviso { get; }

    /// <summary>true cuando hay un StoreId que decir.</summary>
    bool EstaProvisionada { get; }
}

/// <summary>
/// Lee la configuracion vigente cada vez que se le pide, y no la guarda.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE NO ES UN SOLO CONSTRUCTOR CON ApiConfig
/// ─────────────────────────────────────────────────────────────────────────────
/// [ApiConfig] se registra como Transient, resuelto del [ApiConfigProvider], y el
/// proveedor RECONSTRUYE la configuracion en cada INITIALIZE ([ApiConfigProvider.Reload]).
/// Si esta clase tomara [ApiConfig] por constructor, guardaria la del primer
/// arranque y la pantalla seguiria mostrando el StoreId viejo para siempre — que es
/// justamente el defecto que se vino a corregir, repetido un nivel mas arriba.
///
/// Leyendo de [IApiConfigSource] en cada acceso, lo que se ve en pantalla es
/// siempre lo que el modulo esta usando de verdad.
///
/// Y NUNCA LANZA. Describir la tienda no puede impedir que la caja abra: si la
/// lectura falla, se muestra "desconocido" y el flujo sigue. Ver [Describir].
/// </summary>
public sealed class TiendaEnOperacion : ITiendaEnOperacion
{
    private readonly IApiConfigSource _fuente;

    public TiendaEnOperacion(IApiConfigSource fuente) => _fuente = fuente;

    public string Linea => Describir().Linea;

    public string Aviso => Describir().Aviso;

    public bool EstaProvisionada => Describir().EstaProvisionada;

    /// <inheritdoc />
    public string Etiqueta
    {
        get
        {
            try
            {
                var elegida = _fuente.Current.Tienda.StoreId;
                if (string.IsNullOrWhiteSpace(elegida)) return "Sin tienda";

                // Del catalogo sale "037 · NOMBRE". Si el StoreId no esta en la
                // hoja —una tienda nueva que llego por CloudLicense— se muestra el
                // nombre que haya, que es mejor que un identificador crudo.
                return Tiendas.CatalogoDeTiendas.PorStoreId(elegida)?.Etiqueta
                       ?? TextoDeTienda.Nombre(_fuente.Current.StoreName);
            }
            catch (Exception ex)
            {
                AppLogger.W("TiendaEnOperacion", $"No se pudo etiquetar la tienda: {ex.Message}");
                return "Tienda: (no se pudo leer)";
            }
        }
    }

    private (string Linea, string Aviso, bool EstaProvisionada) Describir()
    {
        try
        {
            var config = _fuente.Current;

            // Se describe la tienda ELEGIDA ([ApiConfig.Tienda]), no la que viaja a
            // Credinet ([ApiConfig.StoreId]): en sandbox el identificador no se
            // manda a proposito —el ambiente de pruebas no conoce las tiendas de la
            // hoja— y mirar ese campo haria que la pantalla dijera "falta elegir la
            // tienda" en una caja que ya la tiene elegida.
            var elegida = config.Tienda.StoreId;

            return (
                TextoDeTienda.Linea(config.StoreName, elegida),
                TextoDeTienda.Aviso(elegida, config.IsProduction),
                !string.IsNullOrWhiteSpace(elegida));
        }
        catch (Exception ex)
        {
            // Deliberadamente mudo: es la pantalla de arranque, y un error al
            // DESCRIBIR la tienda no puede impedir que la caja se use. Queda en el
            // log para el que este mirando.
            AppLogger.W("TiendaEnOperacion", $"No se pudo leer la tienda: {ex.Message}");
            return ("Tienda: (no se pudo leer)", string.Empty, false);
        }
    }
}
