using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Tiendas;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Guarda la tienda elegida en <c>Preferences</c>.
///
/// Vive en Services\Platform y no junto a [ITiendaDeLaCaja] porque toca la
/// plataforma: asi la logica de [ResolucionDeTienda] y [CatalogoDeTiendas] sigue
/// entrando entera al proyecto de pruebas, que es donde tiene que poder probarse
/// la regla que decide a nombre de que tienda se vende.
///
/// No va cifrado. El StoreId no es una credencial: viaja en cada peticion a
/// Credinet y esta en la hoja que circula por correo. Lo que importa de el es que
/// sea el CORRECTO, no que sea secreto.
/// </summary>
public sealed class TiendaDeLaCajaEnPreferencias : ITiendaDeLaCaja
{
    private const string Clave = "tienda_de_la_caja_storeid_v1";

    public string? StoreId
    {
        get
        {
            try
            {
                var v = Preferences.Get(Clave, string.Empty);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            catch (Exception ex)
            {
                // Si no se puede leer, la caja queda "sin tienda propia" y decide
                // HioPosCloud. Nunca se inventa una: inventar aca es exactamente
                // el defecto que se esta corrigiendo.
                AppLogger.W("TiendaDeLaCaja", $"No se pudo leer la tienda de la caja: {ex.Message}");
                return null;
            }
        }
    }

    public TiendaDelCatalogo? Seleccionada => CatalogoDeTiendas.PorStoreId(StoreId);

    public void Fijar(TiendaDelCatalogo tienda)
    {
        ArgumentNullException.ThrowIfNull(tienda);

        Preferences.Set(Clave, tienda.StoreId);
        AppLogger.I("TiendaDeLaCaja",
            $"Esta caja queda configurada como {tienda.Etiqueta}.");
    }

    public void Olvidar()
    {
        Preferences.Remove(Clave);
        AppLogger.W("TiendaDeLaCaja",
            "Se borro la tienda configurada en la caja: pasa a depender de STORE_ID de HioPosCloud.");
    }
}
