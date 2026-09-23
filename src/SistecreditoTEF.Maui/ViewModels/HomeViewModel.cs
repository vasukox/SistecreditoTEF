using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla raiz del kiosko (HomePage). Dos opciones grandes:
///   - Comprar a credito: navega al flujo nuevo (CapturaCedula)
///   - Pagar credito: navega al flujo de pago (CreditosActivos)
///
/// HU8-973 fix: usa [INavigationService] inyectado en lugar de
/// [Shell.Current.GoToAsync] directo. Esto evita crashear si Shell no esta
/// listo (cold start, navegacion concurrente, MainThread marshalling).
/// </summary>
public partial class HomeViewModel(
    INavigationService nav,
    AuthService auth,
    ISesionCajero sesion) : ObservableObject
{
    [RelayCommand]
    private async Task GoNuevoCreditoAsync()
    {
        try
        {
            await nav.GoToCapturaCedulaAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("HomeViewModel", "Error navegando a CapturaCedula", ex);
        }
    }

    /// <summary>
    /// Pagar credito. SIEMPRE pasa por identificarse.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// ESTE ES EL PUNTO QUE HACE CUMPLIR EL INGRESO
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes iba directo a los creditos activos. Como las pantallas de ingreso se
    /// abren con push relativo —no absoluto, porque encadenar navegacion absoluta
    /// en cold start rompe el ruteo—, el boton "atras" desde el ingreso vuelve
    /// aca. Si desde aca se pudiera entrar derecho a cobrar, el ingreso seria
    /// decorativo: bastaria un "atras" y volver a tocar el boton.
    ///
    /// Asi que la puerta esta aca, no en que la pantalla sea inescapable: sin
    /// sesion, este boton lleva al ingreso. Con sesion, entra directo, para no
    /// pedirle la clave dos veces al mismo cajero en la misma sesion.
    ///
    /// La venta a credito (arriba) no pasa por nada de esto: entra por HioPos,
    /// donde el cajero ya se identifico en el POS.
    /// </summary>
    [RelayCommand]
    private async Task GoPagarCreditoAsync()
    {
        try
        {
            if (await auth.RequiereConfiguracionInicialAsync())
            {
                await nav.GoToConfigurarAdminAsync();
                return;
            }

            if (sesion.Actual is null)
            {
                await nav.GoToIngresoCajeroAsync();
                return;
            }

            await nav.GoToCreditosActivosAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("HomeViewModel", "Error navegando al flujo de abonos", ex);
        }
    }

    /// <summary>Cierra la sesion del cajero: el siguiente abono vuelve a pedir clave.</summary>
    [RelayCommand]
    private void CerrarSesion() => sesion.Cerrar();

    // NombreVisible y no Nombre: el nombre es opcional al dar de alta al cajero, y
    // con Nombre="" el encabezado quedaba sin nadie. Ver [Cajero.NombreVisible].
    public string CajeroActual => sesion.Actual?.NombreVisible ?? string.Empty;

    public bool HaySesion => sesion.Actual is not null;
}