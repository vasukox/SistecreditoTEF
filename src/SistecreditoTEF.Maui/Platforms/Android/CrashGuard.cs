using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// La red de seguridad del proceso. Se instala una vez, al arrancar.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QUE PROBLEMA RESUELVE
/// ─────────────────────────────────────────────────────────────────────────────
/// Hasta ahora TODA la proteccion contra caidas era artesanal: trece <c>async
/// void</c> y cuatro tareas sueltas, cada uno con su try/catch escrito a mano. Eso
/// cubre lo que alguien se acordo de cubrir.
///
/// La prueba de que no alcanza la dio el terminal: el 22/09 la app se cayo en la
/// caja 3 de la primera tienda por un <c>AsyncDisposableServiceDispose</c> que
/// lanzaba el CONTENEDOR de dependencias al liberar un ViewModel. La pantalla
/// tenia su try/catch; la excepcion no venia por ahi. Sin un manejador global no
/// habia forma de que ese fallo no cerrara la app, ni de enterarse sin tener un
/// logcat abierto de casualidad.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LAS TRES PUERTAS
/// ─────────────────────────────────────────────────────────────────────────────
///   • <c>AndroidEnvironment.UnhandledExceptionRaiser</c>: excepciones que suben
///     por el hilo de Android. Es la UNICA de las tres que se puede marcar como
///     atendida para que el proceso NO muera.
///   • <c>AppDomain.UnhandledException</c>: ultimo aviso del runtime. Ya no se
///     puede evitar la caida; solo dejar rastro.
///   • <c>TaskScheduler.UnobservedTaskException</c>: una Task que fallo y nadie
///     miro. Se marca observada y se sigue.
/// </summary>
public static class CrashGuard
{
    private static bool _instalado;
    private static CrashHandler? _handler;

    /// <summary>Tope de fallos atendidos antes de dejar de mantener viva la app.</summary>
    private const int MaxFallosSostenibles = 5;

    public static void Instalar(IServiceProvider services)
    {
        if (_instalado) return;
        _instalado = true;

        try
        {
            var carpeta = Path.Combine(
                Microsoft.Maui.Storage.FileSystem.AppDataDirectory, FileCrashStore.NombreCarpeta);

            _handler = new CrashHandler(new FileCrashStore(carpeta))
            {
                PantallaActual = () => Shell.Current?.CurrentPage?.GetType().Name,
                Build = () => BuildInfo.Descripcion,
                AvisarAlPos = () => AvisarAlPos(services)
            };

            global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            {
                var fallo = _handler.Registrar(e.Exception, CrashOrigin.Android);

                // ─────────────────────────────────────────────────────────────
                // AQUI SE DECIDE SI LA CAJA SIGUE VIVA
                // ─────────────────────────────────────────────────────────────
                // Marcarlo atendido evita que Android mate el proceso. Se hace a
                // conciencia: en una caja, que la app se cierre en medio de la
                // atencion es peor que seguir con un estado dudoso, porque al POS
                // ya se le contesto y el cajero puede volver a empezar.
                //
                // Con el tope: si el mismo fallo se repite sin parar, sostener el
                // proceso solo esconde el problema. A partir del sexto se deja
                // caer, que ya es una senal clara de que esa caja necesita mirada.
                if (_handler.Contador <= MaxFallosSostenibles)
                {
                    e.Handled = true;
                    VolverAUnaPantallaSegura();
                }
                else
                {
                    AppLogger.E("CrashGuard",
                        $"Sexto fallo seguido ({fallo.Tipo}); se deja caer el proceso en vez de " +
                        "sostener una app en bucle.");
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    _handler.Registrar(ex, CrashOrigin.Runtime);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                _handler.Registrar(e.Exception, CrashOrigin.TaskNoObservada);
                e.SetObserved();
            };

            AppLogger.I("CrashGuard",
                $"Red de seguridad instalada. Fallos previos guardados: " +
                $"{new FileCrashStore(carpeta).Listar().Count}. Build: {BuildInfo.Descripcion}.");
        }
        catch (Exception ex)
        {
            // Que falle la instalacion de la red de seguridad no puede, a su vez,
            // tumbar el arranque.
            AppLogger.E("CrashGuard", "No se pudo instalar la red de seguridad.", ex);
        }
    }

    /// <summary>
    /// Le contesta a HioPos si hay una operacion viva. Sin esto, el POS espera,
    /// agota su tiempo y cierra la venta SIN COBRAR.
    /// </summary>
    private static void AvisarAlPos(IServiceProvider services)
    {
        var state = services.GetService<ITransactionStateStore>();
        if (state is null || !state.HioposTransactionActive) return;

        var builder = services.GetService<HioposResultBuilder>();
        var handler = services.GetService<ITransactionResultHandler>();
        if (builder is null || handler is null) return;

        AppLogger.W("CrashGuard",
            "Habia una operacion viva de HioPos cuando ocurrio el fallo: se le responde " +
            "para que no cierre la venta por tiempo.");

        handler.FinishWithResult(
            builder.BuildUnexpectedFailure(state.ActiveTransaction?.TransactionType));
    }

    /// <summary>
    /// Deja la interfaz en un lugar conocido. Sostener el proceso con una pantalla
    /// a medio construir es cambiar un cierre por algo peor: una caja que parece
    /// viva y responde cualquier cosa.
    /// </summary>
    private static void VolverAUnaPantallaSegura()
    {
        try
        {
            if (Shell.Current is null) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    Shell.Current.GoToAsync(AppRoutes.Splash);
                }
                catch (Exception ex)
                {
                    AppLogger.W("CrashGuard",
                        $"No se pudo volver a la pantalla inicial tras el fallo: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.W("CrashGuard", $"No se pudo agendar el retorno a inicio: {ex.Message}");
        }
    }
}
