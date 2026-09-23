using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// QA A-1 / B-4: enmascarado de datos personales. El proyecto lo documentaba como
/// control de seguridad pero se aplicaba de forma inconsistente, y la variante que
/// existía devolvía intactos los documentos de 4 o menos caracteres.
/// </summary>
public class PiiMaskTests
{
    [Theory]
    [InlineData("1026260942", "******0942")]
    [InlineData("12345", "*2345")]
    public void Document_deja_solo_los_ultimos_4(string doc, string esperado)
    {
        Assert.Equal(esperado, PiiMask.Document(doc));
    }

    [Theory]
    // QA B-4: antes estos se devolvían SIN enmascarar (docId.Length < 4 → return
    // docId), y con longitud exactamente 4 quedaba el documento completo visible.
    [InlineData("1", "*")]
    [InlineData("12", "**")]
    [InlineData("123", "***")]
    [InlineData("1234", "****")]
    public void Document_enmascara_por_completo_los_documentos_cortos(string doc, string esperado)
    {
        Assert.Equal(esperado, PiiMask.Document(doc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Document_tolera_vacios(string? doc)
    {
        Assert.Equal(string.Empty, PiiMask.Document(doc));
    }

    [Fact]
    public void Url_enmascara_la_cedula_de_la_query()
    {
        var url = "https://api.credinet.co/pos/getCreditLimitClient?typeDocument=CC&idDocument=1026260942&storeId=7";

        var masked = PiiMask.Url(url);

        Assert.DoesNotContain("1026260942", masked);
        Assert.Contains("idDocument=******0942", masked);
        // El resto de la URL debe seguir siendo útil para diagnosticar.
        Assert.Contains("getCreditLimitClient", masked);
        Assert.Contains("storeId=7", masked);
    }

    [Fact]
    public void Url_es_idempotente_y_tolera_null()
    {
        Assert.Equal("?", PiiMask.Url(null));
        var once = PiiMask.Url("x?idDocument=1026260942");
        Assert.Equal(once, PiiMask.Url(once));
    }

    [Fact]
    public void Name_deja_solo_iniciales()
    {
        Assert.Equal("J. P.", PiiMask.Name("Juan Perez"));
    }
}
