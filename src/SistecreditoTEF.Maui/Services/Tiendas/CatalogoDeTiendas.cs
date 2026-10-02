using System.Globalization;
using System.Text;

namespace SistecreditoTEF.Maui.Services.Tiendas;

/// <summary>
/// LAS TIENDAS, DENTRO DEL APK.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE EL CATALOGO VIAJA EMBEBIDO
/// ─────────────────────────────────────────────────────────────────────────────
/// El StoreId es lo que decide a nombre de que tienda queda cada credito en
/// Sistecredito. El canal previsto para entregarlo es <c>STORE_ID</c> por
/// CloudLicense, y sigue siendo el que manda — pero mientras una tienda no este
/// provisionada ahi, la caja no puede operar, y esperar a que ICG cargue 76
/// terminales deja las tiendas paradas.
///
/// Con el catalogo adentro, el instalador ELIGE la tienda de una lista al montar
/// la caja y queda operando de una vez. Elegir de una lista, y no teclear, es lo
/// importante: un ObjectId de 24 caracteres escrito a mano es una errata esperando
/// a pasar, y la errata se paga como creditos atribuidos a otra tienda.
///
/// Los datos se generan desde la hoja oficial: ver [CatalogoDeTiendas.Datos.cs].
/// El StoreId no es una credencial —viaja en cada peticion a Credinet— asi que
/// llevarlo en el APK no expone nada que la red no vea ya.
///
/// OJO CON EL ALCANCE: la hoja trae 76 tiendas, todas de Bogota y Soacha. La
/// cadena es mas grande. Una tienda que no este aca necesita que ICG le cargue el
/// STORE_ID, o que alguien pida la hoja nueva y se regenere este archivo.
/// </summary>
public static partial class CatalogoDeTiendas
{
    /// <summary>Todas las tiendas conocidas, por codigo.</summary>
    public static IReadOnlyList<TiendaDelCatalogo> Todas => Datos;

    /// <summary>La tienda con ese StoreId, o null si no esta en la hoja.</summary>
    public static TiendaDelCatalogo? PorStoreId(string? storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId)) return null;
        var buscado = storeId.Trim();

        foreach (var tienda in Datos)
        {
            if (string.Equals(tienda.StoreId, buscado, StringComparison.OrdinalIgnoreCase))
                return tienda;
        }

        return null;
    }

    /// <summary>La tienda con ese codigo ("037"), o null.</summary>
    public static TiendaDelCatalogo? PorCodigo(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
        var buscado = codigo.Trim();

        foreach (var tienda in Datos)
        {
            if (string.Equals(tienda.Codigo, buscado, StringComparison.OrdinalIgnoreCase))
                return tienda;
        }

        return null;
    }

    /// <summary>
    /// Busqueda para el selector: por codigo o por nombre, sin distinguir
    /// mayusculas ni tildes.
    ///
    /// El codigo se compara por PREFIJO y el nombre por contenido, porque asi es
    /// como busca quien esta instalando: teclea "037" o teclea "suba". Las
    /// coincidencias por codigo van primero, que es la forma exacta de nombrar una
    /// tienda en la operacion.
    ///
    /// Sin texto devuelve el catalogo entero: la lista no arranca vacia.
    /// </summary>
    public static IReadOnlyList<TiendaDelCatalogo> Buscar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Datos;

        var aguja = Plegar(texto);
        if (aguja.Length == 0) return Datos;

        var porCodigo = new List<TiendaDelCatalogo>();
        var porNombre = new List<TiendaDelCatalogo>();

        foreach (var tienda in Datos)
        {
            if (tienda.Codigo.StartsWith(aguja, StringComparison.OrdinalIgnoreCase))
            {
                porCodigo.Add(tienda);
                continue;
            }

            if (Plegar(tienda.Nombre).Contains(aguja, StringComparison.Ordinal)
                || Plegar(tienda.Ciudad).Contains(aguja, StringComparison.Ordinal))
            {
                porNombre.Add(tienda);
            }
        }

        porCodigo.AddRange(porNombre);
        return porCodigo;
    }

    /// <summary>
    /// Como nombrar un StoreId en pantalla o en el log.
    ///
    /// Si esta en la hoja se lo nombra ("037 · MULTIMARCA PUNTO CALLE 18"); si no,
    /// se dice EXPLICITAMENTE que no esta, y se muestra el id entero. Un StoreId
    /// que no figura en la hoja oficial es justo el caso que hay que poder ver: o
    /// la hoja quedo vieja, o alguien cargo un valor que no corresponde.
    /// </summary>
    public static string Describir(string? storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId)) return "(sin tienda)";

        var tienda = PorStoreId(storeId);
        return tienda is not null
            ? tienda.Etiqueta
            : $"{storeId.Trim()} (no figura en la hoja de Sistecrédito)";
    }

    /// <summary>
    /// Minusculas y sin tildes, para que "GALERIAS" encuentre "Galerías" y al
    /// reves. La hoja viene sin tildes pero quien busca las escribe.
    /// </summary>
    private static string Plegar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var descompuesto = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);

        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
