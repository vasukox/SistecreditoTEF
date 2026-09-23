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
}
