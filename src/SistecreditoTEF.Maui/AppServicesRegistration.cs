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
        // ---- Configuracion (CloudLicense > appsettings > secrets > default) ----
        // QA M-2: el ApiConfig ya NO es un singleton congelado en el arranque. El
        // proveedor lo reconstruye cuando llega el INITIALIZE de HioPos, y se
        // registra TRANSIENT para que cada consumidor lea el valor vigente.
        services.AddSingleton<ICloudConfig, PreferencesCloudConfig>();
        services.AddSingleton<ApiConfigProvider>();
        // Los servicios reciben la configuracion por IApiConfigSource (un solo
        // constructor cada uno). Ver [IApiConfigSource] para el motivo.
        services.AddSingleton<IApiConfigSource>(sp => sp.GetRequiredService<ApiConfigProvider>());
        services.AddTransient(sp => sp.GetRequiredService<ApiConfigProvider>().Current);

        // ---- HTTP pipeline (auth + logging + resiliencia + pinning) ----
        // QA M-1: los DelegatingHandler van TRANSIENT, no singleton. Un handler
        // singleton es un antipatron conocido de HttpClientFactory: cuando la
        // factory rota la cadena (HandlerLifetime, 2 min por defecto) intenta
        // reasignar InnerHandler sobre la MISMA instancia ya usada, y el setter
        // lanza InvalidOperationException. Hoy no explotaba solo porque
        // CredinetRepository (singleton) capturaba el HttpClient para siempre
        // —una dependencia cautiva—, o sea dos defectos que se cancelaban. Con
        // handlers transient la factory puede rotar como corresponde.
        services.AddTransient<AuthInterceptor>();
        services.AddTransient<HttpLoggingHandler>();
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
                                             | System.Net.DecompressionMethods.Deflate,

                    // ─────────────────────────────────────────────────────────
                    // TECHO DE CONEXIONES SIMULTANEAS
                    // ─────────────────────────────────────────────────────────
                    // Por defecto es ilimitado, y la consulta de plazos validos
                    // dispara 8 getCreditDetails EN PARALELO por cada cambio de
                    // monto. Sin techo eso son 8 handshakes TLS al mismo tiempo.
                    //
                    // En un POS sobre red movil eso suele ser MAS LENTO que
                    // encolarlos: los handshakes compiten por el mismo enlace y la
                    // latencia de cada uno sube. Con un techo de 4, las 8 consultas
                    // reusan conexiones ya establecidas (keep-alive) en vez de
                    // negociar TLS ocho veces.
                    //
                    // 4 y no 2 porque el objetivo sigue siendo que las consultas se
                    // solapen: el limite es para no saturar el enlace, no para
                    // serializarlas.
                    MaxConnectionsPerServer = 4
                };
                // Certificate pinning. Se lee del proveedor en cada validacion
                // para que los pines que lleguen por CloudLicense apliquen sin
                // recompilar (QA A-5).
                var provider = sp.GetRequiredService<ApiConfigProvider>();
                handler.ServerCertificateCustomValidationCallback =
                    (_, cert, chain, errors) =>
                        CertificatePinning.Validate(
                            cert, chain, errors, provider.Current.CertificatePins);
                return handler;
            })
            .ConfigureHttpClient((sp, client) =>
            {
                var cfg = sp.GetRequiredService<ApiConfigProvider>().Current;
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

        // "Volver a HioPos": un solo lugar arma la salida sin cobro y le entrega el
        // resultado al POS. Lo usan el boton del header y el boton atras del POS.
        services.AddSingleton<IHioposExit, HioposExit>();

        // ---- Throttle de OTP (anti-spam de reenvios + tope de intentos) ----
        // QA A-12: tambien lleva el contador de INTENTOS DE VERIFICACION, que
        // antes vivia en el ViewModel (transient) y se reiniciaba navegando atras.
        services.AddSingleton<OtpRequestThrottle>(sp =>
        {
            var cfg = sp.GetRequiredService<ApiConfigProvider>().Current;
            return new OtpRequestThrottle(
                TimeSpan.FromSeconds(cfg.OtpResendCooldownSeconds),
                cfg.OtpMaxResends,
                cfg.OtpMaxVerifyAttempts);
        });

        // ---- Impresion del comprobante de ABONO (modo standalone) ----
        // QA: cadena con fallback REAL en runtime, no una eleccion unica al
        // resolver el DI. Antes se elegia un printer al arrancar y si fallaba al
        // imprimir no habia alternativa; ademas AndroidPrintPrinter.IsAvailable
        // es true en cualquier Android, asi que PdfReceiptPrinter era codigo
        // muerto. Ver [CompositeReceiptPrinter].
        //
        // Orden:
        //   1) USB ESC/POS    - la termica del POS. Es el camino REAL: se verifico
        //                       en el terminal que la impresora es USB de clase 07
        //                       y que HioPos le habla asi (ver UsbEscPosPrinter).
        //   2) Sunmi nativo   - solo si esta habilitado y el AIDL oficial esta
        //                       presente (hoy deshabilitado: ver SunmiPrinter).
        //   3) Android Print  - PDF al print framework. Solo sirve si el POS tiene
        //                       una impresora registrada en Ajustes (de red); en los
        //                       terminales revisados no hay ninguna.
        //   4) Compartir PDF  - chooser del sistema como ultimo recurso.
        services.AddSingleton<UsbEscPosPrinter>();
        services.AddSingleton<SunmiPrinter>(sp =>
            new SunmiPrinter(sp.GetRequiredService<ApiConfigProvider>().Current.EnableSunmiNative));
        services.AddSingleton<AndroidPrintPrinter>();
        services.AddSingleton<PdfReceiptPrinter>();
        // ─────────────────────────────────────────────────────────────────────
        // EL PDF YA NO ESTA EN LA CADENA AUTOMATICA
        // ─────────────────────────────────────────────────────────────────────
        // [PdfReceiptPrinter] devuelve true al generar el archivo, asi que el
        // compuesto se detenia ahi y daba el comprobante por IMPRESO. Resultado en
        // terminal: el cajero terminaba el abono, no salia nada por la termica y
        // solo aparecia un PDF, sin aviso ni posibilidad de reintentar, porque para
        // la app la impresion habia sido exitosa.
        //
        // En este POS la termica USB esta conectada de forma permanente, asi que un
        // PDF no es un respaldo util: es un fallo disfrazado de exito. Sacandolo de
        // la cadena, si la termica falla [ImprimirAsync] devuelve false y el cajero
        // ve el dialogo de reintento en vez de quedarse sin comprobante.
        //
        // El servicio sigue registrado por si se lo quiere ofrecer como accion
        // manual (compartir/guardar), pero no participa de la impresion automatica.
        services.AddSingleton<IReceiptPrinter>(sp => new CompositeReceiptPrinter(
        [
            sp.GetRequiredService<UsbEscPosPrinter>(),
            sp.GetRequiredService<SunmiPrinter>(),
            sp.GetRequiredService<AndroidPrintPrinter>()
        ]));

        // ---- Dominio ----
        services.AddSingleton<SistecreditoService>();

        // ---- Ingreso de cajeros (solo abonos) ----
        // El store y el servicio son SINGLETON a proposito:
        //   • El store cachea la conexion a la BD cifrada; uno nuevo por pantalla
        //     reabriria SQLCipher en cada navegacion.
        //   • [AuthService] lleva el contador de intentos fallidos EN MEMORIA. Si
        //     fuera transient, el contador se reiniciaria al salir y volver a la
        //     pantalla, y el bloqueo por intentos seria trivial de evitar — el
        //     mismo defecto que tuvo el throttle del OTP (QA A-12).
        //   • [ISesionCajero] guarda quien esta operando: por definicion uno solo.
        // Contexto de arranque: lo escribe MainActivity.OnCreate antes de que exista
        // el Shell, y lo lee la pantalla raiz para no decidir en una venta.
        services.AddSingleton<ILaunchContext, LaunchContext>();

        services.AddSingleton<Services.Auth.IAuthStore, Services.Auth.SqliteAuthStore>();
        services.AddSingleton<Services.Auth.AuthService>();
        services.AddSingleton<Services.Auth.ISesionCajero, Services.Auth.SesionCajero>();

        // ---- ViewModels (transient: uno por navegacion) ----
        services.AddTransient<SplashViewModel>();
        services.AddTransient<ConfigurarAdminViewModel>();
        services.AddTransient<IngresoCajeroViewModel>();
        services.AddTransient<AdminCajerosViewModel>();

        // TRANSIENT, y es una decision de seguridad, no de estilo: este ViewModel
        // levanta un socket que ofrece el padron de la tienda. Como singleton
        // sobreviviria a salir de la pantalla y quedaria repartiendo todo el dia.
        services.AddTransient<ReplicacionViewModel>();
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
        services.AddTransient<Views.SplashPage>();
        services.AddTransient<Views.ConfigurarAdminPage>();
        services.AddTransient<Views.IngresoCajeroPage>();
        services.AddTransient<Views.AdminCajerosPage>();
        services.AddTransient<Views.ReplicacionPage>();
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
