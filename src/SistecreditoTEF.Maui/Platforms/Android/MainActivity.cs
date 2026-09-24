using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// Entry point de la app en Android.
///
/// HioPosCloud lanza Intents con action
/// "icg.actions.electronicpayment.{apk_name}.XXX", donde {apk_name} es
/// [HioposActions.ApkName] — hoy <c>"permoda"</c>.
///
/// QA M-18: este comentario decia ".sistecredito.XXX", que NO es el apk_name
/// real. El codigo siempre estuvo bien (usa la constante), pero el comentario
/// desinformaba sobre el dato mas critico de la integracion: si el apk_name no
/// coincide con el alta en HioPosCloud, la app nunca recibe el Intent.
///
/// Esta Activity
/// los dispatchea a [HioposResultBuilder] o navega a la primera
/// pagina del flujo TRANSACTION.
///
/// Cambios respecto al original:
///   - B1: en lugar de Quit(), entrega el resultado al POS via
///         [ITransactionResultHandler.FinishWithResult].
///   - B6: maneja las 11 actions (antes solo 4 + default Canceled).
///   - B7: lee DocumentPath via [IDocumentReader] y persiste [HioposTransaction]
///         en [ITransactionStateStore] para que las Pages lo consuman.
///   - B8/B9: inicializa token store, idempotency store, audit logger.
///   - O9/O10: el switch usa constantes de [HioposActions]; el
///         [IntentFilter] vive aqui (sin duplicar con el XML).
///
/// launchMode=singleTask: si HioPos nos lanza otro Intent mientras
/// estamos en foreground, NO recrea la Activity -> onNewIntent().
/// </summary>
[Activity(
    Label = "SistecreditoTEF",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
                          | ConfigChanges.KeyboardHidden | ConfigChanges.Locale
                          | ConfigChanges.LayoutDirection,
    Exported = true)]
[IntentFilter(
    actions: new[] {
        HioposActions.Initialize,
        HioposActions.Finalize,
        HioposActions.GetBehavior,
        HioposActions.GetVersion,
        HioposActions.ShowSetupScreen,
        HioposActions.Transaction,
        HioposActions.GetCustomParams,
        HioposActions.GetPrintInfo,
        HioposActions.ReadCard,
        HioposActions.ChargeCard,
        HioposActions.GetCardData,
        // Se declara para poder RECHAZARLO. Ver [HioposActions.VoidTransaction]:
        // es la otra puerta por la que HioPos puede mandar un abono.
        HioposActions.VoidTransaction
    },
    Categories = new[] { Intent.CategoryDefault })]
[IntentFilter(
    actions: new[] { "android.intent.action.MAIN" },
    Categories = new[] { "android.intent.category.LAUNCHER" })]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // ─────────────────────────────────────────────────────────────────────
        // ESTO VA ANTES DE base.OnCreate, Y ES A PROPOSITO
        // ─────────────────────────────────────────────────────────────────────
        // base.OnCreate construye el Shell y con el la pantalla raiz. Si el
        // contexto de arranque se registrara despues, la raiz ya se habria
        // preguntado "¿esto es una venta o un abono?" con la informacion todavia
        // ausente — y respondia "abono", pidiendo la clave del cajero en medio de
        // una factura.
        try
        {
            var launch = IPlatformApplication.Current?.Services
                .GetService(typeof(ILaunchContext)) as ILaunchContext;
            if (launch is not null)
            {
                launch.Action = Intent?.Action;
                Log.Info("MainActivity",
                    $"Contexto de arranque: action={Intent?.Action ?? "(null)"}, " +
                    $"esDeHiopos={launch.EsDeHiopos}");
            }
            else
            {
                Log.Warn("MainActivity",
                    "ILaunchContext no resoluble en OnCreate; la raiz podria decidir mal.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("MainActivity", $"No se pudo registrar el contexto de arranque: {ex.Message}");
        }

        base.OnCreate(savedInstanceState);
        Log.Info("MainActivity", $"After base.OnCreate: Current={IPlatformApplication.Current != null}, Services={IPlatformApplication.Current?.Services != null}");
        // Guardamos el Intent para procesarlo en OnResume, donde MAUI
        // ya termino su bootstrap (IPlatformApplication.Current.Services
        // garantizado disponible).
        _pendingIntent = Intent;
    }

    protected override void OnStart()
    {
        base.OnStart();
        Log.Info("MainActivity", $"After OnStart: Current={IPlatformApplication.Current != null}, Services={IPlatformApplication.Current?.Services != null}");
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null) return;

        // Reemplaza Intent (propiedad heredada de Activity)
        // para que MainActivity.Intent apunte al nuevo.
        Intent = intent;
        // Guardamos el nuevo intent; OnResume lo procesa.
        _pendingIntent = intent;

        // ─────────────────────────────────────────────────────────────────────
        // EL CONTEXTO DE ARRANQUE TAMBIEN SE ACTUALIZA ACA
        // ─────────────────────────────────────────────────────────────────────
        // Solo se seteaba en OnCreate, asi que con launchMode=singleTask —donde la
        // Activity se reutiliza— [ILaunchContext] se quedaba describiendo el intent
        // con el que la app se abrio por PRIMERA vez. Una venta abierta desde el
        // icono seguia diciendo "launcher", y al reves.
        //
        // Asignar Action ademas apaga NavegacionConsumida: el intent nuevo le
        // devuelve el mando a esta Activity.
        try
        {
            if (IPlatformApplication.Current?.Services
                    .GetService(typeof(ILaunchContext)) is ILaunchContext launch)
            {
                launch.Action = intent.Action;
                Log.Info("MainActivity",
                    $"Intent nuevo: action={intent.Action ?? "(null)"}, esDeHiopos={launch.EsDeHiopos}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("MainActivity", $"No se pudo actualizar el contexto de arranque: {ex.Message}");
        }
    }

    /// <summary>
    /// Que instancia de esta Activity abrio la operacion de HioPos que hoy esta viva.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// SIN ESTO, LA ACTIVITY QUE SE VA LE BORRA EL ESTADO A LA QUE LLEGA
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Cuando Android REEMPLAZA una Activity, el orden del ciclo de vida es:
    ///
    ///     nueva.OnCreate -> nueva.OnStart -> nueva.OnResume -> vieja.OnStop
    ///                                                       -> vieja.OnDestroy
    ///
    /// O sea que el OnDestroy de la que se va corre DESPUES de que la nueva ya
    /// atendio su intent. La limpieza de "venta abandonada" encontraba entonces la
    /// bandera de la venta NUEVA en true, la daba por abandonada y le borraba el
    /// estado a una operacion que recien empezaba.
    ///
    /// El sintoma en caja no se parecia en nada a la causa: el boton seguia diciendo
    /// "Volver a HioPos" —su texto se fija al dibujar la pantalla— pero al tocarlo ya
    /// no habia operacion viva que devolver, asi que hacia el "atras" normal. Ese pop
    /// cae en la raiz del Shell, que resuelve destino y manda a pagar credito. El
    /// cajero tocaba "Volver a HioPos" y aterrizaba en abonos.
    ///
    /// Con el dueno anotado, la Activity que se va solo limpia lo suyo.
    ///
    /// Weak a proposito: esto es estatico y vive lo que vive el proceso; una
    /// referencia fuerte a una Activity destruida es una fuga de la ventana entera.
    /// </summary>
    private static WeakReference<MainActivity>? _duenoDeLaOperacion;

    /// <summary>¿Fue ESTA instancia la que abrio la operacion viva?</summary>
    private bool EsDuenoDeLaOperacion =>
        _duenoDeLaOperacion is not null
        && _duenoDeLaOperacion.TryGetTarget(out var dueno)
        && ReferenceEquals(dueno, this);

    /// <summary>
    /// Cuando esta Activity se cierra SIN haberle respondido al POS, la venta deja
    /// de estar viva aunque nadie haya bajado la bandera.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// LA BANDERA PEGADA QUE DEJABA EL MODULO INUTILIZABLE
    /// ─────────────────────────────────────────────────────────────────────────────
    /// <c>HioposTransactionActive</c> la apaga un solo lugar:
    /// [AndroidTransactionResultHandler.FinishWithResult]. Si el cajero se sale con
    /// "atras" en medio de una venta, ese camino NUNCA corre: Android le devuelve
    /// RESULT_CANCELED al POS por su cuenta y cierra la Activity.
    ///
    /// Pero el PROCESO sigue vivo, y con el los singletons del contenedor. O sea que
    /// la bandera se quedaba en true sin nadie esperando nada del otro lado. A partir
    /// de ahi, [HioposIntentGuard] descartaba el intent del icono ("hay una factura
    /// esperando resultado") y el cajero no podia entrar mas a los abonos.
    ///
    /// Se limpia TODO el estado, no solo la bandera: los datos de una venta
    /// abandonada —documento, cliente, credito elegido— no pueden sobrevivir a la
    /// pantalla que los mostraba.
    ///
    /// Dos condiciones, y las dos hacen falta:
    ///
    ///   · <c>IsFinishing</c>: si Android esta recreando la Activity (memoria, cambio
    ///     de configuracion no declarado), la venta sigue en pie y borrarla seria peor
    ///     que el problema.
    ///
    ///   · [EsDuenoDeLaOperacion]: la venta viva tiene que ser LA NUESTRA. Sin esto, la
    ///     Activity que se va le borraba el estado a la que acababa de llegar —el
    ///     OnDestroy corre despues del OnResume de la nueva— y el cajero terminaba en
    ///     pagar credito al tocar "Volver a HioPos".
    /// </summary>
    protected override void OnDestroy()
    {
        try
        {
            if (IsFinishing
                && IPlatformApplication.Current?.Services is { } services
                && services.GetService(typeof(ITransactionStateStore))
                    is ITransactionStateStore state
                && state.HioposTransactionActive)
            {
                if (!EsDuenoDeLaOperacion)
                {
                    // Otra instancia ya tomo el relevo: lo que esta vivo es de ella.
                    Log.Info("MainActivity",
                        "Se cierra una Activity anterior con una operacion de HioPos viva " +
                        "que NO es suya: no se toca el estado.");
                }
                else
                {
                    Log.Warn("MainActivity",
                        "La Activity se cierra sin haberle respondido al POS: la venta se da " +
                        "por abandonada y se limpia el estado.");

                    state.Clear();
                    (services.GetService(typeof(IStandaloneModeTracker))
                        as IStandaloneModeTracker)?.Reset();
                    _duenoDeLaOperacion = null;
                }
            }
        }
        catch (Exception ex)
        {
            // OnDestroy no puede tirar: la app se estaria cerrando igual, y una
            // excepcion aca se lleva el proceso por delante.
            Log.Warn("MainActivity", $"No se pudo limpiar el estado al cerrar: {ex.Message}");
        }

        base.OnDestroy();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Log.Info("MainActivity", $"After OnResume: Current={IPlatformApplication.Current != null}, Services={IPlatformApplication.Current?.Services != null}");
        // OnResume se invoca DESPUES de que toda la cadena de
        // inicializacion de MAUI (Application.OnCreate -> MauiApp build
        // -> ServiceProvider) termino. Aca IPlatformApplication.Current.Services
        // siempre esta disponible.
        if (_pendingIntent is not null
            && IPlatformApplication.Current?.Services is not null)
        {
            var pending = _pendingIntent;
            _pendingIntent = null;

            // Guard contra colision de modos. singleTask hace que el LAUNCHER y
            // HioPos compartan la misma Activity, asi que hay que decidir cual
            // intent manda. La regla vive en [HioposIntentGuard] (pura y testeada):
            // las acciones de HioPos se procesan SIEMPRE porque es la autoridad
            // sobre la venta; solo se descarta el intent del launcher cuando hay
            // una factura esperando resultado.
            var state = IPlatformApplication.Current.Services
                .GetService(typeof(ITransactionStateStore)) as ITransactionStateStore;
            var standalone = IPlatformApplication.Current.Services
                .GetService(typeof(IStandaloneModeTracker)) as IStandaloneModeTracker;

            var decision = HioposIntentGuard.Evaluate(
                pending.Action,
                hioposTransactionActive: state?.HioposTransactionActive ?? false,
                isStandalone: standalone?.IsStandalone ?? false);

            if (decision.Discard)
            {
                Log.Warn("MainActivity", $"Intent descartado: {decision.Reason}");
                return;
            }

            var esLauncher = pending.Action == HioposIntentGuard.LauncherAction;

            // Una accion de HioPos manda sobre cualquier estado previo: si venimos
            // del modo standalone, o de una venta anterior que quedo a medias, se
            // limpia todo para que la venta nueva arranque en cero.
            //
            // Esto es lo que hace que al volver atras en HioPos y elegir
            // Sistecredito otra vez, el modulo tome el cliente de la venta ACTUAL y
            // no el de la anterior.
            if (!esLauncher)
            {
                if (standalone?.IsStandalone == true)
                {
                    Log.Info("MainActivity", "Cambio de modo: standalone -> hiopos.");
                    standalone.Reset();
                }
                if (state?.HioposTransactionActive == true)
                {
                    Log.Info("MainActivity",
                        "Llego una accion de HioPos con una venta previa sin cerrar: " +
                        "se descarta el estado viejo y se rehace con la venta nueva.");
                }
            }

            Log.Info("MainActivity", $"HandleIntent starting for action={pending.Action}");
            HandleIntent(pending, isNewIntent: false);
            Log.Info("MainActivity", "HandleIntent completed");
        }
        else
        {
            Log.Warn("MainActivity", $"OnResume skipped: _pendingIntent={_pendingIntent?.Action ?? "null"}, Services null={IPlatformApplication.Current?.Services == null}");
        }
    }


    /// <summary>
    /// Intent que se va a procesar cuando MAUI este completamente
    /// inicializado. Se setea en OnCreate / OnNewIntent, y se consume
    /// en OnResume (donde IPlatformApplication.Current.Services ya esta
    /// garantizado disponible).
    /// </summary>
    private Intent? _pendingIntent;

    private void HandleIntent(Intent? intent, bool isNewIntent)
    {
        Log.Info("MainActivity", $"HandleIntent ENTERED: action={intent?.Action}, isNewIntent={isNewIntent}");

        if (intent is null)
        {
            Log.Warn("MainActivity", "HandleIntent: intent is null");
            return;
        }

        var action = intent.Action ?? string.Empty;
        Log.Info("MainActivity", $"Processing action={action}");

        // LAUNCHER: el usuario abrio la app desde el icono del launcher
        // (modo standalone, SIN venta abierta en HI-POS). Navegamos directo
        // al flujo de pagos de creditos (abonos) para que el cajero no
        // tenga que hacer una factura nueva solo para entrar a la opcion.
        if (action == "android.intent.action.MAIN")
        {
            // ─────────────────────────────────────────────────────────────────
            // DESDE EL ICONO NO SE NAVEGA DESDE ACA
            // ─────────────────────────────────────────────────────────────────
            // Lo decide [SplashPage], que es la raiz del Shell. Antes se navegaba
            // aca Y el Shell navegaba a su raiz por su cuenta: dos navegaciones
            // compitiendo, que es lo que producia el salto de pantallas al abrir.
            //
            // Solo se marca el modo. La decision del destino vive en un lugar.
            Log.Info("MainActivity", "LAUNCHER action: app abierta desde icono (standalone).");
            var serviceProvider = IPlatformApplication.Current?.Services;
            serviceProvider?.GetRequiredService<IStandaloneModeTracker>().IsStandalone = true;
            return;
        }


        // Inicializar AppLogger (necesario porque en cold-start la
        // primera llamada a AppLogger puede no tener logger).
        AppLogger.I("MainActivity", $"Intent recibido: action={action} (newIntent={isNewIntent})");

        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException(
                "IPlatformApplication.Current.Services es null; la app MAUI no se termino de inicializar.");

        // INITIALIZE: persistir Token antes que cualquier otra cosa.
        if (action == HioposActions.Initialize)
        {
            var token = intent.GetStringExtra(HioposExtras.Token);
            if (!string.IsNullOrEmpty(token))
                services.GetRequiredService<ITokenStore>().SetToken(token);
        }

        // Switch exhaustivo (O9) por las 11 actions.
        // Robustez: si un handler lanza, NO debemos crashear la Activity (el
        // POS lo muestra como "error en modulo externo"); se captura abajo y
        // se devuelve un resultado seguro (Canceled) al POS.
        try
        {
        switch (action)
        {
            case HioposActions.Initialize:
                Log.Info("MainActivity", "INITIALIZE action received");
                HandleInitialize(intent, services);
                Log.Info("MainActivity", "INITIALIZE: SetResult called");
                break;
            case HioposActions.Finalize:
                Log.Info("MainActivity", "FINALIZE action received");
                HandleFinalize(services);
                Log.Info("MainActivity", "FINALIZE: SetResult called");
                break;
            case HioposActions.GetVersion:
                Log.Info("MainActivity", "GET_VERSION action received");
                HandleGetVersion(services);
                Log.Info("MainActivity", "GET_VERSION: SetResult called");
                break;
            case HioposActions.GetBehavior:
                Log.Info("MainActivity", "GET_BEHAVIOR action received");
                HandleGetBehavior(services);
                Log.Info("MainActivity", "GET_BEHAVIOR: SetResult called");
                break;
            case HioposActions.GetCustomParams:
                Log.Info("MainActivity", "GET_CUSTOM_PARAMS action received");
                HandleGetCustomParams(services);
                Log.Info("MainActivity", "GET_CUSTOM_PARAMS: SetResult called");
                break;
            case HioposActions.GetPrintInfo:
                Log.Info("MainActivity", "GET_PRINT_INFO action received");
                HandleGetPrintInfo(services);
                Log.Info("MainActivity", "GET_PRINT_INFO: SetResult called");
                break;
            case HioposActions.VoidTransaction:
                // Se rechaza SIEMPRE, sin mirar configuracion ni documento: esta
                // accion solo llega cuando el POS decidio mandar un abono por aca
                // en vez de por REFUND. Ver [HioposActions.VoidTransaction].
                Log.Warn("MainActivity", "VOID_TRANSACTION recibido: se rechaza siempre.");
                HandleVoidTransaction(intent, services);
                break;
            case HioposActions.Transaction:
                Log.Info("MainActivity", "TRANSACTION action received");
                HandleTransaction(intent, services);
                Log.Info("MainActivity", "TRANSACTION: SetResult called");
                break;
            case HioposActions.ShowSetupScreen:
                Log.Info("MainActivity", "SHOW_SETUP_SCREEN action received");
                HandleShowSetupScreen(services);
                Log.Info("MainActivity", "SHOW_SETUP_SCREEN: SetResult called");
                break;
            default:
                // READ_CARD, CHARGE_CARD, GET_CARD_DATA: CanChargeCard=false
                // y ReadCardFromApi=false, asi que el POS NO las dispara.
                // Si llegan (alguien configuro mal el CloudLicense),
                // devolvemos Canceled (B6).
                Log.Warn("MainActivity", $"Unsupported action: {action}");
                HandleUnsupported(services, action);
                break;
        }
        }
        catch (Exception ex)
        {
            AppLogger.E("MainActivity",
                $"Handler de '{action}' lanzo excepcion; se le responde al POS para no crashear.", ex);
            try
            {
                var builder = services.GetRequiredService<HioposResultBuilder>();

                // ─────────────────────────────────────────────────────────────────
                // EN UNA TRANSACTION, RESULT_CANCELED NO SIRVE
                // ─────────────────────────────────────────────────────────────────
                // Aca se devolvia BuildCanceled para CUALQUIER accion, y eso es
                // justo lo que HioPos muestra como "Error en modulo externo": una
                // Activity que vuelve con RESULT_CANCELED y sin extras es, para el
                // POS, indistinguible de un modulo que se murio.
                //
                // En una TRANSACTION el cajero esta esperando saber que paso con una
                // venta, asi que se le responde FAILED con un mensaje leible. En el
                // resto de las acciones del contrato (GET_VERSION, READ_CARD...) no
                // hay cajero mirando y RESULT_CANCELED sigue siendo lo correcto.
                var respuesta =
                    string.Equals(action, HioposActions.Transaction, StringComparison.Ordinal)
                        ? builder.BuildUnexpectedFailure(
                            intent.GetStringExtra(HioposExtras.TransactionType))
                        : builder.BuildCanceled(action);

                services.GetRequiredService<ITransactionResultHandler>()
                    .FinishWithResult(respuesta);
            }
            catch (Exception inner)
            {
                AppLogger.E("MainActivity", "Fallo tambien el finish seguro al POS.", inner);
            }
        }
    }

    // ------------------------------------------------------------------
    // Handlers por action
    // ------------------------------------------------------------------

    private static void HandleInitialize(Intent intent, IServiceProvider services)
    {
        // El Token ya se persistio en HandleIntent.
        // Parsear y persistir los Parameters de CloudLicense (API_BASE_URL,
        // SUBSCRIPTION_KEY, STORE_ID, ENVIRONMENT, OTP_DESTINATION, ...).
        var parameters = intent.GetStringExtra(HioposExtras.Parameters);
        if (!string.IsNullOrEmpty(parameters))
        {
            AppLogger.I("MainActivity", "Parameters XML recibido del Cloud; persistiendo.");
            var count = CloudConfigStore.SaveFromXml(parameters);

            // QA C-3: si el XML llego pero no se reconocio ningun <Param>, la app
            // se quedaria con la configuracion de appsettings (sandbox) sin que
            // nadie lo note. CloudConfigStore ya lo registra como ERROR; aca
            // ademas queda en la auditoria que va al POS.
            if (count == 0)
            {
                services.GetService<IAuditLogger>()?.Log(
                    "CONFIG_ERROR",
                    "El INITIALIZE trajo Parameters pero no se reconocio ningun parametro. " +
                    "La app opera con la configuracion embebida (sandbox).");
            }
        }

        // QA M-2: reconstruir la configuracion AHORA, con los parametros recien
        // persistidos. Antes ApiConfig era un singleton que se armaba la primera
        // vez que alguien lo resolvia, asi que la precedencia "CloudLicense gana"
        // solo se cumplia por casualidad de orden.
        var config = services.GetRequiredService<ApiConfigProvider>().Reload();

        // Mantenimiento de la BD local (QA M-13): purga en segundo plano para no
        // demorar el arranque del POS. Va por [Fire.AndForget] para que una
        // excepcion de la purga no pueda subir al SynchronizationContext y tumbar
        // la app durante un INITIALIZE.
        Fire.AndForget(() => PurgeLocalDataAsync(services), "MainActivity.Purga");

        AppLogger.I("MainActivity",
            $"INITIALIZE completado. env={config.Environment}, " +
            $"pinning={(config.CertificatePins.Count > 0 ? "ON" : "OFF")}, " +
            $"otp={SistecreditoService.DescribeOtpChannel(config.OtpDestination)}.");

        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildOk(HioposActions.Initialize));
    }

    /// <summary>
    /// Aviso al cajero cuando la pantalla de abonos no se pudo abrir. Best-effort:
    /// si tampoco se puede mostrar el dialogo, al menos queda en el log.
    /// </summary>
    private static async Task MostrarErrorDeAperturaAsync()
    {
        try
        {
            var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return;
            await page.DisplayAlertAsync(
                "No se pudo abrir Abonos",
                "Ocurrio un error al abrir la pantalla de abonos. Cerra y volve a abrir la " +
                "aplicacion; si persiste, avisa al area de sistemas.",
                "Entendido");
        }
        catch (Exception ex)
        {
            AppLogger.W("MainActivity", $"No se pudo mostrar el aviso de apertura: {ex.Message}");
        }
    }

    /// <summary>
    /// QA M-13: retencion de datos locales. Idempotencia y auditoria no tenian
    /// purga y crecian indefinidamente en un POS que opera anos.
    /// </summary>
    private static async Task PurgeLocalDataAsync(IServiceProvider services)
    {
        try
        {
            var retention = TimeSpan.FromDays(180);
            if (services.GetService<IIdempotencyStore>() is { } store)
                await store.PurgeOlderThanAsync(retention);
            if (services.GetService<IAuditLogCapture>() is { } audit)
                await audit.PurgeOlderThanAsync(retention);
        }
        catch (Exception ex)
        {
            AppLogger.W("MainActivity", $"Purga de datos locales fallo: {ex.Message}");
        }
    }

    private static void HandleFinalize(IServiceProvider services)
    {
        services.GetRequiredService<ITokenStore>().Clear();
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildOk(HioposActions.Finalize));
    }

    private static void HandleGetVersion(IServiceProvider services)
    {
        // B4: usa HioposExtras.Version (no TransactionResult).
        //
        // La version se toma SOLO de la constante, nunca de IAppInfo.VersionString.
        //
        // Antes se prefería IAppInfo.VersionString —el versionName del APK— y la
        // constante quedaba como fallback. Como el versionName sube en cada release,
        // HioPos veía una version distinta cada vez, creía que el modulo habia
        // cambiado y pedia "actualizar el modulo" en CADA arranque, sin poder
        // resolverlo nunca (el modulo es side-loaded: no hay APK que bajar). Se
        // observo en terminal al pasar de versionName 1.0.8 a 1.0.
        //
        // Lo que HioPos ve es un valor de CONTRATO acordado con ICG, no la version
        // del build. Ver [HioposActions.ModuleVersion].
        //
        // Al ser una constante ya no puede fallar ni venir vacía, que era el otro
        // riesgo: con Version=null HioPos hace version.isEmpty() -> NPE y muestra
        // "error en modulo externo".
        //
        // Y va como ENTERO: HioPos la lee con getIntExtra y descartaba la cadena.
        var version = HioposActions.ModuleVersion;
        AppLogger.I("MainActivity", $"GET_VERSION: se responde Version={version} (int).");

        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildVersion(HioposActions.GetVersion, version));
    }

    private static void HandleGetBehavior(IServiceProvider services)
    {
        // B3: 18 flags obligatorios (doc §3).
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildBehavior(HioposActions.GetBehavior));
    }

    private static void HandleGetCustomParams(IServiceProvider services)
    {
        // B6: Name + Logo PNG.
        //
        // El comentario que estaba aca decia "placeholder 1x1 PNG; en Fase 2 se carga
        // desde Resources/Raw". Era FALSO desde hace tiempo: se verifico que el logo
        // embebido es el KOAJ real, 320x320, byte por byte igual al PNG del icono del
        // launcher. Un comentario asi es peor que ninguno — hace desconfiar de algo
        // que funciona y manda a "arreglar" lo que ya esta hecho.
        var logo = SistecreditoTEF.Maui.Resources.Images.SistecreditoLogo.PngBytes;
        if (logo.Length == 0)
            AppLogger.W("MainActivity",
                "GET_CUSTOM_PARAMS sin logo: HioPos mostrara un icono generico.");

        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildCustomParams(HioposActions.GetCustomParams, logo));
    }

    private static void HandleGetPrintInfo(IServiceProvider services)
    {
        // B6: aunque CanPrint=false (MVP), el handler existe.
        var pkg = services.GetRequiredService<IAppInfo>().PackageName;
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildPrintInfo(HioposActions.GetPrintInfo, pkg, horizontalDots: 576));
    }

    private void HandleTransaction(Intent intent, IServiceProvider services)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ─────────────────────────────────────────────────────────────────────────
        // UN REFUND, DOS RESPUESTAS — SE RESUELVE ANTES DE TODO LO DEMAS
        // ─────────────────────────────────────────────────────────────────────────
        // HioPos manda el MISMO intent (TransactionType=REFUND) para dos cosas
        // distintas, y lo unico que las separa es el DocumentTypeId de la cabecera
        // del documento. Ver [RefundClassifier] para la tabla completa.
        //
        //   venta en curso (1,2)  -> el cajero quiere SOLTAR la linea de pago.
        //                            Se contesta ACCEPTED con la referencia y el
        //                            importe originales.
        //   abono (3,4)           -> es una NOTA DE CREDITO. Se rechaza con
        //                            FAILED y un mensaje visible.
        //
        // Va PRIMERO, antes de validar configuracion, limpiar estado o navegar:
        // ninguno de los dos caminos necesita backend ni pantalla, y cada paso
        // previo es una oportunidad de fallar y dejar al POS esperando.
        //
        // Los extras se leen DIRECTO del Intent y no del parser: los dos caminos
        // tienen que poder responder aunque el resto de los extras venga mal.
        var tipoRecibido = intent.GetStringExtra(HioposExtras.TransactionType);
        if (string.Equals(tipoRecibido, "REFUND", StringComparison.OrdinalIgnoreCase))
        {
            var documentoXml = intent.GetStringExtra(HioposExtras.DocumentData);
            var documentTypeId = RefundClassifier.LeerDocumentTypeId(documentoXml);
            var netAmount = RefundClassifier.LeerNetAmount(documentoXml);
            var clasificacion = RefundClassifier.Clasificar(documentoXml);

            var resultHandler = services.GetRequiredService<ITransactionResultHandler>();
            var builder = services.GetRequiredService<HioposResultBuilder>();
            var audit = services.GetService<IAuditLogger>();

            var traza = $"NetAmount={netAmount ?? "(ausente)"}, " +
                        $"DocumentTypeId={documentTypeId ?? "(ausente)"}, " +
                        $"documento={(string.IsNullOrEmpty(documentoXml) ? "ausente" : $"{documentoXml.Length} chars")}";

            // ─────────────────────────────────────────────────────────────────────
            // ANTE LA DUDA, NO
            // ─────────────────────────────────────────────────────────────────────
            // Nota de credito Y caso indeterminado se rechazan por igual.
            //
            // El indeterminado se ACEPTABA antes, para no trabar el desmarcado de la
            // linea. Ese "fallar abierto" es exactamente por donde se colo un abono
            // en produccion: llego con DocumentTypeId=28 —un valor que no figura en
            // ninguna lista del contrato de ICG—, no se reconocio, se trato como
            // venta y el abono se hizo.
            //
            // Entre trabar una papelera y regalar plata, se traba la papelera.
            if (clasificacion != TipoDeRefund.DesmarcarLineaDePago)
            {
                var motivo = clasificacion == TipoDeRefund.NotaDeCredito
                    ? "es un ABONO"
                    : "NO se pudo clasificar; ante la duda no se acepta";

                AppLogger.W("MainActivity",
                    $"REFUND rechazado: {motivo}. {traza}. " +
                    "Credinet no soporta reversos.");
                audit?.Log(AuditActions.RefundRejected, $"abono rechazado ({traza})");

                resultHandler.FinishWithResult(builder.BuildRefundNotSupported(tipoRecibido));
                return;
            }

            // Desmarcado de la linea de pago: NetAmount positivo, o sea una venta.
            var importe = intent.GetStringExtra(HioposExtras.Amount);
            var referencia = intent.GetStringExtra(HioposExtras.TransactionData);

            AppLogger.I("MainActivity",
                $"REFUND aceptado: venta en curso, se suelta la linea de pago. {traza}");

            // Se audita SIEMPRE, con referencia e importe. Este camino le dice al POS
            // que el abono quedo pagado sin que se haya movido un peso: el credito en
            // Credinet sigue vivo. Esta traza es el unico rastro para cuadrarlo.
            audit?.Log(AuditActions.PaymentLineReleased,
                $"linea de pago liberada SIN reverso en Credinet " +
                $"(DocumentTypeId={documentTypeId ?? "?"}, ref={referencia ?? "?"}, " +
                $"importe={importe ?? "?"})");

            resultHandler.FinishWithResult(
                builder.BuildPaymentLineRelease(tipoRecibido, importe, referencia));
            return;
        }

        // QA C-3: BARRERA DE AMBIENTE. Si la configuracion es incoherente
        // (ENVIRONMENT=production con la key publica de sandbox, o con la URL de
        // sandbox, o sin STORE_ID) se RECHAZA la transaccion con un mensaje claro
        // en vez de operar contra el ambiente equivocado.
        //
        // Este era el peor modo de fallo del modulo: una terminal de produccion
        // creando "creditos" en sandbox, con el cajero convencido de que eran
        // reales. Fallar ruidosamente es infinitamente preferible.
        var config = services.GetRequiredService<ApiConfigProvider>().Current;
        var problems = config.Validate();
        sw.Stop();
        AppLogger.I("MainActivity", $"HandleTransaction TIMING: config+validate={sw.ElapsedMilliseconds}ms");
        if (problems.Count > 0)
        {
            var detail = string.Join(" | ", problems);
            AppLogger.E("MainActivity", $"TRANSACTION rechazada por configuracion invalida: {detail}");
            services.GetService<IAuditLogger>()?.Log("CONFIG_ERROR", detail);

            services.GetRequiredService<ITransactionResultHandler>()
                .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                    .BuildTransactionFailed(
                        "El modulo Sistecredito no esta configurado correctamente en este POS. " +
                        "Avisa al area de sistemas antes de continuar (revisar parametros de " +
                        "CloudLicense).",
                        "Configuracion invalida"));
            return;
        }

        // BUG-FIX: limpiar el state al INICIO de cada transaccion real desde
        // HI-POS. Garantiza que datos residuales de una transaccion previa no
        // contaminen esta corrida.
        var stateStore = services.GetRequiredService<ITransactionStateStore>();
        stateStore.Clear();

        // HU8-973 (Fase 2): resetear el modo standalone al inicio de cada
        // TRANSACTION. Si el proceso quedo vivo entre un REFUND anterior y
        // esta SALE, IsRefundFromHioPos podia quedarse en true y
        // [ReciboPagoViewModel.FinalizarAsync] cerraria con la rama REFUND
        // (imprime local + setResult REFUND) en vez de la SALE (delega en
        // HioPos). Mismo riesgo para IsStandalone si veniamos de un launcher.
        // La rama REFUND, si corresponde, se setea abajo, despues del parseo
        // del TransactionType.
        services.GetRequiredService<IStandaloneModeTracker>().Reset();

        // HU8-973: marca la factura como VIVA. Se apaga en FinishWithResult
        // (AndroidTransactionResultHandler) al devolver el resultado a HioPos.
        // Mientras esté viva, el guard descarta el intent del launcher para no
        // pisar la venta que HioPos está esperando.
        stateStore.HioposTransactionActive = true;

        // Y se anota QUIEN la abrio. Lo lee [OnDestroy] para no limpiar el estado de
        // una operacion que ya es de otra instancia: el OnDestroy de la Activity que
        // se va corre DESPUES del OnResume de la que llega.
        _duenoDeLaOperacion = new WeakReference<MainActivity>(this);

        // BUG-FIX (no bloquear facturacion): el throttle de OTP es SINGLETON y
        // persiste entre transacciones. Si una factura agoto los reenvios, la
        // SIGUIENTE quedaba con "reenviar no disponible" y no se podia facturar.
        // Reseteamos por transaccion: cada una arranca con reenvios frescos.
        services.GetRequiredService<OtpRequestThrottle>().Reset();

        // 1) Parsear extras del Intent. Guard: si el Intent llega sin bundle,
        //    ExtractExtras puede devolver null -> Parse haria NRE. Usamos un
        //    diccionario vacio para que el parseo produzca un transaction
        //    "vacio" manejable en vez de crashear.
        var extras = ExtractExtras(intent) ?? new Dictionary<string, string?>();

        // DIAGNOSTICO: HioPos usa la MISMA accion para una venta y para un cobro
        // iniciado desde la terminal, asi que por la accion sola no se distinguen.
        // La diferencia viaja en los extras, pero no se logueaban: una captura en
        // terminal mostraba que el TRANSACTION habia llegado y nada sobre su
        // naturaleza, asi que no habia forma de saber que campo marca un abono sin
        // adivinar. [HioposExtrasDiagnostics] los describe con el PII enmascarado.
        AppLogger.I("MainActivity", HioposExtrasDiagnostics.Describe(extras));

        var transaction = services.GetRequiredService<HioposIntentParser>().Parse(extras);

        // HU-134 (Fase 3): cuando el cajero selecciona TEF desde "entrada de
        // caja" para hacer un recaudo (no una venta), el POS dispara el
        // Intent TRANSACTION pero:
        //   - Sin DocumentData (porque no hay factura abierta).
        //   - Con TransactionType=SALE (algunas versiones del POS lo mandan
        //     asi para el flujo de "entrada de caja").
        //   - Con AmountCents en "0" o un valor por defecto cualquiera.
        //
        // El unico indicador confiable de que es una VENTA es que venga el
        // documento (DocumentData o DocumentPath). Si NO viene documento,
        // es inequivocamente un RECAUDO: enrutamos a CreditosActivos aunque
        // el TransactionType venga como SALE. Si HAY documento, respetamos
        // el TransactionType que mande el POS.
        var hayDocumento = !string.IsNullOrEmpty(transaction.DocumentData)
                            || !string.IsNullOrEmpty(transaction.DocumentPath);
        // Es un recaudo si NO hay documento de venta. Confirmado en terminal
        // comparando las dos operaciones:
        //
        //   venta          TransactionType=SALE TenderType=CREDIT DocumentData=(11540 chars)
        //   entrada caja   TransactionType=SALE TenderType=(vacio) DocumentData ausente
        //
        // O sea que el POS manda SALE en los dos casos y la ausencia del documento
        // es lo unico que los distingue. IsAdvancedPayment y OverPaymentType valen
        // lo MISMO en ambos, asi que no sirven para decidir.
        //
        // Aca habia tambien un "|| transaction.IsRefund", de cuando se creia que el
        // POS marcaba las entradas de caja como REFUND. No es asi —las manda como
        // SALE sin documento, segun la captura de arriba— y ademas el REFUND ya no
        // llega hasta aca: es una NOTA DE CREDITO y se rechaza al entrar a
        // [HandleTransaction]. Dejarlo habria hecho que una nota de credito abriera
        // la lista de creditos del cliente.
        var esRecaudo = !hayDocumento;

        if (esRecaudo)
        {
            AppLogger.W("MainActivity",
                "TRANSACTION sin documento de venta: es un recaudo (entrada de caja).");
            var standalone = services.GetRequiredService<IStandaloneModeTracker>();
            standalone.IsStandalone = true;
            standalone.IsRefundFromHioPos = true;

            // OJO: NO se reescribe TransactionType.
            //
            // Antes se hacia `transaction with { TransactionType = "REFUND" }`, y eso
            // se propagaba a la RESPUESTA: HioPos preguntaba SALE y le contestabamos
            // REFUND. Capturado en terminal: tras recibir esa respuesta, HioPos
            // relanzaba otro TRANSACTION con un TransactionId nuevo (4000614 ->
            // 4000615) en vez de darla por cerrada, y el abono no quedaba registrado
            // como movimiento de caja.
            //
            // El tipo que viaja de vuelta tiene que ser el que el POS pidio. Que
            // internamente sea un recaudo se lleva en [esRecaudo] y en
            // IsRefundFromHioPos, no alterando el contrato.
        }

        // DIAGNOSTICO (Fase 2): en logcat queda inequivoco el TransactionType
        // y el destino de la navegacion, para distinguir SALE vs REFUND sin
        // tener que adivinar. El TransactionType es un enumerado de contrato
        // (SALE / REFUND / NEGATIVE_SALE / etc.), sin PII.
        AppLogger.I("MainActivity",
            $"TRANSACTION: TransactionType='{transaction.TransactionType ?? "(vacio)"}', " +
            $"hayDocumento={hayDocumento}, esRecaudo={esRecaudo}, navegando a " +
            (esRecaudo ? "CreditosActivos (recaudo)" : "CapturaCedula (credito nuevo)"));

        // HU8-973 BugFix: cuando HioPos dispara un REFUND no estamos ante una
        // venta, sino ante un recaudo (pago de una cuota de un credito ya
        // existente). El cajero NO debe pasar por el flujo de crear un credito
        // nuevo (CapturaCedula -> Validacion -> Cuotas -> OTP -> Confirmacion);
        // debe ir directo al flujo de pagar credito (CreditosActivos).
        //
        // El Intent se sigue procesando como TRANSACTION y HioPos ESPERA un
        // setResult al terminar (con TransactionType=REFUND), igual que en SALE.
        // Por eso HioposTransactionActive queda en true (seteado arriba) y se
        // devuelve el control al POS al finalizar el pago.
        //
        // El comprobante del recaudo se le devuelve a HioPos en MerchantReceipt /
        // CustomerReceipt para que lo imprima EL POS, igual que en una venta. Esa es
        // la impresora que funciona: la termica de este terminal no aparece en el
        // bus USB, asi que el camino local no puede alcanzarla.
        //
        // Se marca IsRefundFromHioPos=true para distinguir del launcher (que cierra
        // con FinishAffinity sin devolver nada); [ReciboPagoViewModel] lo consulta al
        // finalizar.
        if (esRecaudo)
        {
            var standalone = services.GetRequiredService<IStandaloneModeTracker>();
            standalone.IsStandalone = true;
            standalone.IsRefundFromHioPos = true;
        }

        // 2) Leer el documento de venta. Con OnlyUseDocumentPath=false, HioPos
        //    lo manda INLINE en DocumentData (sin permisos de fichero, que en
        //    Android 13+ fallan por scoped storage). Si viniera por ruta
        //    (documentos >1MB), caemos al IDocumentReader por path.
        //    Doc §12 gotcha #10: leerlo ANTES de finish().
        SistecreditoTEF.Maui.Models.SaleDocument? doc = null;
        try
        {
            if (!string.IsNullOrEmpty(transaction.DocumentData))
            {
                doc = XmlDocumentReader.Parse(transaction.DocumentData);
                AppLogger.I("MainActivity",
                    $"Documento leido inline (DocumentData): {(doc is null ? "null" : "ok")}.");
            }
            else if (!string.IsNullOrEmpty(transaction.DocumentPath))
            {
                doc = services.GetRequiredService<IDocumentReader>()
                    .Read(transaction.DocumentPath);
            }
        }
        catch (Exception ex)
        {
            // QA M-17: [XmlDocumentReader.Parse] LANZA ante XML malformado (a
            // diferencia de Read, que captura y devuelve null). Sin este catch la
            // excepcion subia al handler de HandleIntent y CANCELABA la venta
            // entera. El documento solo aporta conveniencia (autocompletar la
            // cedula y el SaleId), asi que lo correcto es continuar sin el.
            AppLogger.E("MainActivity",
                "El documento de venta no se pudo parsear; se continua sin autocompletar.", ex);
            doc = null;
        }

        if (doc is not null)
        {
            services.GetRequiredService<ITransactionStateStore>()
                .SetActiveDocument(doc);

            // ─────────────────────────────────────────────────────────────────
            // DIAGNOSTICO: LOS IDs REALES DE LOS MEDIOS DE PAGO DE ESTA TIENDA
            // ─────────────────────────────────────────────────────────────────
            // Hace falta para el recaudo. En una entrada de caja se manda
            // FixedPaymentMeanId con el id del medio EFECTIVO, y ese id lo define
            // HioPosCloud por tienda: si no coincide, HioPos descarta el campo
            // entero y la entrada queda con el importe que escribio el cajero.
            //
            // El id no se puede consultar: la BD de HioPos es privada. Pero el
            // documento de una VENTA si trae los medios con su id y su nombre,
            // asi que una venta cobrada con cualquier medio los revela.
            if (doc.PaymentMeans.Count > 0)
            {
                var medios = string.Join(" | ", doc.PaymentMeans.Select(pm =>
                    $"id={pm.PaymentMeanId ?? "?"} " +
                    $"nombre='{SistecreditoTEF.Maui.Models.DocumentFieldCollectionExtensions.GetValue(pm.Fields, "PaymentMeanName") ?? "?"}' " +
                    $"desc='{SistecreditoTEF.Maui.Models.DocumentFieldCollectionExtensions.GetValue(pm.Fields, "Description") ?? ""}' " +
                    $"importe={pm.Amount}"));
                AppLogger.I("MainActivity", $"MEDIOS DE PAGO del documento: {medios}");
            }
            else
            {
                AppLogger.I("MainActivity",
                    "El documento no trae medios de pago todavia (se agregan despues de que respondemos).");
            }
        }

        // 3) Persistir el HioposTransaction para que las Pages lo lean.
        services.GetRequiredService<ITransactionStateStore>()
            .SetActiveTransaction(transaction);

        // 4) Aterrizar en la pantalla de INICIO (selector "Comprar a credito" /
        //    "Pagar credito"). HioPos lanza el TEF para compras y recaudos; el
        //    cajero elige aqui.
        //
        //    CRITICO: en COLD START el Shell YA arranca en Home por si mismo.
        //    Si ademas navegamos a //home aqui, esa navegacion CARREA con la
        //    navegacion inicial del Shell y deja el ruteo roto -> despues los
        //    botones de Home "no hacen nada" (el push relativo falla en
        //    silencio). Por eso SOLO reseteamos a Home si venimos de un
        //    re-lanzamiento CALIENTE que quedo en una pantalla profunda.
        //    state.Clear() (arriba) ya limpio los datos; el Finish lo hacen
        //    ConfirmacionPage / ReciboPagoPage via ITransactionResultHandler.
        if (Shell.Current is null) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (esRecaudo)
                    await IrARecaudoAsync();
                else
                    await IrACapturaDeClienteAsync();
            }
            catch (Exception ex)
            {
                // FAIL-SAFE: esta lambda es async void para el dispatcher, asi
                // que una excepcion aqui NO la atrapa el try/catch de
                // HandleIntent: subiria al SynchronizationContext y mataria el
                // proceso. La venta ya esta en curso en HioPos, asi que ante un
                // fallo de navegacion el cajero se queda en la pantalla actual
                // en vez de perder la app.
                AppLogger.E("MainActivity",
                    "Error navegando al iniciar la TRANSACTION.", ex);
            }
        });
    }

    /// <summary>
    /// Al iniciar una TRANSACTION desde HioPos se va DIRECTO a la pantalla de
    /// consulta de cliente, con la cedula autocompletada desde el documento de
    /// venta.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE NO SE ATERRIZA EN EL MENU
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes la TRANSACTION dejaba al cajero en Home, con el selector "Comprar a
    /// credito / Pagar credito". Pero si HioPos ya abrio una venta, el paso
    /// siguiente no es ambiguo: es cobrar esa venta a credito. Obligar a elegir
    /// agrega un toque innecesario en cada factura y permite entrar por error al
    /// flujo de abonos con una venta abierta.
    ///
    /// El menu NO desaparece: se llega a la captura con un PUSH sobre Home, asi
    /// que el boton "atras" lo devuelve. El recaudo standalone sigue disponible
    /// por el icono del launcher.
    ///
    /// Sobre el orden de navegacion: en COLD START el Shell ya arranca en Home por
    /// si mismo, y encadenar una navegacion absoluta ahi carrea con la inicial y
    /// deja el ruteo roto (los botones dejan de responder). Por eso solo se resetea
    /// a Home cuando venimos de un relanzamiento CALIENTE que quedo en una pantalla
    /// profunda; desde Home se hace directamente el push.
    /// </summary>
    private static async Task IrACapturaDeClienteAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var shell = Shell.Current;
        if (shell is null) return;

        if (!EstaEnLaBase(shell, out var loc))
        {
            AppLogger.I("MainActivity",
                $"Relanzamiento caliente desde '{loc}': se resetea a la base antes de la captura.");
            await shell.GoToAsync(AppRoutes.Home);
        }

        if (EstaEnCapturaDeCedula(shell))
        {
            AppLogger.I("MainActivity", "Ya se esta en la captura de cliente; no se navega de nuevo.");
            return;
        }

        var swNav = System.Diagnostics.Stopwatch.StartNew();
        await shell.GoToAsync(AppRoutes.CapturaCedula);
        swNav.Stop();
        sw.Stop();
        AppLogger.I("MainActivity",
            $"TRANSACTION: navegado a la captura de cliente. " +
            $"NAV={swNav.ElapsedMilliseconds}ms TOTAL={sw.ElapsedMilliseconds}ms");
    }

    private static bool EstaEnCapturaDeCedula(Shell shell)
    {
        var loc = shell.CurrentState?.Location?.OriginalString ?? string.Empty;
        return loc.Contains(AppRoutes.CapturaCedula, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ¿El Shell esta en su ubicacion BASE, o sea que se puede hacer el push directo
    /// sin resetear antes?
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE ESTO ERA UN COSTO EN CADA ARRANQUE
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Antes se comparaba contra la cadena fija "home". El comentario decia "en cold
    /// start el Shell ya arranca en Home", y era cierto cuando se escribio — pero la
    /// raiz paso a ser la pantalla de marca (splash) y la comparacion quedo atras.
    ///
    /// Resultado, medido en terminal en un arranque EN FRIO:
    ///
    ///   Relanzamiento caliente desde 'splash': se resetea a Home antes de la captura
    ///
    /// O sea que en cada venta se hacia un GoToAsync a la pantalla donde YA estabamos,
    /// y despues el push. Dos navegaciones del Shell donde va una.
    ///
    /// Ahora la raiz se DERIVA de [AppRoutes.Home] en vez de escribirse fija, para que
    /// el dia que la raiz vuelva a cambiar esta comprobacion la siga sola.
    /// </summary>
    private static bool EstaEnLaBase(Shell shell, out string loc)
    {
        loc = shell.CurrentState?.Location?.OriginalString?.Trim('/') ?? string.Empty;

        if (loc.Length == 0) return true;

        var raiz = AppRoutes.Home.Trim('/');

        return string.Equals(loc, raiz, StringComparison.OrdinalIgnoreCase)
            || string.Equals(loc, "home", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HU8-973 (Fase 2): cuando llega un REFUND desde HioPos no hay venta
    /// abierta en el POS, asi que el cajero va directo al flujo de pagar
    /// credito (lista de creditos activos -> pago -> comprobante -> setResult).
    ///
    /// Misma regla de navegacion que [IrACapturaDeClienteAsync]: encadenar una
    /// navegacion absoluta sobre la inicial del Shell carrea y deja el ruteo roto.
    /// Por eso solo se resetea cuando venimos de un relanzamiento caliente que quedo
    /// en una pantalla profunda; estando en la base se hace directamente el push.
    /// Ver [EstaEnLaBase] para por que eso se derivaba mal.
    ///
    /// El boton "atras" desde CreditosActivos lo devuelve al menu (igual
    /// que desde CapturaCedula), por lo que el cajero puede salir del
    /// flujo de recaudo si lo necesita.
    /// </summary>
    private static async Task IrARecaudoAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var shell = Shell.Current;
        if (shell is null) return;

        if (!EstaEnLaBase(shell, out var loc))
        {
            AppLogger.I("MainActivity",
                $"Relanzamiento caliente desde '{loc}': se resetea a la base antes del recaudo.");
            await shell.GoToAsync(AppRoutes.Home);
        }

        if (EstaEnCreditosActivos(shell))
        {
            AppLogger.I("MainActivity", "Ya se esta en creditos activos; no se navega de nuevo.");
            return;
        }

        var swNav = System.Diagnostics.Stopwatch.StartNew();
        await shell.GoToAsync(AppRoutes.CreditosActivos);
        swNav.Stop();
        sw.Stop();
        AppLogger.I("MainActivity",
            // Decia "(REFUND)", de cuando se creia que las entradas de caja llegaban
            // con ese TransactionType. Llegan como SALE sin documento, y el REFUND
            // hoy se rechaza al entrar (es una nota de credito): la etiqueta vieja
            // apuntaba al camino equivocado al leer el log.
            $"TRANSACTION (recaudo): navegado a creditos activos. " +
            $"NAV={swNav.ElapsedMilliseconds}ms TOTAL={sw.ElapsedMilliseconds}ms");
    }

    private static bool EstaEnCreditosActivos(Shell shell)
    {
        var loc = shell.CurrentState?.Location?.OriginalString ?? string.Empty;
        return loc.Contains(AppRoutes.CreditosActivos, StringComparison.OrdinalIgnoreCase);
    }

    private static void HandleShowSetupScreen(IServiceProvider services)
    {
        // Doc §2: "Botón Configurar en Adm > Módulos Externos".
        // MVP: la pantalla de setup no esta implementada todavia;
        // devolvemos OK vacio y finish para no romper HioPosCloud.
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildOk(HioposActions.ShowSetupScreen));
    }

    /// <summary>
    /// VOID_TRANSACTION: se rechaza siempre, con el mismo mensaje que la nota de
    /// credito.
    ///
    /// No se mira el documento ni ninguna bandera. Esta accion llega cuando el POS
    /// decidio mandar un abono por aca en lugar de por REFUND —lo que ocurre con
    /// <c>ExecuteVoidWhenAvailable=true</c>— y ya se colaron cuatro abonos por esa
    /// puerta, respondidos ACCEPTED en silencio.
    ///
    /// Nuestra bandera esta en false, pero un cambio de configuracion del POS la
    /// abre. Rechazar sin condiciones es lo unico que no depende de eso.
    /// </summary>
    private static void HandleVoidTransaction(Intent intent, IServiceProvider services)
    {
        var tipo = intent.GetStringExtra(HioposExtras.TransactionType);

        services.GetService<IAuditLogger>()?.Log(
            AuditActions.RefundRejected,
            $"VOID_TRANSACTION rechazado (TransactionType={tipo ?? "(ausente)"})");

        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildRefundNotSupported(tipo));
    }

    private static void HandleUnsupported(IServiceProvider services, string action)
    {
        AppLogger.W("MainActivity", $"Action no soportada: {action}");
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildCanceled(action));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Extrae los extras del Intent de Android como string dictionary.
    /// Aísla la dependencia de Android.OS.Bundle aqui.
    /// </summary>
    private static Dictionary<string, string?>? ExtractExtras(Intent intent)
    {
        var bundle = intent.Extras;
        if (bundle is null) return null;

        // QA B-1 (CS8602): KeySet() puede devolver null en un Bundle vacio.
        var keys = bundle.KeySet();
        if (keys is null) return null;

        var result = new Dictionary<string, string?>(keys.Count);
        foreach (var key in keys)
        {
            if (key is null) continue;
            result[key] = bundle.Get(key)?.ToString();
        }
        return result;
    }
}
