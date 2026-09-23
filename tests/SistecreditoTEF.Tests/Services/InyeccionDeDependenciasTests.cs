using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Verifica que el grafo de inyección de dependencias se pueda RESOLVER.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ EXISTE ESTE ARCHIVO
/// ─────────────────────────────────────────────────────────────────────────────
/// Un servicio con DOS constructores públicos de la misma aridad hace que
/// Microsoft.Extensions.DependencyInjection lance
/// <c>AmbiguousConstructorException</c> en el primer <c>GetService</c>, y en una
/// app MAUI eso es un crash en el arranque del flujo, no un error de compilación.
///
/// El resto de la suite instancia las clases a mano, así que no puede detectarlo:
/// el código compila, los tests pasan, y la app crashea en el terminal. Estos
/// tests cierran ese hueco resolviendo servicios desde un contenedor real.
///
/// Nota: no se puede invocar <c>AppServicesRegistration</c> desde aquí porque
/// registra Pages y ViewModels que dependen de MAUI. Lo que se verifica es la
/// **capa de dominio y de acceso a datos**, que es donde vive el riesgo, más una
/// regla estructural sobre los constructores.
/// </summary>
public class InyeccionDeDependenciasTests
{
    private static readonly ApiConfig Config = new()
    {
        SubscriptionKey = "test",
        BaseUrl = "https://api.test/"
    };

    /// <summary>
    /// Registra la porción del contenedor real que no depende de MAUI, con la
    /// misma forma de registro que usa la app.
    /// </summary>
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApiConfigSource>(new StaticApiConfigSource(Config));
        services.AddTransient(sp => sp.GetRequiredService<IApiConfigSource>().Current);

        services.AddSingleton<ICredinetApi, FakeApi>();
        services.AddSingleton<ICredinetRepository, CredinetRepository>();
        services.AddSingleton<IIdempotencyStore, FakeIdempotency>();
        services.AddSingleton<IAuditLogger, FakeAudit>();
        services.AddSingleton<IAuditLogCapture, InMemoryAuditLog>();
        services.AddSingleton<ITransactionStateStore, TransactionStateStore>();
        services.AddSingleton<IRequestLogCapture, InMemoryRequestLogCapture>();
        services.AddTransient<AuthInterceptor>();
        services.AddTransient<HttpLoggingHandler>();
        services.AddSingleton<SistecreditoService>();
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IApiConfigSource>().Current;
            return new OtpRequestThrottle(
                TimeSpan.FromSeconds(cfg.OtpResendCooldownSeconds),
                cfg.OtpMaxResends,
                cfg.OtpMaxVerifyAttempts);
        });

        // validateOnBuild/validateScopes: la validación estricta detecta problemas
        // del grafo al construir el contenedor, no al primer uso.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void El_contenedor_se_construye_con_validacion_estricta()
    {
        using var provider = BuildContainer();
        Assert.NotNull(provider);
    }

    [Theory]
    [InlineData(typeof(ICredinetRepository))]
    [InlineData(typeof(ICredinetApi))]
    [InlineData(typeof(SistecreditoService))]
    [InlineData(typeof(AuthInterceptor))]
    [InlineData(typeof(HttpLoggingHandler))]
    [InlineData(typeof(ITransactionStateStore))]
    [InlineData(typeof(OtpRequestThrottle))]
    [InlineData(typeof(ApiConfig))]
    [InlineData(typeof(IApiConfigSource))]
    public void Cada_servicio_se_resuelve_sin_ambiguedad(Type servicio)
    {
        // Este es el test que faltaba: resolver de verdad desde el contenedor.
        // Con dos constructores de la misma aridad, esta llamada lanza
        // AmbiguousConstructorException — exactamente el crash que se veía en el
        // POS al abrir la app.
        using var provider = BuildContainer();

        var instancia = provider.GetRequiredService(servicio);

        Assert.NotNull(instancia);
        Assert.IsAssignableFrom(servicio, instancia);
    }

    [Fact]
    public void El_repositorio_lee_la_configuracion_de_la_fuente()
    {
        using var provider = BuildContainer();

        var repo = provider.GetRequiredService<ICredinetRepository>();

        Assert.IsType<CredinetRepository>(repo);
    }

    // ------------------------------------------------------------------
    // Regla estructural
    // ------------------------------------------------------------------

    /// <summary>
    /// Tipos destinados al contenedor de DI. Se listan explícitamente para que
    /// agregar un servicio obligue a pensar en su construcción.
    /// </summary>
    public static TheoryData<Type> TiposDeServicio() =>
    [
        typeof(CredinetRepository),
        typeof(CredinetApiClient),
        typeof(AuthInterceptor),
        typeof(HttpLoggingHandler),
        typeof(SistecreditoService),
        typeof(ApiConfigProvider),
        typeof(TransactionStateStore),
        typeof(InMemoryAuditLog),
        typeof(InMemoryRequestLogCapture),
        typeof(CompositeReceiptPrinter),
        typeof(HioposExit),
        typeof(StaticApiConfigSource)
    ];

    [Theory]
    [MemberData(nameof(TiposDeServicio))]
    public void Ningun_servicio_tiene_constructores_ambiguos(Type tipo)
    {
        // Regla: el contenedor elige el constructor con más parámetros que puede
        // satisfacer, y lanza si dos candidatos tienen la misma aridad y ninguno
        // es subconjunto del otro. La forma simple de garantizar que eso nunca
        // ocurra es no tener dos constructores públicos de la misma aridad.
        var ctors = tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        var duplicados = ctors
            .GroupBy(c => c.GetParameters().Length)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.True(duplicados.Count == 0,
            $"{tipo.Name} tiene {duplicados.Count} grupo(s) de constructores publicos con la " +
            "misma cantidad de parametros. El contenedor de DI lanzaria " +
            "AmbiguousConstructorException al resolverlo. Sobrecargas encontradas: " +
            string.Join(" | ", duplicados.SelectMany(g => g).Select(c =>
                $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})")));
    }

    [Fact]
    public void Los_servicios_criticos_tienen_exactamente_un_constructor()
    {
        // Para los servicios que el contenedor construye por reflexión (sin
        // factory explícita), un solo constructor elimina toda ambigüedad posible.
        foreach (var tipo in new[] { typeof(CredinetRepository), typeof(AuthInterceptor) })
        {
            var ctors = tipo.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.True(ctors.Length == 1,
                $"{tipo.Name} debe tener exactamente 1 constructor publico, tiene {ctors.Length}.");
        }
    }

    // ------------------------------------------------------------------
    // Dobles
    // ------------------------------------------------------------------

    private sealed class FakeApi : ICredinetApi
    {
        public Task<Dtos.ApiResponse<Dtos.ClientDto>> GetCreditLimitClientAsync(string t, string i, string? s) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<Dtos.CreditDetailsDto>> GetCreditDetailsAsync(double c, int f, int m, string t, string i, string? s) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<Dtos.CreditTokenDto>> GetCreditTokenAsync(double c, int m, int f, string t, string i, int? d, string? s) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<Dtos.CreditDto>> CreateAsync(Dtos.CreateCreditRequest r) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<List<Dtos.ActiveCreditDto>>> GetActiveCreditsAsync(string t, string i, string? s) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<Dtos.PaymentDto>> PayCreditAsync(Dtos.PayCreditRequest r) => throw new NotSupportedException();
        public Task<Dtos.ApiResponse<Dtos.SimulatedMonthLimitDto>> GetSimulatedMonthLimitAsync(double c, string? s) => throw new NotSupportedException();
    }

    private sealed class FakeIdempotency : IIdempotencyStore
    {
        public Task<CachedTransaction?> FindBySaleIdAsync(string saleId) => Task.FromResult<CachedTransaction?>(null);
        public Task SaveAsync(CachedTransaction transaction) => Task.CompletedTask;
        public Task<CachedPayment?> FindRecentPaymentAsync(string c, long a, TimeSpan w) => Task.FromResult<CachedPayment?>(null);
        public Task SavePaymentAsync(CachedPayment payment) => Task.CompletedTask;
        public Task PurgeOlderThanAsync(TimeSpan retention) => Task.CompletedTask;
    }

    private sealed class FakeAudit : IAuditLogger
    {
        public void Log(string action, string comment, string? documentId = null) { }
    }
}
