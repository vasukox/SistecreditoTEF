using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

#pragma warning disable SA1633 // File should have header

namespace SistecreditoTEF.Maui;

/// <summary>
/// Bootstrap del APK TEF (<c>com.pos2pay</c>): integra con HI-POS Cloud y,
/// abierto desde el launcher, también hace abonos standalone.
///
/// El registro de dependencias COMÚN a las dos apps vive en
/// <see cref="AppServicesRegistration"/> (DRY). Aquí solo queda lo específico
/// del TEF: los handlers que responden a HI-POS.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Android.Util.Log.Info("MauiProgram", "CreateMauiApp START (TEF)");

        // HU8-973: inicializa el proveedor SQLCipher (bundle_e_sqlcipher) antes
        // de cualquier uso de SQLite, para que la BD local vaya cifrada.
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();

#if ANDROID
        // HU8-973 (UI/UX): quitar el subrayado nativo del Entry en Android para
        // el look "filled" limpio (los Entry van dentro de un Border estilizado).
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderlineHU8973", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        });
#endif

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { });

        builder.Configuration.LoadAppSettingsFromAsset();

#if DEBUG
        builder.Configuration.AddUserSecrets<MauiApp>();
        builder.Logging.AddDebug();
#endif

        // ---- Servicios COMPARTIDOS con la app de Abonos (DRY) ----
        // Pipeline HTTP, stores, dominio, ViewModels y Pages viven en
        // AppServicesRegistration para no duplicar ni desincronizar con el otro APK.
        builder.Services.AddSistecreditoSharedServices();

        // ---- Específico del TEF (com.pos2pay): SÍ responde a HI-POS ----
#if ANDROID
        builder.Services.AddSingleton<ITransactionResultHandler, Platforms.Android.AndroidTransactionResultHandler>();
        builder.Services.AddSingleton<IAuditLogger, Platforms.Android.BroadcastAuditLogger>();
#else
        builder.Services.AddSingleton<ITransactionResultHandler, NoOpTransactionResultHandler>();
        builder.Services.AddSingleton<IAuditLogger, NoOpAuditLogger>();
#endif
        // Standalone arranca en false; MainActivity lo pone true si el cajero
        // abre por el ícono del launcher (abonos sin venta HI-POS abierta).
        builder.Services.AddSingleton<IStandaloneModeTracker, StandaloneModeTracker>();

        var app = builder.Build();
        // Se loguea que el contenedor quedo armado, sin comparar contra null:
        // builder.Build() nunca devuelve null, y esa comparacion le decia al
        // compilador que si podia, con lo que el acceso a app.Services de la linea
        // siguiente quedaba marcado como posible desreferencia nula (CS8602). El
        // aviso era falso, pero tapaba los avisos reales.
        Android.Util.Log.Info("MauiProgram", "Build SUCCESS: contenedor de servicios listo.");

        AppLogger.Init(app.Services.GetRequiredService<ILogger<SistecreditoApp>>());

#if ANDROID
        // ─────────────────────────────────────────────────────────────────────
        // RED DE SEGURIDAD: lo PRIMERO despues de que existe el log
        // ─────────────────────────────────────────────────────────────────────
        // Va aca y no antes porque necesita el logger y el contenedor; y va antes
        // de devolver la app porque a partir de este punto cualquier excepcion no
        // atrapada —incluidas las que lanza el propio contenedor al liberar
        // servicios, que ya tumbaron una caja en produccion— queda registrada, no
        // cierra la app y, si habia una venta viva, se le responde al POS.
        Platforms.Android.CrashGuard.Instalar(app.Services);

        // La llave de suscripcion se lee de SecureStorage de forma SINCRONA la
        // primera vez que se arma una peticion HTTP. Se precalienta aca para que
        // ese camino encuentre la cache llena y nunca bloquee un hilo esperando al
        // Keystore. Es mejor esfuerzo: si falla, el camino sincronico sigue ahi.
        Fire.AndForget(CloudConfigStore.PrecalentarAsync, "CloudConfigStore");
#endif

        return app;
    }
}

/// <summary>
/// Marker type para ILogger&lt;T&gt;. No se usa para nada mas que
/// como categoria del logger estatica.
/// </summary>
public sealed class SistecreditoApp;

/// <summary>
/// Fallback para plataformas no-Android (tests, iOS si se agrega).
/// </summary>
internal sealed class NoOpTransactionResultHandler : ITransactionResultHandler
{
    public void FinishWithResult(HioposResponse response) { /* no-op */ }
}

internal sealed class NoOpAuditLogger(IAuditLogCapture capture) : IAuditLogger
{
    public void Log(string action, string comment, string? documentId = null)
        => capture.Append(action, comment, documentId);
}
