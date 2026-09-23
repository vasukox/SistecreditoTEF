using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Platform;

/// <summary>
/// Diagnóstico de la estructura del payload de configuración de CloudLicense.
///
/// La garantía que estos tests protegen es la más importante de esta utilidad:
/// **no filtrar valores**. Uno de los parámetros es la credencial de Azure APIM
/// de producción, y el diagnóstico se imprime en logcat.
/// </summary>
public class CloudConfigDiagnosticoTests
{
    private const string Secreto = "88dec4b8617c4644a239a8af283dc742";

    [Fact]
    public void NUNCA_incluye_valores_de_los_parametros()
    {
        var xml = $"""
            <Configuration><Parameters>
              <Param Key="SUBSCRIPTION_KEY">{Secreto}</Param>
              <Param Key="STORE_ID">7</Param>
            </Parameters></Configuration>
            """;

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.DoesNotContain(Secreto, descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain("7", descripcion.Replace("len=", "").Replace("x7", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void NUNCA_incluye_valores_de_atributos()
    {
        var xml = $"""<Configuration><Parameter name="SUBSCRIPTION_KEY" value="{Secreto}" /></Configuration>""";

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.DoesNotContain(Secreto, descripcion, StringComparison.Ordinal);
        // Pero SÍ reporta los nombres de atributo, que es lo que hace falta para
        // adaptar el parseo.
        Assert.Contains("name", descripcion, StringComparison.Ordinal);
        Assert.Contains("value", descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void Reporta_los_nombres_de_elementos_y_la_jerarquia()
    {
        var xml = """
            <Configuration><Parameters>
              <Param Key="A">1</Param>
              <Param Key="B">2</Param>
            </Parameters></Configuration>
            """;

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.Contains("root=Configuration", descripcion, StringComparison.Ordinal);
        Assert.Contains("Parameters", descripcion, StringComparison.Ordinal);
        // Dos <Param> en el mismo nivel se reportan con su multiplicidad.
        Assert.Contains("Paramx2", descripcion, StringComparison.Ordinal);
        Assert.Contains("Key", descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void Marca_los_namespaces_sin_volcar_la_URI()
    {
        var xml = """
            <Configuration xmlns="http://schemas.icg.es/hiopos/config-super-larga">
              <Parameters><Param Key="A">1</Param></Parameters>
            </Configuration>
            """;

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.Contains("@ns", descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain("schemas.icg.es", descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void Detecta_que_el_payload_no_es_XML_y_da_el_perfil_de_caracteres()
    {
        // Si ICG entregara pares clave=valor en lugar de XML, el perfil lo delata:
        // muchos '=' y saltos de línea, ningún '<'.
        var noXml = $"SUBSCRIPTION_KEY={Secreto}\nSTORE_ID=7\nENVIRONMENT=production";

        var descripcion = CloudConfigParser.DescribeStructure(noXml);

        Assert.Contains("NO-ES-XML", descripcion, StringComparison.Ordinal);
        Assert.Contains("=:3", descripcion, StringComparison.Ordinal);
        Assert.Contains("nl:2", descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain(Secreto, descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void Detecta_JSON_por_el_perfil_de_llaves()
    {
        var json = $"{{\"SUBSCRIPTION_KEY\":\"{Secreto}\",\"STORE_ID\":\"7\"}}";

        var descripcion = CloudConfigParser.DescribeStructure(json);

        Assert.Contains("NO-ES-XML", descripcion, StringComparison.Ordinal);
        Assert.Contains("{}:2", descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain(Secreto, descripcion, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "payload=null")]
    [InlineData("", "payload=vacio")]
    [InlineData("   ", "solo espacios")]
    public void Tolera_payloads_vacios(string? xml, string esperado)
    {
        Assert.Contains(esperado, CloudConfigParser.DescribeStructure(xml), StringComparison.Ordinal);
    }

    [Fact]
    public void Reporta_las_hojas_y_su_longitud_pero_no_su_contenido()
    {
        var xml = $"""<Cfg><A>{Secreto}</A><B></B></Cfg>""";

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.Contains("hojas=2", descripcion, StringComparison.Ordinal);
        Assert.Contains("conTexto=1", descripcion, StringComparison.Ordinal);
        Assert.Contains($"lenMax={Secreto.Length}", descripcion, StringComparison.Ordinal);
        Assert.DoesNotContain(Secreto, descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void No_explota_con_un_XML_muy_anidado()
    {
        var xml = string.Concat(Enumerable.Range(0, 40).Select(i => $"<n{i}>"))
                + "x"
                + string.Concat(Enumerable.Range(0, 40).Reverse().Select(i => $"</n{i}>"));

        var descripcion = CloudConfigParser.DescribeStructure(xml);

        Assert.Contains("root=n0", descripcion, StringComparison.Ordinal);
        // Se corta la profundidad para no generar un log gigante.
        Assert.DoesNotContain("n30", descripcion, StringComparison.Ordinal);
    }
}
