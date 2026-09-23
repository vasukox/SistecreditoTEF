using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// QA C-3 / M-3: la precedencia de configuración es la regla de negocio más
/// delicada del módulo —decide si el POS habla con producción o con sandbox— y no
/// tenía NI UN test. Además varios parámetros documentados como "configurables sin
/// recompilar" solo se leían de appsettings.
/// </summary>
public class ApiConfigTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v =>
                new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class FakeCloud : ICloudConfig
    {
        private readonly Dictionary<string, string> _values;
        public FakeCloud(params (string Key, string Value)[] values) =>
            _values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    }

    // ------------------------------------------------------------------
    // Precedencia
    // ------------------------------------------------------------------

    [Fact]
    public void CloudLicense_gana_sobre_appsettings()
    {
        var settings = Settings(
            ("Credinet:SubscriptionKey", "de-appsettings"),
            ("Credinet:BaseUrl", "https://api.credinet.co/pos/"),
            ("Credinet:StoreId", "1"));
        var cloud = new FakeCloud(
            (ICloudConfig.SubscriptionKey, "de-cloud"),
            (ICloudConfig.ApiBaseUrl, "https://api.credinet.co/posprod/"),
            (ICloudConfig.StoreId, "99"));

        var config = ApiConfig.FromConfiguration(settings, cloud);

        Assert.Equal("de-cloud", config.SubscriptionKey);
        Assert.Equal("https://api.credinet.co/posprod/", config.BaseUrl);
        Assert.Equal("99", config.StoreId);
    }

    [Fact]
    public void Sin_CloudLicense_se_usa_appsettings()
    {
        var settings = Settings(
            ("Credinet:SubscriptionKey", "de-appsettings"),
            ("Credinet:BaseUrl", "https://api.credinet.co/pos/"));

        var config = ApiConfig.FromConfiguration(settings, new FakeCloud());

        Assert.Equal("de-appsettings", config.SubscriptionKey);
    }

    [Fact]
    public void El_marcador_SANDBOX_se_expande_a_la_key_publica()
    {
        var settings = Settings(("Credinet:SubscriptionKey", ApiConfig.SandboxKeyPlaceholder));

        var config = ApiConfig.FromConfiguration(settings, new FakeCloud());

        Assert.Equal(ApiConfig.SandboxSubscriptionKey, config.SubscriptionKey);
        Assert.True(config.UsesSandboxKey);
    }

    [Fact]
    public void Sin_SubscriptionKey_lanza_en_vez_de_arrancar_a_medias()
    {
        var settings = Settings(("Credinet:BaseUrl", "https://api.credinet.co/pos/"));

        Assert.Throws<InvalidOperationException>(
            () => ApiConfig.FromConfiguration(settings, new FakeCloud()));
    }

    [Fact]
    public void La_BaseUrl_siempre_termina_en_barra()
    {
        // HttpClient.BaseAddress necesita '/' final para combinar rutas relativas.
        var settings = Settings(
            ("Credinet:SubscriptionKey", "k"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod"));

        var config = ApiConfig.FromConfiguration(settings, new FakeCloud());

        Assert.Equal("https://api.credinet.co/posprod/", config.BaseUrl);
    }

    // ------------------------------------------------------------------
    // QA M-3: parámetros que antes NO se leían de CloudLicense
    // ------------------------------------------------------------------

    [Fact]
    public void CloudLicense_puede_definir_los_parametros_operativos()
    {
        var settings = Settings(("Credinet:SubscriptionKey", "k"));
        var cloud = new FakeCloud(
            (ICloudConfig.Frequency, "15"),
            (ICloudConfig.Source, "9"),
            (ICloudConfig.AuthMethod, "2"),
            (ICloudConfig.TimeoutSeconds, "45"),
            (ICloudConfig.OtpMaxResends, "7"),
            (ICloudConfig.StoreName, "KOAJ Unicentro"),
            (ICloudConfig.OtpDestination, "1"));

        var config = ApiConfig.FromConfiguration(settings, cloud);

        Assert.Equal(15, config.Frequency);
        Assert.Equal("9", config.Source);
        Assert.Equal(2, config.AuthMethod);
        Assert.Equal(45, config.TimeoutSeconds);
        Assert.Equal(7, config.OtpMaxResends);
        Assert.Equal("KOAJ Unicentro", config.StoreName);
        Assert.Equal(1, config.OtpDestination);
    }

    [Fact]
    public void Los_pines_TLS_se_pueden_entregar_por_CloudLicense()
    {
        // QA A-5: antes solo se leían de appsettings, así que el pinning no se
        // podía activar sin recompilar el APK.
        var settings = Settings(("Credinet:SubscriptionKey", "k"));
        var cloud = new FakeCloud((ICloudConfig.CertificatePins, "pin1=, pin2=;pin3="));

        var config = ApiConfig.FromConfiguration(settings, cloud);

        Assert.Equal(3, config.CertificatePins.Count);
        Assert.Contains("pin1=", config.CertificatePins);
        Assert.Contains("pin3=", config.CertificatePins);
    }

    // ------------------------------------------------------------------
    // QA C-3: validación de coherencia ambiente / credenciales
    // ------------------------------------------------------------------

    [Fact]
    public void Produccion_con_key_de_sandbox_es_invalida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", ApiConfig.SandboxKeyPlaceholder),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:StoreId", "7"),
            ("Credinet:Environment", "production")), new FakeCloud());

        var problems = config.Validate();

        Assert.NotEmpty(problems);
        Assert.Contains(problems, p => p.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Produccion_apuntando_a_la_URL_de_sandbox_es_invalida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "key-real"),
            ("Credinet:BaseUrl", "https://api.credinet.co/pos/"),
            ("Credinet:StoreId", "7"),
            ("Credinet:Environment", "production")), new FakeCloud());

        var problems = config.Validate();

        Assert.Contains(problems, p => p.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Produccion_sin_StoreId_es_invalida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "key-real"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:Environment", "production")), new FakeCloud());

        Assert.Contains(config.Validate(), p => p.Contains("STORE_ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Produccion_bien_configurada_es_valida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "key-real"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:StoreId", "7"),
            ("Credinet:Environment", "production")), new FakeCloud());

        Assert.Empty(config.Validate());
        Assert.True(config.IsProduction);
    }

    [Fact]
    public void Sandbox_con_key_de_sandbox_es_valido()
    {
        // El ambiente de pruebas DEBE seguir funcionando: la validación estricta
        // solo aplica a producción.
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", ApiConfig.SandboxKeyPlaceholder),
            ("Credinet:BaseUrl", "https://api.credinet.co/pos/"),
            ("Credinet:Environment", "sandbox")), new FakeCloud());

        Assert.Empty(config.Validate());
        Assert.False(config.IsProduction);
    }

    [Fact]
    public void Una_BaseUrl_sin_HTTPS_es_invalida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "k"),
            ("Credinet:BaseUrl", "http://api.credinet.co/pos/")), new FakeCloud());

        Assert.Contains(config.Validate(), p => p.Contains("HTTPS", StringComparison.Ordinal));
    }
}
