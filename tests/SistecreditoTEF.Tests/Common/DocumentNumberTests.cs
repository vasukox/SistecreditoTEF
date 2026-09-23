using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// Reglas del cliente genérico. Autocompletar el marcador de cliente anónimo del
/// POS (222222222222) hace que el cajero valide contra Credinet a una persona que
/// no es la que está comprando.
/// </summary>
public class DocumentNumberTests
{
    // ------------------------------------------------------------------
    // Normalización
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("1.026.260.942", "1026260942")]
    [InlineData("1026260942", "1026260942")]
    [InlineData(" 1026260942 ", "1026260942")]
    [InlineData("1026-260-942", "1026260942")]
    [InlineData("1026,260,942", "1026260942")]
    [InlineData("1026 260 942", "1026260942")]
    public void Normalize_deja_solo_digitos(string entrada, string esperado)
    {
        Assert.Equal(esperado, DocumentNumber.Normalize(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("...")]
    public void Normalize_tolera_entradas_sin_digitos(string? entrada)
    {
        Assert.Equal(string.Empty, DocumentNumber.Normalize(entrada));
    }

    // ------------------------------------------------------------------
    // Detección del cliente genérico
    // ------------------------------------------------------------------

    [Theory]
    // El caso real observado en el terminal: doce dos.
    [InlineData("222222222222")]
    [InlineData("22222222222222")]
    [InlineData("999999999")]
    [InlineData("111111111")]
    [InlineData("00000000")]
    [InlineData("123456789")]
    [InlineData("1234567890")]
    public void Los_marcadores_genericos_se_detectan(string generico)
    {
        Assert.True(DocumentNumber.IsGenericPlaceholder(generico),
            $"'{generico}' deberia detectarse como cliente generico.");
        Assert.False(DocumentNumber.IsUsableForAutocomplete(generico));
    }

    [Theory]
    // La cédula de pruebas que se usa en el POS empieza por 430: NO es genérica y
    // debe autocompletarse.
    [InlineData("430123456")]
    [InlineData("4301234567")]
    [InlineData("1026260942")]
    [InlineData("52123456")]
    [InlineData("900123456")]
    public void Las_cedulas_reales_NO_se_marcan_como_genericas(string cedula)
    {
        Assert.False(DocumentNumber.IsGenericPlaceholder(cedula),
            $"'{cedula}' es una cedula valida y no deberia marcarse como generica.");
        Assert.True(DocumentNumber.IsUsableForAutocomplete(cedula));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]        // demasiado corta
    [InlineData("1234")]       // demasiada corta
    [InlineData("1234567890123456")]  // demasiado larga
    public void Los_documentos_implausibles_no_sirven_para_autocompletar(string? documento)
    {
        Assert.True(DocumentNumber.IsGenericPlaceholder(documento));
    }

    [Fact]
    public void El_generico_se_detecta_aunque_venga_con_separadores()
    {
        // El documento de HioPos podría traerlo formateado.
        Assert.True(DocumentNumber.IsGenericPlaceholder("222.222.222.222"));
        Assert.True(DocumentNumber.IsGenericPlaceholder(" 222222222222 "));
    }

    [Fact]
    public void Un_documento_con_digitos_mezclados_nunca_es_generico()
    {
        // Propiedad general: basta un dígito distinto para que no sea marcador.
        Assert.False(DocumentNumber.IsGenericPlaceholder("222222222223"));
        Assert.False(DocumentNumber.IsGenericPlaceholder("322222222222"));
    }
}
