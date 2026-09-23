using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Configuracion inicial: se crea el PIN de administrador la primera vez que se
/// abre la app en el terminal.
///
/// Es la unica pantalla que puede FIJAR el PIN. Cambiarlo despues exige el
/// anterior ([AdminCajerosViewModel]), porque si no cualquiera que llegue aca se
/// queda con el terminal.
/// </summary>
public partial class ConfigurarAdminViewModel(
    AuthService auth,
    INavigationService nav) : ObservableObject
{
    /// <summary>
    /// Trae la configuracion de otra caja de la misma tienda, en vez de crearla.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ESTA ACA Y NO SOLO EN ADMINISTRACION
    /// ─────────────────────────────────────────────────────────────────────────
    /// Estaba unicamente detras del PIN de administrador, y eso era un circulo:
    /// en una caja recien instalada habia que INVENTAR un PIN para poder llegar a
    /// la pantalla que justamente viene a copiar el PIN de la otra caja. El
    /// instalador creaba uno que a los dos minutos quedaba pisado.
    ///
    /// Esta pantalla es la primera que ve una caja sin configurar, asi que es
    /// donde la opcion tiene sentido. Sigue estando tambien en administracion,
    /// porque desde alla se COMPARTE.
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
            AppLogger.E("ConfigurarAdminViewModel", "Error abriendo la copia entre cajas.", ex);
            ErrorMessage = "No se pudo abrir la pantalla de copia entre cajas.";
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string pin = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string pinConfirmacion = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private bool guardando;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));

    /// <summary>
    /// Sello de compilacion y ambiente, para la esquina de la pantalla. Ver
    /// [BuildInfo]: es la unica forma de saber que APK tiene una caja, porque la
    /// version que ve HioPos esta fija por contrato.
    /// </summary>
    public string Build => BuildInfo.Descripcion;

    public string Ayuda =>
        $"Minimo {PasswordHasher.MinLength} digitos. Con este PIN se administran los " +
        "cajeros, asi que no lo comparta con ellos.";

    /// <summary>
    /// Motivo por el que no se puede guardar, o vacio. Un boton gris sin explicacion
    /// deja al instalador sin saber que falta.
    /// </summary>
    public string MotivoBloqueo
    {
        get
        {
            if (Guardando) return "Guardando...";
            if (!AuthService.EsClaveValida(Pin))
                return $"El PIN debe tener al menos {PasswordHasher.MinLength} digitos.";
            if (Pin != PinConfirmacion) return "Los dos PIN no coinciden.";
            return string.Empty;
        }
    }

    private bool CanGuardar()
    {
        var motivo = MotivoBloqueo;
        OnPropertyChanged(nameof(MotivoBloqueo));
        return motivo.Length == 0;
    }

    [RelayCommand(CanExecute = nameof(CanGuardar))]
    private async Task GuardarAsync()
    {
        // ─────────────────────────────────────────────────────────────────────
        // LA VALIDACION SE REPITE ACA A PROPOSITO
        // ─────────────────────────────────────────────────────────────────────
        // El CanExecute apaga el boton, pero NO protege el comando: invocarlo por
        // codigo —como hace una prueba, o como haria cualquier atajo futuro— lo
        // ejecuta igual. Sin esta comprobacion, dos PIN distintos terminaban
        // guardando el PRIMERO como PIN de administrador del terminal, en
        // silencio: el instalador creia haber puesto uno y quedaba otro.
        //
        // Lo encontro una prueba, no una tienda.
        var motivo = MotivoBloqueo;
        if (motivo.Length > 0)
        {
            ErrorMessage = motivo;
            return;
        }

        Guardando = true;
        ErrorMessage = null;
        try
        {
            if (!await auth.ConfigurarPinAdminAsync(Pin))
            {
                // Pasa si otro camino ya configuro el PIN entremedio.
                ErrorMessage = "No se pudo configurar el PIN. Si ya existe uno, " +
                               "usa la opcion de administracion para cambiarlo.";
                return;
            }

            AppLogger.I("ConfigurarAdminViewModel", "PIN de administrador creado.");

            // Se pasa directo al alta de cajeros: un terminal con PIN pero sin
            // cajeros no puede cobrar nada, asi que dejarlo ahi seria un callejon.
            await nav.GoToAdminCajerosAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfigurarAdminViewModel", "Error configurando el PIN.", ex);
            ErrorMessage = "Ocurrio un error guardando el PIN. Intenta de nuevo.";
        }
        finally
        {
            Guardando = false;
        }
    }
}
