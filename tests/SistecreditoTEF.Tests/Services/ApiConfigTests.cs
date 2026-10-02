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

        // El StoreId ya NO sigue esta precedencia: la tienda se elige en el
        // terminal y ninguna de las dos fuentes de este test la define. Lo que
        // manda CloudLicense se conserva solo para avisar si no coincide.
        Assert.Null(config.StoreId);
        Assert.Equal("99", config.Tienda.DeHioPos);
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
            (ICloudConfig.OtpDestination, "1"));

        var config = ApiConfig.FromConfiguration(settings, cloud);

        Assert.Equal(15, config.Frequency);
        Assert.Equal("9", config.Source);
        Assert.Equal(2, config.AuthMethod);
        Assert.Equal(45, config.TimeoutSeconds);
        Assert.Equal(7, config.OtpMaxResends);
        Assert.Equal(1, config.OtpDestination);

        // STORE_NAME ya NO se comprueba aqui, y no por descuido: dejo de ser un
        // parametro operativo mas. El nombre de la tienda depende de que tienda
        // tenga elegida la caja —tiene que describir al StoreId que se manda— y
        // sus reglas viven en [TiendaDeLaCajaTests]. Sin tienda elegida, como en
        // este caso, no hay nombre que valga: ver ApiConfig.SinTienda.
        Assert.Equal(ApiConfig.SinTienda, config.StoreName);
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
    public void Produccion_sin_tienda_elegida_es_invalida()
    {
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "key-real"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:Environment", "production")), new FakeCloud());

        Assert.Contains(config.Validate(), p => p.Contains("SIN TIENDA", StringComparison.Ordinal));
    }

    /// <summary>
    /// La tienda se elige EN EL TERMINAL, así que una configuración válida la
    /// incluye. Antes este test pasaba un <c>Credinet:StoreId</c> por appsettings:
    /// ese camino dejó de habilitar una caja, porque un valor dentro del paquete es
    /// el mismo para las 76 tiendas que lo instalen — fue lo que puso los créditos
    /// de Suba a nombre de la 037.
    /// </summary>
    [Fact]
    public void Produccion_bien_configurada_es_valida()
    {
        var config = ApiConfig.FromConfiguration(
            Settings(
                ("Credinet:SubscriptionKey", "key-real"),
                ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
                ("Credinet:Environment", "production")),
            new FakeCloud(),
            storeIdDeLaCaja: "607af8e38c91f70001436058");

        Assert.Empty(config.Validate());
        Assert.True(config.IsProduction);
    }

    // ══════════════════════════════════════════════════════════════════════
    // EL APK NO DICE QUE TIENDA ES
    // ══════════════════════════════════════════════════════════════════════
    //
    // appsettings.produccion.json traia "StoreId": "607af8e38c91f70001436058"
    // (la 037). Con ese valor horneado, una tienda de las 76 que ICG no hubiera
    // provisionado tomaba ese StoreId y REPORTABA SUS CREDITOS A LA 037: venta
    // verde, cobro bien, y un credito a nombre de otra tienda que se descubria
    // conciliando en la plataforma de Sistecredito semanas despues.
    //
    // La defensa son dos cosas juntas, y las dos hacen falta:
    //   1. el APK no trae StoreId (este test lo fija), y
    //   2. produccion sin STORE_ID se RECHAZA, no se cae a un valor de rescate.

    [Fact]
    public void El_appsettings_de_produccion_no_trae_StoreId_horneado()
    {
        // Si alguien vuelve a pegar un StoreId aca, este test lo canta. Es la
        // unica linea de codigo que decia "este APK es de la 037".
        var ruta = BuscarEnElRepo("src", "SistecreditoTEF.Maui", "appsettings.produccion.json");
        Assert.NotNull(ruta);

        // appsettings.produccion.json es JSON con comentarios, asi que no se
        // parsea: se busca el "StoreId" del bloque Credinet y se mira su valor.
        var texto = System.IO.File.ReadAllText(ruta!);
        var indice = texto.IndexOf("\"StoreId\"", StringComparison.Ordinal);
        Assert.True(indice >= 0, "no se encontro la clave StoreId en appsettings.produccion.json");

        var desde = texto.IndexOf(':', indice);
        var hasta = texto.IndexOfAny(new[] { ',', '\r', '\n' }, desde);
        var valor = texto[(desde + 1)..hasta].Trim();

        Assert.Equal("\"\"", valor);
    }

    [Fact]
    public void Sin_tienda_el_mensaje_dice_donde_elegirla()
    {
        // El mensaje tiene que decir QUE HACER y DONDE. Antes decia que la terminal
        // "no estaba provisionada", que describe un estado y deja al instalador
        // igual que estaba: no habia nada que el pudiera hacer desde la caja.
        // Ahora la tienda se elige en el propio terminal, asi que el mensaje es una
        // instruccion.
        var config = ApiConfig.FromConfiguration(Settings(
            ("Credinet:SubscriptionKey", "key-real"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:StoreId", ""),
            ("Credinet:Environment", "production")), new FakeCloud());

        var problema = Assert.Single(config.Validate());
        Assert.Contains("Configuracion", problema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tienda de esta caja", problema, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Busca un archivo del repositorio subiendo desde la carpeta de salida de las
    /// pruebas. Los binarios viven en tests/…/bin/Debug/net10.0, asi que hay que
    /// subir hasta la raiz del repositorio y de ahi bajar por la ruta pedida.
    /// </summary>
    private static string? BuscarEnElRepo(params string[] segmentos)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var ruta = Path.Combine(new[] { dir.FullName }.Concat(segmentos).ToArray());
            if (File.Exists(ruta)) return ruta;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Sandbox_con_key_de_sandbox_es_valido()
    {
        // El ambiente de pruebas DEBE seguir funcionando: la validación estricta de
        // key y URL solo aplica a producción.
        //
        // La TIENDA es la excepción y se pasa acá: elegirla es obligatorio también
        // en sandbox. Si en pruebas se pudiera vender sin ella, el paso se
        // descubriría el día que la caja pasa a producción, ya instalada.
        var config = ApiConfig.FromConfiguration(
            Settings(
                ("Credinet:SubscriptionKey", ApiConfig.SandboxKeyPlaceholder),
                ("Credinet:BaseUrl", "https://api.credinet.co/pos/"),
                ("Credinet:Environment", "sandbox")),
            new FakeCloud(),
            storeIdDeLaCaja: "607af8e38c91f70001436058");

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
