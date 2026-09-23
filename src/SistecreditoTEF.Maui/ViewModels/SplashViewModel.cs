using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Decide a que modulo entra la app, antes de mostrar ninguno.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DESTELLO QUE ESTO ELIMINA
/// ─────────────────────────────────────────────────────────────────────────────
/// La raiz del Shell era HomePage. El Shell navega a su raiz por si mismo, antes
/// de que nadie pueda decidir, asi que al abrir desde el icono se veia el menu un
/// instante y DESPUES la app saltaba al modulo real.
///
/// Ahora la raiz es una pantalla de marca —neutra, no un modulo— y el destino se
/// resuelve desde aca. Nunca se ve un modulo que no corresponde.
/// </summary>
public partial class SplashViewModel(
    AuthService auth,
    ISesionCajero sesion,
    IStandaloneModeTracker standalone,
    ITransactionStateStore state,
    ILaunchContext launch,
    INavigationService nav) : ObservableObject
{
    [ObservableProperty]
    private string? errorMessage;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));

    /// <summary>Evita decidir dos veces si la pagina reaparece.</summary>
    private bool _yaDecidio;

    /// <summary>
    /// Resuelve el destino y navega.
    ///
    /// Si la operacion la origino HioPos, NO se navega: [MainActivity] ya se
    /// encarga —lleva a la captura de cliente en una venta, o al recaudo en una
    /// entrada de caja— y meter una segunda navegacion encima produce
    /// exactamente el salto de pantallas que se quiere evitar.
    /// </summary>
    public async Task DecidirYNavegarAsync()
    {
        if (_yaDecidio) return;
        _yaDecidio = true;

        try
        {
            // ─────────────────────────────────────────────────────────────────
            // SI LA APP LA ABRIO HIOPOS, ACA NO SE DECIDE NADA
            // ─────────────────────────────────────────────────────────────────
            // Se pregunta por [ILaunchContext] y no por HioposTransactionActive.
            // Ese flag se enciende en HandleTransaction, que corre en OnResume, o
            // sea DESPUES de que esta pantalla ya aparecio: preguntarlo aca daba
            // "no hay transaccion" incluso en una venta, y el modulo terminaba
            // pidiendo la clave del cajero en medio de una factura.
            //
            // Tampoco se toca IsStandalone en ese caso: marcar el modo standalone
            // durante una venta corrompe el cierre (se esperaria FinishAffinity en
            // vez de devolverle el resultado al POS).
            if (launch.EsDeHiopos || state.HioposTransactionActive)
            {
                AppLogger.I("SplashViewModel",
                    $"Arranque de HioPos (action={launch.Action}): la navegacion la maneja MainActivity.");
                return;
            }

            standalone.IsStandalone = true;

            var destino =
                await auth.RequiereConfiguracionInicialAsync() ? Destino.Configuracion
                : sesion.Actual is null                        ? Destino.Ingreso
                :                                                Destino.Cobrar;

            AppLogger.I("SplashViewModel", $"Destino resuelto: {destino}.");

            switch (destino)
            {
                case Destino.Configuracion: await nav.GoToConfigurarAdminAsync(); break;
                case Destino.Ingreso:       await nav.GoToIngresoCajeroAsync();   break;
                case Destino.Cobrar:        await nav.GoToCreditosActivosAsync(); break;
            }
        }
        catch (Exception ex)
        {
            // Sin esto la app se quedaria en la pantalla de marca para siempre, sin
            // decir nada. Se permite reintentar en vez de obligar a reiniciar.
            AppLogger.E("SplashViewModel", "No se pudo resolver el modulo de inicio.", ex);
            ErrorMessage = "No se pudo abrir el modulo. Verifica la instalacion y reintenta.";
            _yaDecidio = false;
        }
    }

    private enum Destino { Configuracion, Ingreso, Cobrar }

    [RelayCommand]
    private async Task ReintentarAsync()
    {
        ErrorMessage = null;
        await DecidirYNavegarAsync();
    }
}
