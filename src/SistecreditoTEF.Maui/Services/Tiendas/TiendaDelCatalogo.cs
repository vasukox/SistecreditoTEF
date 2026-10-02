namespace SistecreditoTEF.Maui.Services.Tiendas;

/// <summary>
/// Una tienda de la hoja oficial de Sistecredito.
/// </summary>
/// <param name="Codigo">Codigo interno de Permoda ("037"). Es como se la nombra en la operacion.</param>
/// <param name="Nombre">Nombre comercial. Puede venir VACIO: ver [NombreVisible].</param>
/// <param name="Ciudad">Ciudad, para desempatar nombres parecidos.</param>
/// <param name="StoreId">ObjectId de 24 hex con el que Sistecredito la identifica.</param>
public sealed record TiendaDelCatalogo(
    string Codigo,
    string Nombre,
    string Ciudad,
    string StoreId)
{
    /// <summary>
    /// Como se la nombra en pantalla.
    ///
    /// Diez de las 76 tiendas de la hoja llegan SIN nombre —las nuevas, de la 545
    /// en adelante—. Se las conserva igual: tienen StoreId valido, y sacarlas del
    /// catalogo obligaria a teclear 24 caracteres a mano justo en las tiendas que
    /// se estan abriendo, que es donde mas facil se equivoca uno.
    /// </summary>
    public string NombreVisible =>
        string.IsNullOrWhiteSpace(Nombre) ? $"Tienda {Codigo}" : Nombre;

    /// <summary>"037 · MULTIMARCA PUNTO CALLE 18". Lo que ve el instalador.</summary>
    public string Etiqueta => $"{Codigo} · {NombreVisible}";

    /// <summary>Con ciudad, para cuando hay que desempatar.</summary>
    public string EtiquetaConCiudad => $"{Etiqueta} ({Ciudad})";
}
