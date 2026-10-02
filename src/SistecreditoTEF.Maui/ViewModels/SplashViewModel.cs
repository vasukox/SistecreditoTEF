using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Hiopos;
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
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LOS DOS MODULOS NO SE MEZCLAN, Y ESTA PANTALLA ES DONDE PODRIAN
/// ─────────────────────────────────────────────────────────────────────────────
/// El que entra por la POS es de la POS; el que se abre a mano es de abonos. Esta
/// raiz es el unico lugar comun de los dos, asi que es el unico lugar donde se
/// pueden juntar por accidente.
///
/// La regla, entonces, es dura: mientras la POS tenga una operacion viva, ACA NO SE
/// DECIDE NADA. Ni se navega, ni se cambia de modo, ni se limpia estado.
///
/// Ya se intento una version mas lista —que distinguia "MainActivity todavia no
/// navego" de "el cajero volvio atras"— y salio mal: depende de CUANDO el framework
/// entrega el evento de aparicion de la pagina, y cuando llega tarde esta pantalla
/// decide en plena venta y le borra el estado. El sintoma en caja fue que el boton
/// "Volver a la POS" aterrizaba en pagar credito. Una regla que depende del orden de
/// un evento del framework no es una regla.
/// </summary>
public partial class SplashViewModel(
    AuthService auth,
    ISesionCajero sesion,
    IStandaloneModeTracker standalone,
    ITransactionStateStore state,
    ILaunchContext launch,
    IHioposExit salida,
    INavigationService nav,
    ITiendaEnOperacion tienda) : ObservableObject
{
    [ObservableProperty]
    private string? errorMessage;

    public bool TieneError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(TieneError));

    /// <summary>
    /// Aviso y salida cuando esta pantalla queda a la vista con la POS al mando.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// LA PANTALLA NEGRA
    /// ─────────────────────────────────────────────────────────────────────────
    /// Esta pagina es la raiz del Shell y el flujo de la POS se EMPUJA encima, asi
    /// que un "atras" mal atendido cae aca. Y aca no hay nada: fondo #333333 y un
    /// aviso de error que esta oculto mientras no haya error. En un terminal eso se
    /// ve como una pantalla negra de la que no se sale.
    ///
    /// La respuesta NO es que esta pantalla decida —eso mezcla los dos modulos—
    /// sino que deje de estar vacia: si el cajero termina aca con la POS al mando,
    /// tiene que ver que pasa y poder volver a la POS.
    /// </summary>
    [ObservableProperty]
    private string? mensajeDeLaPos;

    public bool TieneMensajeDeLaPos => !string.IsNullOrEmpty(MensajeDeLaPos);

    partial void OnMensajeDeLaPosChanged(string? value) =>
        OnPropertyChanged(nameof(TieneMensajeDeLaPos));

    /// <summary>Evita que dos llamadas simultaneas decidan a la vez.</summary>
    private bool _decidiendo;

    /// <summary>
    /// Cuanto se espera antes de mostrar la salida a la POS.
    ///
    /// En un arranque normal esta pantalla vive decenas de milisegundos, asi que sin
    /// esta espera el aviso destellaria en CADA venta. Si al cumplirse el plazo la
    /// POS sigue al mando, es que el cajero de verdad se quedo aca.
    /// </summary>
    private static readonly TimeSpan EsperaAntesDeOfrecerLaSalida = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Resuelve el destino y navega.
    ///
    /// Se ejecuta en CADA aparicion de la pagina, no una sola vez. Antes decidia una
    /// unica vez en la vida de la app: al volver a la raiz no volvia a decidir nada y
    /// la pantalla negra no se iba nunca. El unico guard que queda es contra dos
    /// llamadas simultaneas.
    /// </summary>
    public async Task DecidirYNavegarAsync()
    {
        if (_decidiendo) return;
        _decidiendo = true;

        try
        {
            // ─────────────────────────────────────────────────────────────────
            // SI LA POS ESTA AL MANDO, ACA NO SE DECIDE NADA
            // ─────────────────────────────────────────────────────────────────
            // Las dos condiciones cubren dos momentos distintos y las dos hacen
            // falta:
            //
            //   [EsDeHiopos]              el arranque. Esta pantalla aparece ANTES de
            //                             que MainActivity navegue, cuando la bandera
            //                             de transaccion todavia no se encendio.
            //                             Decidir ahi hacia que el modulo pidiera la
            //                             clave del cajero en medio de una factura.
            //
            //   [HioposTransactionActive]  la operacion en curso, ya encendida.
            //
            // Tampoco se toca IsStandalone: marcar el modo standalone durante una
            // venta corrompe el cierre —[ReciboPagoViewModel] elige entre devolverle
            // el resultado al POS o cerrar con FinishAffinity segun ese modo—.
            if (launch.EsDeHiopos || state.HioposTransactionActive)
            {
                AppLogger.I("SplashViewModel",
                    $"La POS esta al mando (action={launch.Action}, " +
                    $"operacionViva={state.HioposTransactionActive}): la navegacion la " +
                    "maneja MainActivity.");

                OfrecerLaSalidaSiSeQuedaAca();
                return;
            }

            MensajeDeLaPos = null;
            standalone.IsStandalone = true;

            // ─────────────────────────────────────────────────────────────────
            // EL MONTAJE VA POR PASOS, Y LA TIENDA ES EL PRIMERO
            // ─────────────────────────────────────────────────────────────────
            // Antes la primera pantalla de una caja nueva era el formulario del
            // PIN, con la tienda como una tarjeta mas ahi dentro. El orden
            // importaba: el instalador creaba el PIN, daba de alta a los cajeros,
            // y la tienda —lo unico que decide a nombre de quien quedan los
            // creditos— quedaba para el final o no se tocaba. Asi fue como una
            // caja de Suba termino reportando a la 037.
            //
            // Ahora la tienda se pregunta ANTES que nada y las dos preguntas del
            // montaje van en pantallas separadas:
            //
            //   paso 1   ¿en que tienda esta esta caja?   -> asistente de tienda
            //   paso 2   ¿como se configura?              -> copiar / desde cero
            //
            // Una caja YA configurada a la que le falta la tienda no esta
            // montandose: va a la misma pantalla pero en modo ajustes, elige, y al
            // volver esta raiz la manda a operar.
            var requiereConfiguracion = await auth.RequiereConfiguracionInicialAsync();
            var hayTienda = tienda.EstaProvisionada;

            var destino =
                requiereConfiguracion && !hayTienda ? Destino.ElegirTienda
                : requiereConfiguracion             ? Destino.ComoConfigurar
                : !hayTienda                        ? Destino.TiendaPendiente
                : sesion.Actual is null             ? Destino.Ingreso
                :                                     Destino.Cobrar;

            AppLogger.I("SplashViewModel",
                $"Destino resuelto: {destino} (PIN configurado={!requiereConfiguracion}, " +
                $"tienda elegida={hayTienda}).");

            switch (destino)
            {
                case Destino.ElegirTienda:    await nav.GoToElegirTiendaAsistenteAsync(); break;
                case Destino.ComoConfigurar:  await nav.GoToConfiguracionInicioAsync();   break;
                case Destino.TiendaPendiente: await nav.GoToTiendaAsync();                break;
                case Destino.Ingreso:         await nav.GoToIngresoCajeroAsync();         break;
                case Destino.Cobrar:          await nav.GoToCreditosActivosAsync();       break;
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

    /// <summary>
    /// Si al cabo de un momento la POS sigue al mando, esta pantalla dejo de ser un
    /// paso de arranque y paso a ser donde el cajero se quedo. Se le muestra que
    /// pasa y como volver.
    /// </summary>
    private void OfrecerLaSalidaSiSeQuedaAca() =>
        Fire.AndForget(async () =>
        {
            await Task.Delay(EsperaAntesDeOfrecerLaSalida);

            if (!launch.EsDeHiopos && !state.HioposTransactionActive) return;

            MensajeDeLaPos = state.HioposTransactionActive
                ? "La POS tiene una operacion abierta en este modulo."
                : "Este modulo lo abrio la POS.";
        }, "SplashViewModel");

    /// <summary>
    /// Devuelve el control a la POS. Es la salida de emergencia de esta pantalla, y
    /// NO cruza a abonos: los dos modulos se mantienen separados.
    ///
    /// Si no hay nada que devolverle a la POS, entonces la POS ya no esta al mando y
    /// se resuelve el destino normalmente.
    /// </summary>
    [RelayCommand]
    private async Task VolverALaPosAsync()
    {
        if (salida.Volver("SplashPage")) return;

        AppLogger.W("SplashViewModel",
            "No habia operacion que devolverle a la POS; se resuelve el destino.");

        MensajeDeLaPos = null;
        launch.Action = null;
        await DecidirYNavegarAsync();
    }

    /// <summary>
    /// A donde entra la app. Los tres primeros son el montaje; los dos ultimos, la
    /// operacion normal.
    /// </summary>
    private enum Destino
    {
        /// <summary>Paso 1 del montaje: elegir la tienda, con confirmacion.</summary>
        ElegirTienda,

        /// <summary>Paso 2 del montaje: copiar de otra caja o configurar de cero.</summary>
        ComoConfigurar,

        /// <summary>
        /// Caja ya configurada a la que le falta la tienda. No se esta montando:
        /// se le pide la tienda como ajuste y al volver sigue operando.
        /// </summary>
        TiendaPendiente,

        Ingreso,
        Cobrar
    }

    [RelayCommand]
    private async Task ReintentarAsync()
    {
        ErrorMessage = null;
        await DecidirYNavegarAsync();
    }
}
