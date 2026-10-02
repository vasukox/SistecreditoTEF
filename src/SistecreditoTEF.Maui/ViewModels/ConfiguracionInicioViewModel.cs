using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// SEGUNDO PASO DEL MONTAJE: ¿como se configura esta caja?
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTA PANTALLA NO HACE NADA MAS QUE PREGUNTAR
/// ─────────────────────────────────────────────────────────────────────────────
/// Las dos formas de montar una caja convivian con el formulario del PIN en una
/// sola pantalla: el atajo "copia de otra caja" arriba, el formulario abajo. Quien
/// llegaba con una caja ya montada en la tienda igual veia un campo de PIN, y lo
/// normal es llenar el campo que te ponen delante. Ese PIN quedaba pisado dos
/// minutos despues, al copiar de la otra caja.
///
/// Aqui la eleccion ocurre ANTES de que exista algo que llenar, y cada camino
/// lleva a una pantalla que hace una sola cosa.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL ORDEN DE LAS DOS OPCIONES NO ES CASUAL
/// ─────────────────────────────────────────────────────────────────────────────
/// "Ya hay otra caja configurada" va primero. De las tres cajas de un local, dos
/// se montan copiando: es el camino mas frecuente, el mas corto y el que no puede
/// equivocarse de PIN. Crear desde cero es lo que hace la PRIMERA caja de la
/// tienda, una vez por local.
/// </summary>
public partial class ConfiguracionInicioViewModel(
    INavigationService nav,
    ITiendaEnOperacion tienda) : ObservableObject
{
    /// <summary>
    /// Con que tienda quedo la caja en el paso anterior. Se muestra arriba para
    /// que el instalador confirme de un vistazo que el paso 1 quedo bien antes de
    /// seguir: a partir de aqui ya no se vuelve a preguntar.
    /// </summary>
    public string Tienda => tienda.Etiqueta;

    /// <summary>
    /// Vuelve a leer la tienda. Propiedad calculada: MAUI evalua el enlace al
    /// armarlo y no lo vuelve a mirar, asi que sin esto volver de cambiar la
    /// tienda seguiria mostrando la anterior. Lo llama la pagina en OnAppearing.
    /// </summary>
    public void RefrescarTienda() => OnPropertyChanged(nameof(Tienda));

    [ObservableProperty]
    private string? errorMessage;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));

    /// <summary>Sello de compilacion y ambiente. Ver [BuildInfo].</summary>
    public string Build => BuildInfo.Descripcion;

    /// <summary>
    /// Camino corto: traerse el PIN y el padron de la caja que ya opera en esta
    /// tienda. No hay que crear nada.
    /// </summary>
    [RelayCommand]
    private async Task CopiarDeOtraCajaAsync()
    {
        ErrorMessage = null;
        try
        {
            await nav.GoToReplicacionAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfiguracionInicioViewModel", "Error abriendo la copia entre cajas.", ex);
            ErrorMessage = "No se pudo abrir la pantalla de copia entre cajas.";
        }
    }

    /// <summary>
    /// Camino largo: crear el PIN de administrador y dar de alta a los cajeros.
    /// Es lo que hace la primera caja de la tienda.
    /// </summary>
    [RelayCommand]
    private async Task ConfigurarDesdeCeroAsync()
    {
        ErrorMessage = null;
        try
        {
            await nav.GoToConfigurarAdminAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfiguracionInicioViewModel", "Error abriendo la configuracion inicial.", ex);
            ErrorMessage = "No se pudo abrir la configuración de esta caja.";
        }
    }

    /// <summary>
    /// Volver al paso anterior a corregir la tienda.
    ///
    /// Existe porque el paso 1 es irreversible de otra forma: una vez elegida la
    /// tienda, esta pantalla ya no la vuelve a preguntar, y la unica correccion
    /// posible estaria detras del PIN de administrador... que en una caja nueva
    /// todavia no existe.
    /// </summary>
    [RelayCommand]
    private async Task CambiarLaTiendaAsync()
    {
        ErrorMessage = null;
        try
        {
            await nav.GoToElegirTiendaAsistenteAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfiguracionInicioViewModel", "Error volviendo a la eleccion de tienda.", ex);
            ErrorMessage = "No se pudo abrir la pantalla de tiendas.";
        }
    }
}
