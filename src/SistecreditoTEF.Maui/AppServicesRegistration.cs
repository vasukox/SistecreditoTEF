using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui;

/// <summary>
/// HU8-973: registro de servicios COMPARTIDO por las dos apps
/// (TEF <c>com.pos2pay</c> y Abonos <c>com.permoda.sistecreditotef.abonos</c>).
///
/// Vive en el proyecto TEF y se compila también en Abonos vía el glob
/// <c>&lt;Compile Include="..\SistecreditoTEF.Maui\**\*.cs"&gt;</c> de su .csproj.
///
/// DRY/SOLID: un único lugar para el pipeline HTTP, stores, servicios de
/// dominio, ViewModels y Pages. Cada app registra en su propio MauiProgram
/// SOLO lo que la diferencia (result handler, audit logger y modo standalone).
/// Antes esto estaba duplicado en los dos MauiProgram y se desincronizaba
/// (fue lo que dejó al APK de Abonos sin el registro del printer).
/// </summary>
public static class AppServicesRegistration
{
    /// <summary>
    /// Registra todo lo COMÚN a las dos apps. Lo específico de cada app
    /// (<see cref="ITransactionResultHandler"/>, <see cref="IAuditLogger"/> y
    /// <see cref="IStandaloneModeTracker"/>) lo agrega cada MauiProgram.
    /// </summary>
    public static void AddSistecreditoSharedServices(this IServiceCollection services)
    {
        // ---- Config singleton (appsettings + CloudLicense) ----
        services.AddSingleton(sp =>
            ApiConfig.FromConfiguration(sp.GetRequiredService<IConfiguration>()));

        // ---- HTTP pipeline (auth + logging + resiliencia + pinning) ----
        services.AddSingleton<AuthInterceptor>();
        services.AddSingleton<HttpLoggingHandler>();
        services.AddSingleton<IRequestLogCapture, InMemoryRequestLogCapture>();
        services.AddHttpClient<ICredinetApi, CredinetApiClient>()
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
        services.AddSingleton<ICredinetRepository, CredinetRepository>();

        // ---- Stores / plataforma comunes ----
        services.AddSingleton<ITokenStore, PreferencesTokenStore>();
        services.AddSingleton<IIdempotencyStore, SqliteIdempotencyStore>();
        services.AddSingleton<IDocumentReader, XmlDocumentReader>();
        services.AddSingleton<ITransactionStateStore, TransactionStateStore>();
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<IAuditLogCapture, SqliteAuditLog>();

        // ---- HioPosCloud (ReceiptBuilder, parser, result builders) ----
        services.AddSingleton<HioposIntentParser>();
        services.AddSingleton<HioposResultBuilder>();
        services.AddSingleton<ReceiptBuilder>();
        services.AddSingleton<ModifyDocumentResultBuilder>();

        // ---- Throttle de OTP (HU8-973: anti-spam de reenvios) ----
        services.AddSingleton<OtpRequestThrottle>(sp =>
        {
            var cfg = sp.GetRequiredService<ApiConfig>();
            return new OtpRequestThrottle(
                TimeSpan.FromSeconds(cfg.OtpResendCooldownSeconds),
                cfg.OtpMaxResends);
        });

        // ---- Printer para recibos standalone (HU8-973) ----
        // Sunmi si el POS lo soporta; si no, PDF + share intent.
        services.AddSingleton<SunmiPrinter>();
        services.AddSingleton<PdfReceiptPrinter>();
        services.AddSingleton<IReceiptPrinter>(sp =>
        {
            var sunmi = sp.GetRequiredService<SunmiPrinter>();
            return sunmi.IsAvailable ? sunmi : sp.GetRequiredService<PdfReceiptPrinter>();
        });

        // ---- Dominio ----
        services.AddSingleton<SistecreditoService>();

        // ---- ViewModels (transient: uno por navegacion) ----
        services.AddTransient<HomeViewModel>();
        services.AddTransient<CapturaCedulaViewModel>();
        services.AddTransient<ValidacionClienteViewModel>();
        services.AddTransient<SeleccionCuotasViewModel>();
        services.AddTransient<OtpViewModel>();
        services.AddTransient<ConfirmacionViewModel>();
        services.AddTransient<CreditosActivosViewModel>();
        services.AddTransient<PagoViewModel>();
        services.AddTransient<ReciboPagoViewModel>();

        // ---- Pages ----
        services.AddTransient<Views.HomePage>();
        services.AddTransient<Views.CapturaCedulaPage>();
        services.AddTransient<Views.ValidacionClientePage>();
        services.AddTransient<Views.SeleccionCuotasPage>();
        services.AddTransient<Views.OtpPage>();
        services.AddTransient<Views.ConfirmacionPage>();
        services.AddTransient<Views.CreditosActivosPage>();
        services.AddTransient<Views.PagoPage>();
        services.AddTransient<Views.ReciboPagoPage>();
    }

    /// <summary>
    /// Carga el <c>appsettings.json</c> embebido como EmbeddedResource y lo
    /// agrega al IConfiguration como stream.
    ///
    /// Busca el recurso por SUFIJO (<c>*.appsettings.json</c>) en lugar de
    /// armar el nombre como <c>{AssemblyName}.appsettings.json</c>: el nombre
    /// del recurso lo determina el RootNamespace (igual en las dos apps:
    /// <c>SistecreditoTEF.Maui</c>), que NO coincide con el assembly name de
    /// Abonos (<c>SistecreditoTEF.Abonos</c>). Buscar por sufijo funciona en
    /// ambas apps sin depender de esa coincidencia.
    ///
    /// Por qué EmbeddedResource y no AddJsonFile/MauiAsset: durante el bootstrap
    /// de MAUI en Android el IFileSystem y el AssetManager aún no están
    /// disponibles y fallan con "Specified method is not supported".
    /// <see cref="Assembly.GetManifestResourceStream(string)"/> sí funciona.
    /// </summary>
    public static void LoadAppSettingsFromAsset(this IConfigurationBuilder config)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var manifestName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));

            if (manifestName is null)
            {
                Android.Util.Log.Warn("MauiProgram", "appsettings.json no está embebido en el assembly.");
                return;
            }

            using var stream = assembly.GetManifestResourceStream(manifestName);
            if (stream is null)
            {
                Android.Util.Log.Warn("MauiProgram", $"No se pudo abrir el recurso {manifestName}.");
                return;
            }

            config.AddJsonStream(stream);
            Android.Util.Log.Info("MauiProgram",
                $"appsettings.json cargado desde EmbeddedResource ({manifestName}, {stream.Length} bytes).");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("MauiProgram",
                $"appsettings.json no encontrado o no se pudo cargar: {ex.Message}");
        }
    }
}
