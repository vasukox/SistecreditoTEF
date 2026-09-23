using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// Parseo de importes del documento de venta de HioPos.
///
/// Los valores de estos tests están capturados del SaleXML real que HioPos envía a
/// su módulo fiscal en el terminal, no inventados.
/// </summary>
public class HioposNumberTests
{
    [Theory]
    // Valores EXACTOS observados en el terminal. Con el parseo anterior
    // (InvariantCulture + NumberStyles.Any) daban x10.000 y sin reportar error.
    [InlineData("57415,0000", 57415)]
    [InlineData("359600,0000", 359600)]
    [InlineData("1,0000", 1)]
    [InlineData("0,0000", 0)]
    public void Parsea_los_importes_de_HioPos_con_coma_decimal(string raw, decimal esperado)
    {
        Assert.Equal(esperado, HioposNumber.ParseDecimal(raw));
    }

    [Theory]
    [InlineData("1234,56", 1234.56)]
    [InlineData("0,5", 0.5)]
    [InlineData("-1234,56", -1234.56)]
    public void La_coma_es_separador_DECIMAL_no_de_miles(string raw, decimal esperado)
    {
        Assert.Equal(esperado, HioposNumber.ParseDecimal(raw));
    }

    [Theory]
    // Por si algún despliegue emitiera punto decimal.
    [InlineData("57415.0000", 57415)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("359600", 359600)]
    [InlineData("-42", -42)]
    public void Tambien_acepta_punto_decimal_y_enteros(string raw, decimal esperado)
    {
        Assert.Equal(esperado, HioposNumber.ParseDecimal(raw));
    }

    [Theory]
    // Si vinieran los dos separadores, el último es el decimal.
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234.567,89", 1234567.89)]
    public void Con_ambos_separadores_el_ultimo_es_el_decimal(string raw, decimal esperado)
    {
        Assert.Equal(esperado, HioposNumber.ParseDecimal(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("abc")]
    public void Devuelve_null_ante_entradas_no_numericas(string? raw)
    {
        Assert.Null(HioposNumber.ParseDecimal(raw));
    }

    [Fact]
    public void ParseDecimalOrZero_devuelve_cero_en_vez_de_null()
    {
        Assert.Equal(0m, HioposNumber.ParseDecimalOrZero("abc"));
        Assert.Equal(57415m, HioposNumber.ParseDecimalOrZero("57415,0000"));
    }

    // ------------------------------------------------------------------
    // Integración con el documento de venta
    // ------------------------------------------------------------------

    [Fact]
    public void El_Total_del_documento_se_calcula_bien_con_los_montos_reales()
    {
        // Header con los importes exactos del terminal: 57415 + 359600 = 417015.
        // Antes del arreglo, Total daba 574150000 + 3596000000 = 4.170.150.000.
        var xml = """
            <Document>
              <Header><HeaderFields>
                <HeaderField Key="SaleId">67f78717-d87e-44ff-9217-24cc0e74cd9c</HeaderField>
                <HeaderField Key="DocumentTypeId">26</HeaderField>
                <HeaderField Key="TaxesAmount">57415,0000</HeaderField>
                <HeaderField Key="NetAmount">359600,0000</HeaderField>
              </HeaderFields></Header>
            </Document>
            """;

        var doc = XmlDocumentReader.Parse(xml);

        Assert.NotNull(doc);
        Assert.Equal(57415m, doc!.TaxesAmount);
        Assert.Equal(359600m, doc.NetAmount);
        Assert.Equal(417015m, doc.Total);
        Assert.Equal("67f78717-d87e-44ff-9217-24cc0e74cd9c", doc.SaleId);
    }

    [Fact]
    public void El_monto_del_medio_de_pago_tambien_se_parsea_bien()
    {
        var xml = """
            <Document>
              <PaymentMeans><PaymentMean>
                <PaymentMeanField Key="PaymentMeanId">2</PaymentMeanField>
                <PaymentMeanField Key="Amount">417015,0000</PaymentMeanField>
              </PaymentMean></PaymentMeans>
            </Document>
            """;

        var doc = XmlDocumentReader.Parse(xml);

        var medio = Assert.Single(doc!.PaymentMeans);
        Assert.Equal(417015m, medio.Amount);
    }

    [Fact]
    public void Un_importe_ausente_no_rompe_el_Total()
    {
        var doc = XmlDocumentReader.Parse("""
            <Document><Header><HeaderFields>
              <HeaderField Key="TaxesAmount">57415,0000</HeaderField>
            </HeaderFields></Header></Document>
            """);

        Assert.Equal(57415m, doc!.TaxesAmount);
        Assert.Equal(0m, doc.NetAmount);
        Assert.Equal(57415m, doc.Total);
    }
}
