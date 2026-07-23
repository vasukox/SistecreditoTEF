using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui;

/// <summary>
/// Bootstrap del APK ABONOS (<c>com.permoda.sistecreditotef.abonos</c>):
/// launcher standalone, NUNCA responde a HI-POS.
///
/// El registro de dependencias COMÚN a las dos apps vive en
/// <see cref="AppServicesRegistration"/> (DRY). Aquí solo queda lo específico
/// de Abonos: handlers no-op (no hay POS que reciba el resultado) y el modo
/// standalone forzado a <c>true</c>.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Android.Util.Log.Info("MauiProgram", "CreateMauiApp START (Abonos)");

        // HU8-973: inicializa SQLCipher antes de cualquier uso de SQLite.
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();

#if ANDROID
        // HU8-973 (UI/UX): quitar el subrayado nativo del Entry en Android.
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

        // ---- Servicios COMPARTIDOS con el APK TEF (DRY) ----
        builder.Services.AddSistecreditoSharedServices();

        // ---- Específico de ABONOS: standalone SIEMPRE, NUNCA HI-POS ----
        // Sin AndroidTransactionResultHandler ni BroadcastAuditLogger: este APK
        // no le devuelve resultado a ningún POS. Finalizar() cierra la Activity.
        builder.Services.AddSingleton<ITransactionResultHandler, NoOpTransactionResultHandler>();
        builder.Services.AddSingleton<IAuditLogger, NoOpAuditLogger>();
        builder.Services.AddSingleton<IStandaloneModeTracker>(_ =>
            new StandaloneModeTracker { IsStandalone = true });

        var app = builder.Build();
        AppLogger.Init(app.Services.GetRequiredService<ILogger<SistecreditoApp>>());
        return app;
    }
}

/// <summary>Marker type para ILogger&lt;T&gt; (categoría del logger estático).</summary>
public sealed class SistecreditoApp;

internal sealed class NoOpTransactionResultHandler : ITransactionResultHandler
{
    public void FinishWithResult(HioposResponse response) { /* no-op */ }
}

internal sealed class NoOpAuditLogger(IAuditLogCapture capture) : IAuditLogger
{
    public void Log(string action, string comment, string? documentId = null)
        => capture.Append(action, comment, documentId);
}
