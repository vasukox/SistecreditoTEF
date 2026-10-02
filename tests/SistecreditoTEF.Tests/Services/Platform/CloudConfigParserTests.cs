using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Platform;

/// <summary>
/// QA C-3: era el hallazgo más peligroso del módulo.
///
/// El parseo usaba <c>doc.Descendants("Param")</c>, que exige namespace VACÍO. Si
/// ICG entregaba el XML con un namespace por defecto, devolvía cero elementos, la
/// app caía al fallback de appsettings.json (key pública de sandbox + URL de
/// sandbox) y una terminal de PRODUCCIÓN operaba contra el ambiente de pruebas sin
/// que nadie se enterara — el log lo reportaba como éxito.
/// </summary>
public class CloudConfigParserTests
{
    [Fact]
    public void Parse_lee_los_parametros_sin_namespace()
    {
        const string xml = """
            <Configuration><Parameters>
              <Param Key="API_BASE_URL">https://api.credinet.co/posprod/</Param>
              <Param Key="SUBSCRIPTION_KEY">abc123</Param>
              <Param Key="STORE_ID">7</Param>
            </Parameters></Configuration>
            """;

        var values = CloudConfigParser.Parse(xml);

        Assert.Equal(3, values.Count);
        Assert.Equal("https://api.credinet.co/posprod/", values["API_BASE_URL"]);
        Assert.Equal("abc123", values["SUBSCRIPTION_KEY"]);
        Assert.Equal("7", values["STORE_ID"]);
    }

    [Fact]
    public void Parse_lee_los_parametros_CON_namespace_por_defecto()
    {
        // ESTE es el caso que fallaba y dejaba el POS en sandbox.
        const string xml = """
            <Configuration xmlns="http://schemas.icg.es/hiopos/config">
              <Parameters>
                <Param Key="API_BASE_URL">https://api.credinet.co/posprod/</Param>
                <Param Key="SUBSCRIPTION_KEY">abc123</Param>
              </Parameters>
            </Configuration>
            """;

        var values = CloudConfigParser.Parse(xml);

        Assert.Equal(2, values.Count);
        Assert.Equal("https://api.credinet.co/posprod/", values["API_BASE_URL"]);
        Assert.Equal("abc123", values["SUBSCRIPTION_KEY"]);
    }

    [Fact]
    public void Parse_lee_los_parametros_con_prefijo_de_namespace()
    {
        const string xml = """
            <cfg:Configuration xmlns:cfg="http://schemas.icg.es/hiopos/config">
              <cfg:Parameters>
                <cfg:Param Key="STORE_ID">42</cfg:Param>
              </cfg:Parameters>
            </cfg:Configuration>
            """;

        var values = CloudConfigParser.Parse(xml);

        Assert.Equal("42", Assert.Contains("STORE_ID", values));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("KEY")]
    [InlineData("Key")]
    public void Parse_tolera_la_capitalizacion_del_atributo_Key(string attr)
    {
        var xml = $"""<Configuration><Param {attr}="STORE_ID">9</Param></Configuration>""";

        var values = CloudConfigParser.Parse(xml);

        Assert.Equal("9", values["STORE_ID"]);
    }

    [Fact]
    public void Parse_es_insensible_a_la_capitalizacion_de_la_clave()
    {
        const string xml = """<Configuration><Param Key="store_id">9</Param></Configuration>""";

        var values = CloudConfigParser.Parse(xml);

        Assert.Equal("9", values["STORE_ID"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es xml <<<")]
    [InlineData("<Configuration></Configuration>")]
    public void Parse_devuelve_vacio_sin_lanzar_ante_entrada_invalida(string? xml)
    {
        // Devolver vacío (en vez de lanzar) permite que el llamador decida: es
        // MainActivity quien debe alertar y rechazar la transacción.
        var values = CloudConfigParser.Parse(xml);
        Assert.Empty(values);
    }

    [Fact]
    public void Parse_ignora_los_Param_sin_Key()
    {
        const string xml = """
            <Configuration>
              <Param>huerfano</Param>
              <Param Key="">vacio</Param>
              <Param Key="STORE_ID">7</Param>
            </Configuration>
            """;

        var values = CloudConfigParser.Parse(xml);

        Assert.Single(values);
        Assert.Equal("7", values["STORE_ID"]);
    }

    // ══════════════════════════════════════════════════════════════════════
    // EL BUG DE LA CAPITALIZACION — la tienda 037 reportando por todas
    // ══════════════════════════════════════════════════════════════════════
    //
    // Parse es INSENSIBLE a la capitalizacion (test de arriba, linea 86). Ese era
    // el problema: la tolerancia terminaba en la puerta del almacenamiento.
    //
    // Preferences sobre Android es SharedPreferences, que es SENSIBLE A
    // MAYUSCULAS. Los parametros se guardaban con el nombre tal como lo mando
    // ICG y se leian con las constantes en mayusculas de ICloudConfig. Si ICG
    // escribia "Store_Id", se guardaba en "cloudparam_Store_Id" y se buscaba en
    // "cloudparam_STORE_ID": nunca se encontraba.
    //
    // Y no habia ninguna señal: la linea "Parametros Cloud guardados: 5
    // [API_BASE_URL, Store_Id, ...]" se ve perfecta mientras el valor no lo leia
    // nadie. La caja caia al appsettings y se quedaba con el StoreId HORNEADO —
    // el de la 037 — reportando a nombre de la 037 los creditos de Suba. No se
    // veia al conciliar en la plataforma de Sistecredito, no desde la caja.

    /// <summary>
    /// El nombre con el que CloudLicense se equivoca al escribir STORE_ID. Es la
    /// forma en que llega cuando el tecnico de ICG lo escribe en la hoja.
    /// </summary>
    [Theory]
    [InlineData("Store_Id")]
    [InlineData("STORE_Id")]
    [InlineData("StoreId")]
    [InlineData("storeid")]
    [InlineData("STORE_ID")]
    [InlineData(" store_id ")]
    public void NombreCanonico_deja_el_STORE_ID_de_ICG_igual_a_la_constante(string enviada)
    {
        Assert.Equal(ICloudConfig.StoreId, CloudConfigParser.NombreCanonico(enviada));
    }

    [Fact]
    public void NombreCanonico_hace_que_el_parametro_de_ICG_sea_el_que_se_busca()
    {
        // El recorrido completo: lo que mando ICG -> como se guarda -> como se lee.
        const string xml = """<Configuration><Param Key="Store_Id">607af8e38c91f70001436058</Param></Configuration>""";

        var values = CloudConfigParser.Parse(xml);
        var guardada = "cloudparam_" + CloudConfigParser.NombreCanonico(values.Keys.Single());
        var leida = "cloudparam_" + CloudConfigParser.NombreCanonico(ICloudConfig.StoreId);

        // Antes del arreglo eran "cloudparam_Store_Id" y "cloudparam_STORE_ID":
        // distintas, y la caja nunca encontraba el valor.
        Assert.Equal(leida, guardada);
    }

    /// <summary>
    /// Ningun parametro del contrato puede tener dos escrituras distintas. Si
    /// alguno no fuera canonico, o si una variante cayera en una clave distinta,
    /// la precedencia CloudLicense sobre appsettings se rompe en silencio.
    /// </summary>
    [Theory]
    [InlineData(ICloudConfig.ApiBaseUrl)]
    [InlineData(ICloudConfig.SubscriptionKey)]
    [InlineData(ICloudConfig.StoreId)]
    [InlineData(ICloudConfig.Environment)]
    [InlineData(ICloudConfig.StoreName)]
    [InlineData(ICloudConfig.OtpDestination)]
    [InlineData(ICloudConfig.CertificatePins)]
    [InlineData(ICloudConfig.Frequency)]
    [InlineData(ICloudConfig.Source)]
    [InlineData(ICloudConfig.AuthMethod)]
    [InlineData(ICloudConfig.TimeoutSeconds)]
    [InlineData(ICloudConfig.OtpMaxResends)]
    [InlineData(ICloudConfig.PaymentMeanIdRecaudo)]
    [InlineData(ICloudConfig.PaymentMeanIdVenta)]
    public void Las_claves_del_contrato_de_ICG_son_ya_canonicas(string clave)
    {
        Assert.Equal(clave, CloudConfigParser.NombreCanonico(clave));
    }

    /// <summary>
    /// El STORE_ID de CloudLicense se LEE aunque ICG mande la clave con otra
    /// capitalizacion —que es el defecto que esta clase corrige— pero ya NO define
    /// la tienda de la caja: esa se elige en el terminal.
    ///
    /// Se sigue leyendo porque es lo que permite avisar cuando el POS cree que este
    /// terminal es otra tienda. Lo que este test fija es que la normalizacion del
    /// nombre de la clave llega de punta a punta: si se rompiera, el aviso nunca
    /// aparecería y una discrepancia quedaría invisible otra vez.
    /// </summary>
    [Fact]
    public void El_StoreId_de_CloudLicense_se_lee_aunque_venga_en_cripto()
    {
        var suba = "607d8d208c91f70001439630";
        var cloud = new FakeCloudConfig(new() { ["store_id"] = suba });

        var config = ApiConfig.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Credinet:SubscriptionKey"] = "clave-de-pruebas",
                    ["Credinet:StoreId"] = "607af8e38c91f70001436058", // la 037, horneada
                })
                .Build(),
            cloud,
            storeIdDeLaCaja: "607af8e38c91f70001436058");   // elegida en el terminal

        // Manda la elegida en el terminal. Se compara contra [ApiConfig.Tienda] y no
        // contra ApiConfig.StoreId porque este caso no fija ambiente, o sea sandbox,
        // y ahi el identificador no se le manda a Credinet a proposito (responderia
        // StoreNotFound). Ver [ApiConfig.StoreId].
        Assert.Equal("607af8e38c91f70001436058", config.Tienda.StoreId);

        // ...y la discrepancia se ve, que es para lo que sirve leer el de ICG.
        Assert.Equal(suba, config.Tienda.DeHioPos);
        Assert.True(config.Tienda.DiscrepaConHioPos);
    }

    /// <summary>
    /// Doble de CloudLicense que normaliza el nombre como lo hace el
    /// almacenamiento real, para que el test cubra el encadenado completo.
    /// </summary>
    private sealed class FakeCloudConfig : ICloudConfig
    {
        private readonly Dictionary<string, string> _values;

        public FakeCloudConfig(Dictionary<string, string> values) =>
            _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        public string? Get(string key) =>
            _values.TryGetValue(CloudConfigParser.NombreCanonico(key), out var v) ? v : null;
    }
}
