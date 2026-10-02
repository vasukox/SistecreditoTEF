using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Tiendas;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Tiendas;

/// <summary>
/// A NOMBRE DE QUE TIENDA QUEDA CADA CREDITO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL CASO QUE ORIGINO TODO ESTO
/// ─────────────────────────────────────────────────────────────────────────────
/// Creditos hechos en una tienda de Suba quedaron registrados en la plataforma de
/// Sistecredito a nombre de la 037 (Punto Calle 18, Bogota). No fallo nada, no
/// hubo un error, y no se noto hasta conciliar: el APK productivo traia horneado
/// el StoreId de la 037 y cualquier tienda sin provisionar en CloudLicense
/// reportaba como esa.
///
/// Estas pruebas fijan las tres decisiones que lo impiden:
///   1. El catalogo de tiendas es correcto y completo (se elige, no se teclea).
///   2. Cuando HioPosCloud y la caja discrepan, NO se elige: se frena.
///   3. El valor del APK es el ultimo recurso y queda marcado como tal.
/// </summary>
public class TiendaDeLaCajaTests
{
    private const string StoreIdDe037 = "607af8e38c91f70001436058";

    // ══════════════════════════════════════════════════════════════════════
    // EL CATALOGO
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El catalogo se GENERA desde la hoja de Sistecredito. Si alguien lo regenera
    /// con una hoja rota, esto lo atrapa antes de que llegue a una caja: un
    /// StoreId mal copiado son creditos de una tienda a nombre de otra.
    /// </summary>
    [Fact]
    public void ElCatalogo_TieneDatosUsables()
    {
        var todas = CatalogoDeTiendas.Todas;

        Assert.NotEmpty(todas);

        foreach (var tienda in todas)
        {
            Assert.Matches(new Regex(@"^\d{3}$"), tienda.Codigo);
            Assert.Matches(new Regex("^[0-9a-f]{24}$"), tienda.StoreId);
            Assert.False(string.IsNullOrWhiteSpace(tienda.Ciudad));

            // El nombre SI puede faltar (las tiendas nuevas de la hoja), pero
            // entonces tiene que haber algo que mostrar.
            Assert.False(string.IsNullOrWhiteSpace(tienda.NombreVisible));
        }
    }

    [Fact]
    public void ElCatalogo_NoRepiteCodigosNiIdentificadores()
    {
        var todas = CatalogoDeTiendas.Todas;

        Assert.Equal(todas.Count, todas.Select(t => t.Codigo).Distinct().Count());
        Assert.Equal(todas.Count,
            todas.Select(t => t.StoreId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// La 037 es la tienda del piloto y la que aparecia mal en los créditos de
    /// Suba. Su StoreId está anclado acá para que una regeneración del catálogo no
    /// lo mueva sin que nadie se entere.
    /// </summary>
    [Fact]
    public void LaTiendaDelPiloto_ConservaSuIdentificador()
    {
        var tienda = CatalogoDeTiendas.PorCodigo("037");

        Assert.NotNull(tienda);
        Assert.Equal(StoreIdDe037, tienda!.StoreId);
        Assert.Equal(tienda, CatalogoDeTiendas.PorStoreId(StoreIdDe037));
    }

    /// <summary>
    /// Diez tiendas de la hoja llegan sin nombre. Se conservan igual —tienen
    /// StoreId valido— y se muestran por codigo, porque sacarlas obligaria a
    /// teclear 24 caracteres a mano justo en las tiendas que se estan abriendo.
    /// </summary>
    [Fact]
    public void UnaTiendaSinNombre_SeMuestraPorCodigo()
    {
        var sinNombre = new TiendaDelCatalogo("545", "", "Bogota", "67f69d06ff7ecff19a5743a9");

        Assert.Equal("Tienda 545", sinNombre.NombreVisible);
        Assert.Equal("545 · Tienda 545", sinNombre.Etiqueta);
    }

    // ── Búsqueda ──────────────────────────────────────────────────────────

    [Fact]
    public void SeBusca_PorCodigo()
    {
        var encontradas = CatalogoDeTiendas.Buscar("037");

        Assert.Equal("037", encontradas[0].Codigo);
    }

    /// <summary>
    /// Quien instala teclea "037" o teclea "suba". Los dos tienen que funcionar, y
    /// el codigo —que es la forma exacta de nombrar una tienda— va primero.
    /// </summary>
    [Fact]
    public void SeBusca_PorNombre_SinDistinguirTildesNiMayusculas()
    {
        var conTilde = CatalogoDeTiendas.Buscar("GALERÍAS");
        var sinTilde = CatalogoDeTiendas.Buscar("galerias");

        Assert.NotEmpty(sinTilde);
        Assert.Equal(sinTilde.Select(t => t.Codigo), conTilde.Select(t => t.Codigo));
    }

    [Fact]
    public void SinTextoDeBusqueda_SeDevuelveTodo()
    {
        // La lista no arranca vacía: el instalador ve las tiendas desde el principio.
        Assert.Equal(CatalogoDeTiendas.Todas.Count, CatalogoDeTiendas.Buscar("").Count);
        Assert.Equal(CatalogoDeTiendas.Todas.Count, CatalogoDeTiendas.Buscar(null).Count);
    }

    /// <summary>
    /// Un StoreId que no figura en la hoja se muestra ENTERO y se dice que no
    /// figura. Es el caso que hay que poder ver: o la hoja quedó vieja, o alguien
    /// cargó un valor que no corresponde.
    /// </summary>
    [Fact]
    public void UnIdentificadorDesconocido_SeDiceQueNoFigura()
    {
        var texto = CatalogoDeTiendas.Describir("000000000000000000000000");

        Assert.Contains("000000000000000000000000", texto, StringComparison.Ordinal);
        Assert.Contains("no figura", texto, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════
    // LA RESOLUCIÓN
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SinElegirla_LaCajaNoPuedeOperar()
    {
        var r = ResolucionDeTienda.Resolver(null, null);

        Assert.Equal(EstadoDeLaTienda.SinElegir, r.Estado);
        Assert.False(r.SePuedeOperar);
    }

    /// <summary>
    /// LA REGLA. El STORE_ID de HioPosCloud NO define la tienda: si nadie la eligió
    /// en el terminal, la caja no opera aunque el POS mande uno.
    ///
    /// Es deliberado y tiene un costo —un paso más al instalar—. La razón es que un
    /// valor que llega de afuera y nadie revisó es exactamente lo que puso los
    /// créditos de Suba a nombre de la 037.
    /// </summary>
    [Fact]
    public void AunqueHioPosMandeUno_SiNadieLaEligio_NoSeOpera()
    {
        var r = ResolucionDeTienda.Resolver(StoreIdDe037, null);

        Assert.Equal(EstadoDeLaTienda.SinElegir, r.Estado);
        Assert.Null(r.StoreId);
        Assert.False(r.SePuedeOperar);
    }

    [Fact]
    public void ElegidaEnElTerminal_EsLaQueVale()
    {
        var r = ResolucionDeTienda.Resolver(null, StoreIdDe037);

        Assert.Equal(EstadoDeLaTienda.Elegida, r.Estado);
        Assert.Equal(StoreIdDe037, r.StoreId);
        Assert.True(r.SePuedeOperar);
    }

    [Fact]
    public void SiCoinciden_NoHayNadaQueAvisar()
    {
        var r = ResolucionDeTienda.Resolver(StoreIdDe037, StoreIdDe037);

        Assert.True(r.SePuedeOperar);
        Assert.False(r.DiscrepaConHioPos);
    }

    /// <summary>
    /// Si HioPosCloud dice otra tienda, manda la elegida en el terminal y la
    /// discrepancia queda ANOTADA. No frena: dejar una caja sin vender porque ICG
    /// cree otra cosa sería peor que el aviso.
    /// </summary>
    [Fact]
    public void SiHioPosDiceOtra_MandaLaElegida_YSeAnota()
    {
        var suba = "607d8d208c91f70001439630";

        var r = ResolucionDeTienda.Resolver(deHiopos: StoreIdDe037, elegidaEnLaCaja: suba);

        Assert.Equal(suba, r.StoreId);
        Assert.True(r.SePuedeOperar);
        Assert.True(r.DiscrepaConHioPos);
        Assert.Equal(StoreIdDe037, r.DeHioPos);
    }

    [Fact]
    public void LaDiscrepancia_SeExplicaNombrandoLasDosTiendas()
    {
        var suba = "607d8d208c91f70001439630";

        var texto = ResolucionDeTienda.Resolver(StoreIdDe037, suba).ParaElLog();

        Assert.Contains("037", texto, StringComparison.Ordinal);
        Assert.Contains("197", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void SinTienda_ElLogDiceQueHayQueElegirla()
    {
        var texto = ResolucionDeTienda.Resolver(null, null).ParaElLog();

        Assert.Contains("SIN ELEGIR", texto, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ENGANCHE CON LA CONFIGURACIÓN
    // ══════════════════════════════════════════════════════════════════════

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

    private static IConfiguration Produccion() => Settings(
        ("Credinet:SubscriptionKey", "clave-real"),
        ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
        ("Credinet:Environment", "production"),
        ("Credinet:StoreId", ""),
        ("Credinet:StoreName", "Permoda"));

    [Fact]
    public void EnProduccion_LaTiendaDeLaCaja_HabilitaLaOperacion()
    {
        var config = ApiConfig.FromConfiguration(
            Produccion(), new FakeCloud(), storeIdDeLaCaja: StoreIdDe037);

        Assert.Equal(StoreIdDe037, config.StoreId);
        Assert.Empty(config.Validate());
    }

    /// <summary>
    /// Sin tienda elegida no se opera, Y ESO VALE TAMBIÉN EN SANDBOX.
    ///
    /// El resto de la validación solo aprieta en producción, porque son
    /// incoherencias entre ambientes. Esta no: si en pruebas se pudiera vender sin
    /// elegir la tienda, el paso se descubriría el día que la caja pasa a
    /// producción, ya instalada y vendiendo.
    /// </summary>
    [Theory]
    [InlineData("production")]
    [InlineData("sandbox")]
    public void SinTiendaElegida_NoSePuedeOperar(string ambiente)
    {
        var settings = Settings(
            ("Credinet:SubscriptionKey", "clave"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:Environment", ambiente));

        var config = ApiConfig.FromConfiguration(settings, new FakeCloud());

        Assert.Null(config.StoreId);
        Assert.Contains(config.Validate(), p =>
            p.Contains("SIN TIENDA", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ni el STORE_ID de CloudLicense ni el del APK habilitan una caja. Los dos son
    /// valores que llegan de afuera y que nadie revisó para ESTE terminal — el del
    /// APK, además, es el mismo para las 76 tiendas que lo instalen, que fue
    /// exactamente el problema.
    /// </summary>
    [Fact]
    public void NiElApkNiHioPos_HabilitanUnaCaja()
    {
        var settings = Settings(
            ("Credinet:SubscriptionKey", "clave"),
            ("Credinet:BaseUrl", "https://api.credinet.co/posprod/"),
            ("Credinet:Environment", "production"),
            ("Credinet:StoreId", StoreIdDe037));   // horneado en el paquete

        var config = ApiConfig.FromConfiguration(
            settings, new FakeCloud((ICloudConfig.StoreId, StoreIdDe037)));

        Assert.Null(config.StoreId);
        Assert.NotEmpty(config.Validate());
    }

    /// <summary>La falta de tienda se reporta UNA vez, no una por ambiente.</summary>
    [Fact]
    public void LaFaltaDeTienda_NoSeReportaDosVeces()
    {
        var problemas = ApiConfig.FromConfiguration(Produccion(), new FakeCloud()).Validate();

        Assert.Single(problemas);
    }

    // ══════════════════════════════════════════════════════════════════════
    // EL IDENTIFICADOR NO VIAJA EN SANDBOX
    // ══════════════════════════════════════════════════════════════════════
    //
    // Las tiendas del catálogo salen de la hoja STOREID-SISTECREDITO, que es de
    // PRODUCCIÓN. El ambiente de pruebas de Credinet no las conoce. Verificado
    // contra la API, misma cédula, con y sin el parámetro:
    //
    //   getSimulatedMonthLimit  sin storeId -> 200 {"months":2}
    //   getSimulatedMonthLimit  con storeId -> 400 errorCode 225 StoreNotFound
    //   getactivecredits        con storeId -> 400 errorCode 225 StoreNotFound
    //   getCreditDetails        con storeId -> 400 errorCode 225 StoreNotFound
    //
    // Al hacer la tienda obligatoria empezamos a mandarlo también en sandbox, y el
    // resultado en la terminal fue que una venta no simulaba créditos y un abono
    // decía "esta tienda no está registrada en Sistecrédito".

    [Fact]
    public void EnSandbox_ElIdentificadorNoSeLeManda_ACredinet()
    {
        var settings = Settings(
            ("Credinet:SubscriptionKey", "clave"),
            ("Credinet:BaseUrl", "https://api.credinet.co/pos/"),
            ("Credinet:Environment", "sandbox"));

        var config = ApiConfig.FromConfiguration(
            settings, new FakeCloud(), storeIdDeLaCaja: StoreIdDe037);

        // Lo que viaja a la API: nada.
        Assert.Null(config.StoreId);

        // Pero la tienda elegida NO se pierde: sigue para pantallas y comprobante.
        Assert.Equal(StoreIdDe037, config.Tienda.StoreId);
        Assert.Equal(CatalogoDeTiendas.PorCodigo("037")!.NombreVisible, config.StoreName);

        // Y la caja puede operar: elegirla sigue siendo obligatorio y ya está hecha.
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void EnProduccion_ElIdentificadorSiViaja()
    {
        var config = ApiConfig.FromConfiguration(
            Produccion(), new FakeCloud(), storeIdDeLaCaja: StoreIdDe037);

        Assert.Equal(StoreIdDe037, config.StoreId);
    }

    /// <summary>
    /// Si HioPosCloud manda otra tienda, la elegida en el terminal igual opera: la
    /// discrepancia se anota, no frena.
    /// </summary>
    [Fact]
    public void ConDiscrepancia_LaCajaSigueOperando()
    {
        var suba = "607d8d208c91f70001439630";

        var config = ApiConfig.FromConfiguration(
            Produccion(),
            new FakeCloud((ICloudConfig.StoreId, StoreIdDe037)),
            storeIdDeLaCaja: suba);

        Assert.Equal(suba, config.StoreId);
        Assert.True(config.Tienda.DiscrepaConHioPos);
        Assert.Empty(config.Validate());
    }

    /// <summary>
    /// Y el resultado visible de todo esto: el comprobante del abono deja de decir
    /// "Permoda" en las 76 tiendas. El nombre sale de la hoja cuando ICG no manda
    /// STORE_NAME.
    /// </summary>
    [Fact]
    public void ElNombreDeLaTienda_SaleDelCatalogo_CuandoIcgNoLoManda()
    {
        var config = ApiConfig.FromConfiguration(
            Produccion(), new FakeCloud(), storeIdDeLaCaja: StoreIdDe037);

        Assert.NotEqual("Permoda", config.StoreName);
        Assert.Equal(CatalogoDeTiendas.PorCodigo("037")!.NombreVisible, config.StoreName);
    }

    /// <summary>
    /// EL NOMBRE DESCRIBE AL StoreId QUE SE MANDA, NO A LO QUE DIGA ICG.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE SE INVIRTIO LA PRECEDENCIA
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes ganaba STORE_NAME de CloudLicense. Eso permite el peor estado
    /// posible: la caja manda el identificador de UNA tienda y la pantalla —y el
    /// comprobante— muestran el nombre de OTRA. Una mentira coherente, imposible
    /// de detectar mirando el terminal, que es exactamente como se perdieron
    /// semanas persiguiendo creditos de la 012 registrados en la 037.
    ///
    /// El nombre y el identificador tienen que hablar de la misma tienda SIEMPRE.
    /// Por eso manda la hoja: es la unica fuente que los relaciona.
    /// </summary>
    [Fact]
    public void ElNombreDescribeLaTiendaQueSeManda_AunqueIcgDigaOtra()
    {
        var config = ApiConfig.FromConfiguration(
            Produccion(),
            new FakeCloud((ICloudConfig.StoreName, "NOMBRE DISTINTO QUE MANDA ICG")),
            storeIdDeLaCaja: StoreIdDe037);

        Assert.Equal(StoreIdDe037, config.StoreId);
        Assert.Equal(CatalogoDeTiendas.PorCodigo("037")!.NombreVisible, config.StoreName);
    }

    /// <summary>
    /// ICG sigue sirviendo para lo que no esta en la hoja: una tienda nueva, con
    /// StoreId valido pero todavia sin fila. Ahi su nombre es mejor que un
    /// ObjectId de 24 caracteres.
    /// </summary>
    [Fact]
    public void SiLaTiendaNoEstaEnLaHoja_SeUsaElNombreDeIcg()
    {
        const string noEstaEnLaHoja = "aaaaaaaaaaaaaaaaaaaaaaaa";

        var config = ApiConfig.FromConfiguration(
            Produccion(),
            new FakeCloud((ICloudConfig.StoreName, "TIENDA RECIEN ABIERTA")),
            storeIdDeLaCaja: noEstaEnLaHoja);

        Assert.Equal("TIENDA RECIEN ABIERTA", config.StoreName);
    }

    /// <summary>
    /// Y SIN TIENDA NO HAY NOMBRE GENERICO.
    ///
    /// Esto terminaba en `?? "Permoda"`, con el appsettings trayendo "Permoda":
    /// una caja sin tienda elegida se veia IGUAL que una configurada, en la
    /// cabecera, en el comprobante y en el saludo entre cajas. El estado mas
    /// peligroso del modulo disfrazado del estado sano.
    /// </summary>
    [Fact]
    public void SinTiendaElegida_NoSeInventaUnNombre()
    {
        var config = ApiConfig.FromConfiguration(
            Produccion(), new FakeCloud(), storeIdDeLaCaja: null);

        Assert.Equal(ApiConfig.SinTienda, config.StoreName);
        Assert.DoesNotContain("Permoda", config.StoreName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(config.Validate());
    }
}
