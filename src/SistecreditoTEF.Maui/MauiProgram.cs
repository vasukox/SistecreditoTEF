using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;
using SistecreditoTEF.Maui.ViewModels;

#pragma warning disable SA1633 // File should have header

namespace SistecreditoTEF.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Android.Util.Log.Info("MauiProgram", "CreateMauiApp START");

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

        // Configuracion: AddJsonFile("appsettings.json", ...) NO funciona en
        // Android (no hay acceso a la carpeta de output del build). Lo cargamos
        // via MauiAsset DESPUES de UseMauiApp<App>() (asi el IFileSystem de
        // MAUI ya esta registrado y FileSystem.OpenAppPackageFileAsync funciona).
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { });

        LoadAppSettingsFromAsset(builder.Configuration);

#if DEBUG
        builder.Configuration.AddUserSecrets<MauiApp>();
#endif

        Android.Util.Log.Info("MauiProgram", "After UseMauiApp<App>");

#if DEBUG
        builder.Logging.AddDebug();
#endif

        Android.Util.Log.Info("MauiProgram", "Configure services starting...");

        // ---- Config singleton ----
        builder.Services.AddSingleton(sp =>
            ApiConfig.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

        // ---- Logger estatico (O8 fix): lo resolvemos via factory despues del build ----
        //        (se inicializa en MauiProgramExtensions.InitAppLogger post-build)

        // ---- HTTP pipeline ----
        builder.Services.AddSingleton<AuthInterceptor>();
        builder.Services.AddSingleton<HttpLoggingHandler>();
        builder.Services.AddSingleton<IRequestLogCapture, InMemoryRequestLogCapture>();
        builder.Services.AddHttpClient<ICredinetApi, CredinetApiClient>()
            .AddHttpMessageHandler<AuthInterceptor>()
            .AddHttpMessageHandler<HttpLoggingHandler>()
            // HU8-973: reintentos solo para GET idempotentes (excluye getCreditToken
            // y todo POST). No afecta peticiones exitosas. Ver CredinetHttpPolicies.
            .AddPolicyHandler((_, request) => CredinetHttpPolicies.Select(request))
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                                 | System.Net.DecompressionMethods.Deflate
                };
                // HU8-973: certificate pinning. Solo se activa si hay pines
                // configurados; vacío = validación TLS estándar (no rompe nada).
                var cfg = sp.GetRequiredService<ApiConfig>();
                if (cfg.CertificatePins.Count > 0)
                {
                    handler.ServerCertificateCustomValidationCallback =
                        (_, cert, chain, errors) =>
                            CertificatePinning.Validate(cert, chain, errors, cfg.CertificatePins);
                }
                return handler;
            })
            .ConfigureHttpClient((sp, client) =>
            {
                // HU8-973: timeout configurable por Credinet:TimeoutSeconds.
                var cfg = sp.GetRequiredService<ApiConfig>();
                client.Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds);
                client.DefaultRequestHeaders.AcceptEncoding.Add(
                    new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
            });

        // ---- CREDINET ----
        builder.Services.AddSingleton<ICredinetRepository, CredinetRepository>();

        // ---- Servicios de plataforma (single source of truth) ----
#if ANDROID
        builder.Services.AddSingleton<ITransactionResultHandler, Platforms.Android.AndroidTransactionResultHandler>();
        builder.Services.AddSingleton<IAuditLogger, Platforms.Android.BroadcastAuditLogger>();
#else
        builder.Services.AddSingleton<ITransactionResultHandler, NoOpTransactionResultHandler>();
        builder.Services.AddSingleton<IAuditLogger, NoOpAuditLogger>();
#endif
        builder.Services.AddSingleton<ITokenStore, PreferencesTokenStore>();
        builder.Services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();
        builder.Services.AddSingleton<IDocumentReader, XmlDocumentReader>();
        builder.Services.AddSingleton<ITransactionStateStore, TransactionStateStore>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IAuditLogCapture, SqliteAuditLog>();

        // ---- HioPosCloud ----
        builder.Services.AddSingleton<HioposIntentParser>();
        builder.Services.AddSingleton<HioposResultBuilder>();
        builder.Services.AddSingleton<ReceiptBuilder>();
        builder.Services.AddSingleton<ModifyDocumentResultBuilder>();

        // ---- Throttle de OTP (HU8-973: anti-spam de reenvios) ----
        builder.Services.AddSingleton<OtpRequestThrottle>(sp =>
        {
            var cfg = sp.GetRequiredService<ApiConfig>();
            return new OtpRequestThrottle(
                TimeSpan.FromSeconds(cfg.OtpResendCooldownSeconds),
                cfg.OtpMaxResends);
        });

        // ---- Dominio ----
        builder.Services.AddSingleton<SistecreditoService>();

        // ---- ViewModels (transient: uno por navegacion) ----
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CapturaCedulaViewModel>();
        builder.Services.AddTransient<ValidacionClienteViewModel>();
        builder.Services.AddTransient<SeleccionCuotasViewModel>();
        builder.Services.AddTransient<OtpViewModel>();
        builder.Services.AddTransient<ConfirmacionViewModel>();
        builder.Services.AddTransient<CreditosActivosViewModel>();
        builder.Services.AddTransient<PagoViewModel>();
        builder.Services.AddTransient<ReciboPagoViewModel>();

        // ---- Pages ----
        builder.Services.AddTransient<Views.HomePage>();
        builder.Services.AddTransient<Views.CapturaCedulaPage>();
        builder.Services.AddTransient<Views.ValidacionClientePage>();
        builder.Services.AddTransient<Views.SeleccionCuotasPage>();
        builder.Services.AddTransient<Views.OtpPage>();
        builder.Services.AddTransient<Views.ConfirmacionPage>();
        builder.Services.AddTransient<Views.CreditosActivosPage>();
        builder.Services.AddTransient<Views.PagoPage>();
        builder.Services.AddTransient<Views.ReciboPagoPage>();

        var app = builder.Build();
        Android.Util.Log.Info("MauiProgram", $"Build SUCCESS. App null={app == null}");

        AppLogger.Init(app.Services.GetRequiredService<ILogger<SistecreditoApp>>());
        return app;
    }

    /// <summary>
    /// Lee el appsettings.json empaquetado como EmbeddedResource en el
    /// assembly y lo agrega al IConfiguration como stream.
    ///
    /// Por que AddJsonFile NO funciona en Android:
    ///   - En Android, el AppContext.BaseDirectory es /data/user/0/{pkg}
    ///     y no contiene appsettings.json.
    ///
    /// Por que NO usamos MauiAsset (FileSystem.OpenAppPackageFileAsync):
    ///   - Falla con "Specified method is not supported" durante el
    ///     bootstrap de MAUI (antes de que MAUI termine de inicializar).
    ///   - El IFileSystem de MAUI todavia no esta registrado.
    ///
    /// Por que NO usamos Android.App.Application.Context.Assets.Open:
    ///   - Application.Context NO expone AssetManager en Mono/MAUI.
    ///   - Falla con "Specified method is not supported".
    ///
    /// Solucion: EmbeddedResource (csproj). El archivo se compila dentro
    /// del DLL como recurso embebido y se lee con
    /// Assembly.GetManifestResourceStream, que SI funciona en el bootstrap
    /// (no depende de Android Context, MAUI, ni de la Activity).
    /// </summary>
    private static void LoadAppSettingsFromAsset(IConfigurationBuilder config)
    {
        try
        {
            // Manifest name = {RootNamespace}.{filename} = SistecreditoTEF.Maui.appsettings.json
            var manifestName = $"{typeof(MauiProgram).Assembly.GetName().Name}.appsettings.json";
            using var stream = typeof(MauiProgram).Assembly
                .GetManifestResourceStream(manifestName);

            if (stream is null)
            {
                Android.Util.Log.Warn("MauiProgram",
                    $"Manifest resource no encontrado: {manifestName}");
                return;
            }

            var size = stream.Length;
            config.AddJsonStream(stream);
            Android.Util.Log.Info("MauiProgram",
                $"appsettings.json cargado desde EmbeddedResource (size={size} bytes)");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("MauiProgram",
                $"appsettings.json no encontrado o no se pudo cargar: {ex.Message}");
        }
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
