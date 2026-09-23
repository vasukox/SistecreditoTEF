using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Panel de ingreso: usuario y clave.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE SE REEMPLAZO LA LISTA DE CAJEROS
/// ─────────────────────────────────────────────────────────────────────────────
/// La version anterior mostraba los cajeros de la tienda y el cajero se elegia de
/// esa lista. Dos problemas:
///
///   • Publicaba en pantalla quienes trabajan en la tienda ante cualquiera que
///     agarre el terminal.
///   • Dejaba el ingreso a un solo dato secreto —la clave— con el usuario ya
///     resuelto de antemano. Ahora hay que saber las dos cosas.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE SOLO EN ABONOS
/// ─────────────────────────────────────────────────────────────────────────────
/// Un abono se hace abriendo el APK desde el icono, fuera de HioPos: no hay nadie
/// que haya validado quien opera, y se esta moviendo plata de un cliente. La venta
/// a credito entra por HioPos, donde el cajero ya se identifico en el POS.
///
/// El cajero identificado queda en [ISesionCajero] y su nombre viaja a Credinet en
/// <c>userName</c>, asi que la traza del recaudo deja de decir "Cajero Permoda"
/// para todos.
/// </summary>
public partial class IngresoCajeroViewModel(
    AuthService auth,
    ISesionCajero sesion,
    INavigationService nav) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IngresarCommand))]
    private string usuario = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IngresarCommand))]
    private string clave = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IngresarCommand))]
    private bool verificando;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));

    /// <summary>
    /// Por que el boton esta deshabilitado, o vacio.
    ///
    /// Ya no contempla bloqueo por intentos: no hay tope. Un cajero que olvido la
    /// clave puede reintentar cuantas veces necesite.
    /// </summary>
    public string MotivoBloqueo
    {
        get
        {
            if (Verificando) return "Verificando...";
            if (string.IsNullOrWhiteSpace(Usuario)) return "Ingresa tu usuario.";
            if (string.IsNullOrEmpty(Clave)) return "Ingresa tu clave.";
            return string.Empty;
        }
    }

    private bool CanIngresar()
    {
        var motivo = MotivoBloqueo;
        OnPropertyChanged(nameof(MotivoBloqueo));
        return motivo.Length == 0;
    }

    [RelayCommand(CanExecute = nameof(CanIngresar))]
    private async Task IngresarAsync()
    {
        Verificando = true;
        ErrorMessage = null;
        try
        {
            var resultado = await auth.IngresarAsync(Usuario, Clave);

            switch (resultado)
            {
                case ResultadoIngreso.Ok ok:
                    sesion.Iniciar(ok.Cajero);
                    Clave = string.Empty;
                    AppLogger.I("IngresoCajeroViewModel",
                        $"Ingreso OK: {PiiMask.Name(ok.Cajero.NombreVisible)}.");
                    await nav.GoToCreditosActivosAsync();
                    break;

                case ResultadoIngreso.ClaveIncorrecta:
                    // El mensaje NO distingue "no existe el usuario" de "la clave
                    // esta mal", a proposito: si lo hiciera, se podrian descubrir
                    // los usuarios validos probando nombres.
                    //
                    // Y NO menciona intentos restantes: ya no hay tope. Un cajero
                    // que olvido la clave puede reintentar sin quedar bloqueado.
                    Clave = string.Empty;
                    ErrorMessage = "Usuario o clave incorrectos. Verifica e intenta de nuevo.";
                    break;

                case ResultadoIngreso.Bloqueado bloq:
                    // Ya no se produce (no hay tope de intentos), pero el caso se
                    // maneja: el tipo sigue existiendo y dejarlo sin rama daria un
                    // fallo silencioso si alguna vez se repone el limite.
                    Clave = string.Empty;
                    ErrorMessage =
                        $"Ingreso temporalmente no disponible. Espera {bloq.SegundosRestantes} segundos.";
                    break;

                case ResultadoIngreso.NoHabilitado:
                    // Este caso SI se dice: el usuario existe pero esta de baja. No
                    // revela nada que el administrador no sepa, y le ahorra al
                    // cajero seguir probando la clave en vano.
                    Clave = string.Empty;
                    ErrorMessage = "Ese usuario esta desactivado. Consulta con el administrador.";
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("IngresoCajeroViewModel", "Error verificando el ingreso.", ex);
            ErrorMessage = "Ocurrio un error al ingresar. Intenta de nuevo.";
        }
        finally
        {
            Verificando = false;
        }
    }

    /// <summary>Entra a administracion. La pantalla destino pide el PIN.</summary>
    [RelayCommand]
    private async Task IrAAdministracionAsync() => await nav.GoToAdminCajerosAsync();
}
