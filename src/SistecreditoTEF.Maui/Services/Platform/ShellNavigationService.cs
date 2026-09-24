using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion de [INavigationService] sobre MAUI Shell.
///
/// V3 (DRY): rutas centralizadas en [AppRoutes] - antes estaban
/// hardcodeadas en 8 archivos.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// TODAS LAS NAVEGACIONES ESPERAN A QUE EL SHELL EXISTA
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>Shell.Current</c> es null hasta que MAUI termina de construir la ventana, y
/// antes cada metodo hacia <c>Shell.Current.GoToAsync(...)</c> directo. En cold
/// start eso es una CARRERA: la pantalla raiz decide el destino y navega apenas
/// puede, que a veces es antes de que el Shell exista.
///
/// Capturado en el terminal:
///
///   I/SplashViewModel: Destino resuelto: Ingreso.
///   E/SplashViewModel: System.NullReferenceException
///        at ShellNavigationService.GoToIngresoCajeroAsync()
///        at SplashViewModel.DecidirYNavegarAsync()
///
/// La app quedaba sin poder entrar a la pantalla de ingreso.
///
/// La carrera existia desde antes; lo que la tapaba era la animacion de marca del
/// splash, que le regalaba ~350 ms de ventaja al Shell. Al quitar la animacion
/// —que era un bloqueante real en el arranque— la carrera quedo a la vista. O sea
/// que la animacion era load-bearing por accidente: nadie habia decidido que la
/// navegacion dependiera de ella.
///
/// La solucion no es devolver la espera ciega sino esperar a lo que de verdad hace
/// falta. Se centraliza en [IrAsync], asi ningun metodo nuevo puede volver a
/// caer en lo mismo.
/// </summary>
public sealed class ShellNavigationService : INavigationService
{
    /// <summary>Techo de espera por el Shell. Generoso, pero no infinito.</summary>
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(5);

    /// <summary>Intervalo de sondeo mientras el Shell se construye.</summary>
    private static readonly TimeSpan Sondeo = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Devuelve el Shell en cuanto exista. En el caso normal —ya construido— no
    /// cuesta nada: se resuelve en la primera comprobacion, sin ceder el control.
    /// </summary>
    private static async Task<Shell> ShellListoAsync()
    {
        if (Shell.Current is { } yaEsta) return yaEsta;

        var limite = DateTime.UtcNow + EsperaMaxima;
        var avisado = false;

        while (DateTime.UtcNow < limite)
        {
            await Task.Delay(Sondeo);
            if (Shell.Current is { } shell)
            {
                if (avisado)
                    AppLogger.I("ShellNavigationService", "El Shell ya esta disponible.");
                return shell;
            }

            if (!avisado)
            {
                avisado = true;
                AppLogger.I("ShellNavigationService",
                    "Se esperara a que el Shell termine de construirse antes de navegar.");
            }
        }

        // Si en 5 segundos no aparecio, algo mas grave pasa. Se lanza para que el
        // llamador lo reporte —la pantalla raiz muestra el aviso con "Reintentar"—
        // en vez de tragarse el problema y dejar una pantalla muerta.
        throw new InvalidOperationException(
            "El Shell no quedo disponible dentro del tiempo de espera: no se puede navegar.");
    }

    private static async Task IrAsync(string ruta)
    {
        var shell = await ShellListoAsync();
        await shell.GoToAsync(ruta);
    }

    private static async Task IrAsync(string ruta, Dictionary<string, object> parametros)
    {
        var shell = await ShellListoAsync();
        await shell.GoToAsync(ruta, parametros);
    }

    public Task GoToCapturaCedulaAsync() => IrAsync(AppRoutes.CapturaCedula);

    public Task GoToValidacionClienteAsync() => IrAsync(AppRoutes.ValidacionCliente);

    public Task GoToSeleccionCuotasAsync() => IrAsync(AppRoutes.SeleccionCuotas);

    public Task GoToOtpAsync() => IrAsync(AppRoutes.Otp);

    public Task GoToConfirmacionAsync(Credit credit) => IrAsync(AppRoutes.Confirmacion);

    public Task GoToPagoAsync(ActiveCredit credit) =>
        IrAsync(AppRoutes.Pago, new Dictionary<string, object>
        {
            { AppRoutes.Params.Payment, credit }
        });

    public Task GoToReciboPagoAsync(Payment payment) =>
        IrAsync(AppRoutes.ReciboPago, new Dictionary<string, object>
        {
            { AppRoutes.Params.Payment, payment }
        });

    public Task GoToCreditosActivosAsync() => IrAsync(AppRoutes.CreditosActivos);

    public Task GoToHomeAsync() => IrAsync(AppRoutes.Home);

    // ------------------------------------------------------------------
    // Ingreso de cajeros
    // ------------------------------------------------------------------

    /// <summary>
    /// Configuracion inicial y ingreso del cajero: PUSH relativo, no absoluto.
    ///
    /// La tentacion es usar "//" para que el boton atras no las saltee, pero en cold
    /// start el Shell ya arranca en Home por si mismo y encadenar una navegacion
    /// absoluta ahi carrea con la inicial y deja el ruteo roto —despues los botones
    /// "no hacen nada" porque el push relativo falla en silencio—. Ya paso en este
    /// proyecto.
    ///
    /// El control no se apoya en que la pantalla sea inescapable: el "atras" lleva a
    /// Home, y desde Home la opcion de pagar credito vuelve a exigir el ingreso
    /// (ver [HomeViewModel]). El unico camino a los creditos activos pasa por
    /// identificarse.
    /// </summary>
    public Task GoToConfigurarAdminAsync() => IrAsync(AppRoutes.ConfigurarAdmin);

    public Task GoToIngresoCajeroAsync() => IrAsync(AppRoutes.IngresoCajero);

    /// <summary>Administracion. Push: el "atras" vuelve al ingreso.</summary>
    public Task GoToAdminCajerosAsync() => IrAsync(AppRoutes.AdminCajeros);

    /// <summary>Replicacion entre cajas. Push: el "atras" vuelve a administracion.</summary>
    public Task GoToReplicacionAsync() => IrAsync(AppRoutes.Replicacion);

    /// <summary>
    /// La misma pantalla, en modo ACTUALIZAR. El modo viaja como parametro de ruta
    /// para que la pantalla no tenga que adivinarlo del estado de la caja: "ya hay
    /// PIN configurado" no distingue a un instalador que viene a actualizar de uno
    /// que se equivoco de boton.
    /// </summary>
    public Task GoToActualizarCajerosAsync() =>
        IrAsync(AppRoutes.Replicacion, new Dictionary<string, object>
        {
            { AppRoutes.Params.SoloCajeros, true }
        });
}
