namespace SistecreditoTEF.Maui.Services.Tiendas;

/// <summary>
/// La tienda que el instalador eligio al montar ESTA caja.
///
/// Es la mitad local de [ResolucionDeTienda]: la otra la pone HioPosCloud. Vive
/// aparte de [ICloudConfig] a proposito — lo que llega del POS y lo que decidio
/// una persona frente al terminal son dos cosas distintas, y mezclarlas es lo que
/// haria imposible detectar que no coinciden.
///
/// Se guarda en el dispositivo y sobrevive a los reinicios, pero NO a desinstalar
/// la app; eso esta bien: reinstalar es montar la caja de nuevo.
/// </summary>
public interface ITiendaDeLaCaja
{
    /// <summary>StoreId elegido, o null si todavia nadie eligio.</summary>
    string? StoreId { get; }

    /// <summary>
    /// La tienda del catalogo correspondiente, o null si no se eligio ninguna o
    /// el StoreId guardado ya no figura en la hoja.
    /// </summary>
    TiendaDelCatalogo? Seleccionada { get; }

    /// <summary>Fija la tienda de esta caja.</summary>
    void Fijar(TiendaDelCatalogo tienda);

    /// <summary>Olvida la eleccion. Deja la caja a lo que diga HioPosCloud.</summary>
    void Olvidar();
}

/// <summary>
/// Para pruebas y para plataformas sin almacenamiento. No persiste nada.
/// </summary>
public sealed class TiendaDeLaCajaEnMemoria : ITiendaDeLaCaja
{
    public string? StoreId { get; private set; }

    public TiendaDelCatalogo? Seleccionada => CatalogoDeTiendas.PorStoreId(StoreId);

    public void Fijar(TiendaDelCatalogo tienda)
    {
        ArgumentNullException.ThrowIfNull(tienda);
        StoreId = tienda.StoreId;
    }

    public void Olvidar() => StoreId = null;
}
