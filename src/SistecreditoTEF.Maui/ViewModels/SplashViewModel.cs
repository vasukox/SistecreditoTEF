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

    /// <summary>Evita que dos llamadas simultaneas decidan a la vez.</summary>
    private bool _decidiendo;

    /// <summary>
    /// Resuelve el destino y navega.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// LA PANTALLA NEGRA QUE ESTO ARREGLA
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Sintoma reportado desde la tienda: el cajero esta facturando, elige
    /// Sistecredito, el modulo abre en "consultar cliente", el cajero SE SALE, y a
    /// partir de ahi el icono del TEF ya no abre los abonos: queda una pantalla
    /// negra sin nada.
    ///
    /// Eran dos cosas encadenadas, y esta pantalla es las dos:
    ///
    ///   1. Esta es la RAIZ del Shell, y el flujo de HioPos se empuja encima. El
    ///      "atras" desde la captura de cliente, entonces, vuelve ACA. Y aca no hay
    ///      nada: el fondo es [NavSurface] (#333333) y el unico contenido es el
    ///      aviso de error, oculto mientras no haya error. En un terminal eso se ve
    ///      exactamente como una pantalla negra.
    ///
    ///   2. La decision se tomaba UNA sola vez (<c>_yaDecidio</c>). Al volver, esta
    ///      pantalla no volvia a decidir nada, asi que la pantalla negra no se iba
    ///      nunca: ni tocando el icono, porque el Shell ya estaba aca.
    ///
    /// Y encima se preguntaba por <c>state.HioposTransactionActive</c>, que sigue
    /// encendido en una venta que el cajero abandono —solo lo apaga la entrega del
    /// resultado al POS, que nunca ocurrio—. Con esa bandera pegada en true, el
    /// arranque desde el icono tambien se iba por la rama "no decido nada" y
    /// terminaba en la misma pantalla negra, ahora ya sin relacion con HioPos.
    ///
    /// Ahora la raiz se calla SOLO mientras MainActivity todavia tiene que navegar
    /// por la accion actual ([ILaunchContext.NavegacionConsumida]). En cuanto navego,
    /// volver aca es un cajero que se salio, y entonces esta pantalla decide.
    /// </summary>
    public async Task DecidirYNavegarAsync()
    {
        if (_decidiendo) return;
        _decidiendo = true;

        try
        {
            // ─────────────────────────────────────────────────────────────────
            // MIENTRAS HIOPOS TENGA QUE NAVEGAR, ACA NO SE DECIDE NADA
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
            //
            // La condicion lleva [NavegacionConsumida] a proposito. Sin ella, la
            // rama se quedaba callada TAMBIEN cuando el cajero volvia atras desde la
            // primera pantalla del flujo, y ahi callarse es la pantalla negra: el
            // "atras" cae en esta raiz, que no tiene contenido.
            //
            // Y con ella, un relanzamiento CALIENTE desde HioPos —que resetea el
            // Shell a esta raiz antes de empujar la pantalla nueva— sigue quedandose
            // quieto, porque el intent nuevo apago la bandera al llegar.
            if (launch.EsDeHiopos && !launch.NavegacionConsumida)
            {
                AppLogger.I("SplashViewModel",
                    $"Arranque de HioPos (action={launch.Action}): la navegacion la maneja MainActivity.");
                return;
            }

            // ─────────────────────────────────────────────────────────────────
            // EL CAJERO SE SALIO DE LA OPERACION DE HIOPOS
            // ─────────────────────────────────────────────────────────────────
            // Volver a la raiz con una operacion de HioPos viva significa que el
            // cajero abandono la venta. Se suelta ACA, y no mas adelante, por una
            // razon de plata: [ReciboPagoViewModel] elige como cerrar segun el modo
            // —al POS o con FinishAffinity—, y una venta viva mal soltada haria que
            // un ABONO se le devuelva a HioPos como si fuera el cobro de la factura.
            //
            // Soltar incluye [state.Clear]: los datos de esa venta (documento,
            // cliente, credito) no pueden seguir vivos en lo que el cajero haga
            // despues.
            //
            // HioPos se entera cuando la app cierre: recibe RESULT_CANCELED y
            // deselecciona el medio de pago, que es lo que significa abandonarla.
            if (state.HioposTransactionActive)
            {
                AppLogger.W("SplashViewModel",
                    "Se volvio a la raiz con una operacion de HioPos viva: el cajero la " +
                    "abandono. Se suelta el estado de la venta y se sigue como abonos.");
                state.Clear();
                standalone.Reset();
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
        }
        finally
        {
            _decidiendo = false;
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
