using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// Entry point de la app en Android.
///
/// HioPosCloud lanza Intents con action
/// "icg.actions.electronicpayment.sistecredito.XXX" (ver [HioposConstants.ApkName]).
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
        HioposActions.GetCardData
    },
    Categories = new[] { Intent.CategoryDefault })]
[IntentFilter(
    actions: new[] { "android.intent.action.MAIN" },
    Categories = new[] { "android.intent.category.LAUNCHER" })]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
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
        if (intent is not null)
        {
            // Reemplaza Intent (propiedad heredada de Activity)
            // para que MainActivity.Intent apunte al nuevo.
            Intent = intent;
            // Guardamos el nuevo intent; OnResume lo procesa.
            _pendingIntent = intent;
        }
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

        // LAUNCHER: el usuario abrio la app desde el icono del launcher.
        // NO procesamos como intent de HioPosCloud (no cerramos la app,
        // dejamos que MAUI muestre la UI normal - HomePage).
        if (action == "android.intent.action.MAIN")
        {
            Log.Info("MainActivity", "LAUNCHER action: app abierta desde icono. No se procesa como intent HioPos.");
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
                $"Handler de '{action}' lanzo excepcion; devolviendo Canceled al POS para no crashear.", ex);
            try
            {
                services.GetRequiredService<ITransactionResultHandler>()
                    .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                        .BuildCanceled(action));
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
        // B9: ya seteamos Token en HandleIntent.
        // HU8-973 (Opción A): parsear y persistir los Parameters de CloudLicense
        // (API_BASE_URL, SUBSCRIPTION_KEY, STORE_ID, ENVIRONMENT, OTP_DESTINATION).
        // [ApiConfig] los superpone sobre appsettings.json en el próximo uso.
        var parameters = intent.GetStringExtra(HioposExtras.Parameters);
        if (!string.IsNullOrEmpty(parameters))
        {
            AppLogger.I("MainActivity", "Parameters XML recibido del Cloud; persistiendo.");
            SistecreditoTEF.Maui.Services.Platform.CloudConfigStore.SaveFromXml(parameters);
        }

        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildOk(HioposActions.Initialize));
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
        // Robustez: si esto lanzara (IAppInfo no resoluble o VersionString
        // fallando), HioPos recibe Version=null y hace version.isEmpty() ->
        // NPE ("error en modulo externo"). Por eso NUNCA dejamos que falle y
        // garantizamos una version no vacia.
        // Fallback determinista = versionName del APK (HioposActions.ModuleVersion).
        // NUNCA usar un literal distinto ("1.0") o HioPos vera version cambiante.
        var version = HioposActions.ModuleVersion;
        try
        {
            var info = services.GetService<IAppInfo>();
            if (info is not null && !string.IsNullOrEmpty(info.VersionString))
                version = info.VersionString;
        }
        catch (Exception ex)
        {
            AppLogger.E("MainActivity",
                "No se pudo obtener la version de la app; se usa fallback.", ex);
        }

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
        // B6: Name + Logo PNG. MVP: placeholder 1x1 PNG; en Fase 2 se
        // carga desde Resources/Raw o Assets.
        var placeholderLogo = SistecreditoTEF.Maui.Resources.Images.SistecreditoLogo.PngBytes;
        services.GetRequiredService<ITransactionResultHandler>()
            .FinishWithResult(services.GetRequiredService<HioposResultBuilder>()
                .BuildCustomParams(HioposActions.GetCustomParams, placeholderLogo));
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
        // BUG-FIX: limpiar el state al INICIO de cada transaccion real desde
        // HI-POS. Garantiza que datos residuales de una transaccion previa no
        // contaminen esta corrida.
        services.GetRequiredService<ITransactionStateStore>().Clear();

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
        var transaction = services.GetRequiredService<HioposIntentParser>().Parse(extras);

        // 2) Leer el documento de venta. Con OnlyUseDocumentPath=false, HioPos
        //    lo manda INLINE en DocumentData (sin permisos de fichero, que en
        //    Android 13+ fallan por scoped storage). Si viniera por ruta
        //    (documentos >1MB), caemos al IDocumentReader por path.
        //    Doc §12 gotcha #10: leerlo ANTES de finish().
        SistecreditoTEF.Maui.Models.SaleDocument? doc = null;
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

        if (doc is not null)
            services.GetRequiredService<ITransactionStateStore>()
                .SetActiveDocument(doc);

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
        if (Shell.Current is not null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var loc = Shell.Current.CurrentState?.Location?.OriginalString?.Trim('/')
                              ?? string.Empty;
                    if (loc.Length > 0 && !string.Equals(loc, "home", StringComparison.OrdinalIgnoreCase))
                        await Shell.Current.GoToAsync(AppRoutes.Home);
                }
                catch (Exception ex)
                {
                    AppLogger.E("MainActivity", "Error reseteando a Home", ex);
                }
            });
        }
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

        var keys = bundle.KeySet();
        var result = new Dictionary<string, string?>(keys.Count);
        foreach (var key in keys)
        {
            result[key] = bundle.Get(key)?.ToString();
        }
        return result;
    }
}
