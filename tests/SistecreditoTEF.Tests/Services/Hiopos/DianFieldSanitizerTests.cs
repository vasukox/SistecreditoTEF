using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// QA B-2: esta lógica estaba duplicada literalmente en [ConfirmacionViewModel] y
/// [ReciboPagoViewModel] (mismo método, misma constante, copiado y pegado) y sin
/// tests, pese a tener reglas de un tercero y consecuencias fiscales.
/// </summary>
public class DianFieldSanitizerTests
{
    [Fact]
    public void Quita_los_guiones_del_GUID()
    {
        var result = DianFieldSanitizer.AuthorizationId(
            "8f14e45f-ceea-467a-9575-4b4b4b4b4b4b", 123);

        Assert.Equal("8f14e45fceea467a95754b4b4b4b4b4b", result);
        Assert.DoesNotContain("-", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Quita_espacios()
    {
        Assert.Equal("abc123", DianFieldSanitizer.AuthorizationId("  abc 123 ", 1));
    }

    [Fact]
    public void Trunca_a_40_caracteres()
    {
        // El manual de HioPos define AuthorizationId como varchar(40).
        var largo = new string('a', 100);

        var result = DianFieldSanitizer.AuthorizationId(largo, 1);

        Assert.Equal(DianFieldSanitizer.AuthorizationIdMaxLength, result.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData(" - - ")]
    public void Sin_identificador_usa_el_consecutivo_con_padding(string? id)
    {
        // La API SIAT/DIAN rechaza vacíos: hay que mandar algo determinista.
        Assert.Equal("000123", DianFieldSanitizer.AuthorizationId(id, 123));
    }

    [Fact]
    public void El_padding_del_fallback_es_de_6_digitos()
    {
        Assert.Equal("000001", DianFieldSanitizer.AuthorizationId(null, 1));
        Assert.Equal("999999", DianFieldSanitizer.AuthorizationId(null, 999999));
    }
}
