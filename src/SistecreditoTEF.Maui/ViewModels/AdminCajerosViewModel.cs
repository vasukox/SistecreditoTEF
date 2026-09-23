using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Administracion de cajeros: alta, baja y cambio de clave. Detras del PIN de
/// administrador.
///
/// La pantalla tiene DOS estados: primero pide el PIN, y solo despues muestra la
/// lista. El desbloqueo vive en esta instancia y los ViewModels son Transient, asi
/// que salir y volver obliga a poner el PIN otra vez — que es justamente lo que se
/// quiere en un terminal compartido.
/// </summary>
public partial class AdminCajerosViewModel(
    AuthService auth,
    INavigationService nav) : ObservableObject
{
    // ------------------------------------------------------------------
    // Continuar al ingreso
    // ------------------------------------------------------------------

    /// <summary>
    /// Cierra la configuracion y pasa a la seleccion de cajero.
    ///
    /// Existe porque tras crear el PIN y dar de alta los cajeros la pantalla no
    /// tenia salida hacia adelante: el instalador quedaba en administracion sin un
    /// camino obvio a operar.
    ///
    /// Solo se habilita si hay al menos un cajero activo — mandar a elegir de una
    /// lista vacia es un callejon.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinuar))]
    private async Task SiguienteAsync()
    {
        try
        {
            await nav.GoToIngresoCajeroAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error navegando al ingreso de cajero.", ex);
            ErrorMessage = "No se pudo abrir la pantalla de ingreso.";
        }
    }

    private bool CanContinuar() => Cajeros.Any(c => c.Activo);

    /// <summary>
    /// Abre la replicacion entre cajas.
    ///
    /// Vive detras del PIN de administrador, igual que el alta de cajeros: quien
    /// puede repartir el padron es el mismo que puede darlo de alta. Y sirve para
    /// los dos lados —copiar de otra caja y compartir a otra— porque en una caja
    /// nueva el instalador ya paso por aca para crear el PIN.
    /// </summary>
    [RelayCommand]
    private async Task ReplicarAsync()
    {
        try
        {
            await nav.GoToReplicacionAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error abriendo la replicacion entre cajas.", ex);
            ErrorMessage = "No se pudo abrir la pantalla de copia entre cajas.";
        }
    }

    /// <summary>Motivo por el que no se puede continuar, o vacio.</summary>
    public string MotivoSiguiente =>
        CanContinuar() ? string.Empty : "Agrega al menos un cajero para continuar.";

    // ------------------------------------------------------------------
    // Puerta: el PIN
    // ------------------------------------------------------------------

    [ObservableProperty]
    private bool desbloqueado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DesbloquearCommand))]
    private string pin = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? mensajeOk;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);
    public bool TieneMensajeOk => !string.IsNullOrEmpty(MensajeOk);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));
    partial void OnMensajeOkChanged(string? value) => OnPropertyChanged(nameof(TieneMensajeOk));

    private bool CanDesbloquear() => Pin.Length >= PasswordHasher.MinLength;

    [RelayCommand(CanExecute = nameof(CanDesbloquear))]
    private async Task DesbloquearAsync()
    {
        ErrorMessage = null;
        try
        {
            if (!await auth.VerificarPinAdminAsync(Pin))
            {
                Pin = string.Empty;
                ErrorMessage = "PIN incorrecto.";
                return;
            }

            Pin = string.Empty;
            Desbloqueado = true;
            await CargarAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error verificando el PIN.", ex);
            ErrorMessage = "Ocurrio un error verificando el PIN.";
        }
    }

    // ------------------------------------------------------------------
    // Lista de cajeros
    // ------------------------------------------------------------------

    public ObservableCollection<Cajero> Cajeros { get; } = [];

    public async Task CargarAsync()
    {
        if (!Desbloqueado) return;
        try
        {
            var todos = await auth.ObtenerTodosLosCajerosAsync();
            Cajeros.Clear();
            foreach (var c in todos) Cajeros.Add(c);

            // La lista es una ObservableCollection, asi que agregar o quitar no
            // reevalua por si solo el CanExecute del boton Siguiente.
            SiguienteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MotivoSiguiente));
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "No se pudo cargar la lista.", ex);
            ErrorMessage = "No se pudo cargar la lista de cajeros.";
        }
    }

    // ------------------------------------------------------------------
    // Alta
    // ------------------------------------------------------------------

    /// <summary>Usuario de ingreso. Es el identificador y debe ser unico.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarCommand))]
    private string nuevoUsuario = string.Empty;

    // ──────────────────────────────────────────────────────────────────────────
    // EL ALTA YA NO PIDE NOMBRE: SOLO USUARIO Y CLAVE
    // ──────────────────────────────────────────────────────────────────────────
    // Habia un tercer campo, "Nombre completo (opcional)", para mostrar y para el
    // comprobante. Se quito por pedido de operacion: son dos datos por cajero, no
    // tres, multiplicado por ~1.536 terminales.
    //
    // No se pierde nada en el comprobante. [Cajero.NombreVisible] ya cae al usuario
    // cuando el nombre esta vacio —justamente para que nunca quede en blanco— asi
    // que el voucher pasa a decir "jperez" en lugar de "Juan Perez".
    //
    // La columna Nombre SIGUE existiendo en la BD y en el sobre de replicacion, y
    // eso es deliberado: los cajeros dados de alta antes de este cambio conservan
    // su nombre y lo siguen imprimiendo. Borrar la columna los dejaria sin el.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarCommand))]
    private string nuevaClave = string.Empty;

    private bool CanAgregar() =>
        NuevoUsuario.Trim().Length >= AuthService.MinLargoUsuario
        && !NuevoUsuario.Trim().Any(char.IsWhiteSpace)
        && AuthService.EsClaveValida(NuevaClave);

    [RelayCommand(CanExecute = nameof(CanAgregar))]
    private async Task AgregarAsync()
    {
        ErrorMessage = null;
        MensajeOk = null;
        try
        {
            // Nombre vacio: [Cajero.NombreVisible] usa el usuario. Ver la nota de
            // arriba sobre por que el alta ya no lo pide.
            var cajero = await auth.AgregarCajeroAsync(NuevoUsuario, string.Empty, NuevaClave);
            if (cajero is null)
            {
                ErrorMessage =
                    "No se pudo agregar. El usuario debe tener al menos " +
                    $"{AuthService.MinLargoUsuario} caracteres sin espacios, no estar repetido, " +
                    $"y la clave al menos {PasswordHasher.MinLength} caracteres.";
                return;
            }

            MensajeOk = $"Cajero {cajero.Usuario} agregado.";
            NuevoUsuario = string.Empty;
            NuevaClave = string.Empty;
            await CargarAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error agregando cajero.", ex);
            ErrorMessage = "Ocurrio un error agregando el cajero.";
        }
    }

    // ------------------------------------------------------------------
    // Baja y cambio de clave
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task DesactivarAsync(Cajero? cajero)
    {
        if (cajero is null) return;
        ErrorMessage = null;
        MensajeOk = null;
        try
        {
            await auth.DesactivarCajeroAsync(cajero.Id);
            MensajeOk = $"Cajero {cajero.NombreVisible} desactivado.";
            await CargarAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error desactivando cajero.", ex);
            ErrorMessage = "Ocurrio un error desactivando el cajero.";
        }
    }

    /// <summary>
    /// Vuelve a habilitar a un cajero dado de baja.
    ///
    /// Sin esto, desactivar era IRREVERSIBLE: el cajero no aparecia para ingresar y
    /// tampoco se podia recrear, porque el alta rechaza nombres repetidos. La unica
    /// salida era borrar los datos de la app y perder la lista entera.
    /// </summary>
    [RelayCommand]
    private async Task ReactivarAsync(Cajero? cajero)
    {
        if (cajero is null) return;
        ErrorMessage = null;
        MensajeOk = null;
        try
        {
            if (!await auth.ReactivarCajeroAsync(cajero.Id))
            {
                ErrorMessage = $"No se pudo reactivar a {cajero.NombreVisible}: ya hay otro " +
                               "cajero activo con ese nombre.";
                return;
            }

            MensajeOk = $"Cajero {cajero.NombreVisible} reactivado.";
            await CargarAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error reactivando cajero.", ex);
            ErrorMessage = "Ocurrio un error reactivando el cajero.";
        }
    }

    /// <summary>Cajero al que se le va a cambiar la clave, o null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarClaveNuevaCommand))]
    private Cajero? cajeroACambiar;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarClaveNuevaCommand))]
    private string claveNueva = string.Empty;

    public bool CambiandoClave => CajeroACambiar is not null;

    partial void OnCajeroACambiarChanged(Cajero? value) =>
        OnPropertyChanged(nameof(CambiandoClave));

    [RelayCommand]
    private void CambiarClave(Cajero? cajero)
    {
        CajeroACambiar = cajero;
        ClaveNueva = string.Empty;
    }

    [RelayCommand]
    private void CancelarCambioClave()
    {
        CajeroACambiar = null;
        ClaveNueva = string.Empty;
    }

    private bool CanGuardarClaveNueva() =>
        CajeroACambiar is not null && AuthService.EsClaveValida(ClaveNueva);

    [RelayCommand(CanExecute = nameof(CanGuardarClaveNueva))]
    private async Task GuardarClaveNuevaAsync()
    {
        if (CajeroACambiar is null) return;
        ErrorMessage = null;
        MensajeOk = null;
        try
        {
            if (!await auth.CambiarClaveCajeroAsync(CajeroACambiar.Id, ClaveNueva))
            {
                ErrorMessage = "No se pudo cambiar la clave.";
                return;
            }

            MensajeOk = $"Clave actualizada para {CajeroACambiar.NombreVisible}.";
            CancelarCambioClave();
            await CargarAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error cambiando la clave.", ex);
            ErrorMessage = "Ocurrio un error cambiando la clave.";
        }
    }

    // ------------------------------------------------------------------
    // Cambio del PIN de administrador
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CambiarPinCommand))]
    private string pinActual = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CambiarPinCommand))]
    private string pinNuevo = string.Empty;

    private bool CanCambiarPin() =>
        PinActual.Length >= PasswordHasher.MinLength && AuthService.EsClaveValida(PinNuevo);

    [RelayCommand(CanExecute = nameof(CanCambiarPin))]
    private async Task CambiarPinAsync()
    {
        ErrorMessage = null;
        MensajeOk = null;
        try
        {
            if (!await auth.CambiarPinAdminAsync(PinActual, PinNuevo))
            {
                ErrorMessage = "No se pudo cambiar el PIN. Verifica el PIN actual.";
                return;
            }

            MensajeOk = "PIN de administrador actualizado.";
            PinActual = string.Empty;
            PinNuevo = string.Empty;
        }
        catch (Exception ex)
        {
            AppLogger.E("AdminCajerosViewModel", "Error cambiando el PIN.", ex);
            ErrorMessage = "Ocurrio un error cambiando el PIN.";
        }
    }
}
