using System.Globalization;
using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// Formateo del monto que digita el cajero, en pesos colombianos.
///
/// La garantía crítica: el valor se obtiene contando DÍGITOS, nunca interpretando
/// separadores. El punto es separador de miles en es-CO y separador DECIMAL en
/// cultura invariante, así que la misma cadena "50.000" vale 50.000 o 50 según la
/// cultura activa. En una pantalla de cobro eso es un error de mil veces, silencioso
/// y dependiente de cómo esté configurado el terminal.
/// </summary>
public class MoneyInputTests
{
    // ------------------------------------------------------------------
    // Formateo mientras se escribe
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("5", "5")]
    [InlineData("50", "50")]
    [InlineData("500", "500")]
    [InlineData("5000", "5.000")]
    [InlineData("50000", "50.000")]
    [InlineData("500000", "500.000")]
    [InlineData("5000000", "5.000.000")]
    public void Agrega_separador_de_miles_mientras_se_escribe(string tecleado, string esperado)
    {
        Assert.Equal(esperado, MoneyInput.Format(tecleado));
    }

    [Theory]
    // Ya formateado: reformatear no debe alterarlo (idempotencia).
    [InlineData("50.000")]
    [InlineData("1.234.567")]
    [InlineData("500")]
    public void El_formateo_es_idempotente(string yaFormateado)
    {
        Assert.Equal(yaFormateado, MoneyInput.Format(yaFormateado));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("...")]
    public void Sin_digitos_devuelve_vacio_para_no_tapar_el_placeholder(string? tecleado)
    {
        Assert.Equal(string.Empty, MoneyInput.Format(tecleado));
    }

    // ------------------------------------------------------------------
    // Parseo: la garantía contra el error de mil veces
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("50.000", 50000)]
    [InlineData("500.000", 500000)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("50000", 50000)]
    public void El_punto_NUNCA_se_interpreta_como_decimal(string texto, decimal esperado)
    {
        // Si el punto se leyera como separador decimal, "50.000" daría 50.
        Assert.Equal(esperado, MoneyInput.Parse(texto));
    }

    [Theory]
    [InlineData("es-CO")]
    [InlineData("en-US")]
    [InlineData("de-DE")]   // usa punto como miles y coma como decimal
    [InlineData("tr-TR")]
    public void El_parseo_da_el_MISMO_valor_en_cualquier_cultura(string cultura)
    {
        // Es la garantía central: el resultado no puede depender de la configuración
        // regional del POS.
        var previa = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultura);

            Assert.Equal(50000m, MoneyInput.Parse("50.000"));
            Assert.Equal(50000m, MoneyInput.Parse("50000"));
            Assert.Equal("50.000", MoneyInput.Format("50000"));
            Assert.Equal("50.000", MoneyInput.FormatValue(50000m));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previa;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    public void Sin_digitos_el_valor_es_cero(string? texto)
    {
        Assert.Equal(0m, MoneyInput.Parse(texto));
    }

    // ------------------------------------------------------------------
    // Robustez del tecleo
    // ------------------------------------------------------------------

    [Fact]
    public void Los_ceros_a_la_izquierda_se_descartan()
    {
        // "0050000" pasa si el cajero teclea antes de mover el cursor.
        Assert.Equal(50000m, MoneyInput.Parse("0050000"));
        Assert.Equal("50.000", MoneyInput.Format("0050000"));
    }

    [Fact]
    public void Solo_ceros_se_conserva_como_cero()
    {
        Assert.Equal("0", MoneyInput.OnlyDigits("000"));
        Assert.Equal(0m, MoneyInput.Parse("000"));
    }

    [Fact]
    public void Se_ignoran_los_caracteres_que_no_son_digitos()
    {
        // El teclado numérico de Android permite el separador decimal, y el cajero
        // puede pegar texto.
        Assert.Equal(50000m, MoneyInput.Parse("$ 50.000 COP"));
        Assert.Equal(50000m, MoneyInput.Parse("50,000"));
    }

    [Fact]
    public void Se_acota_la_cantidad_de_digitos()
    {
        // Tecleo accidental sostenido: no debe desbordar ni colgar el formateo.
        var muchos = new string('9', 40);

        var digitos = MoneyInput.OnlyDigits(muchos);

        Assert.True(digitos.Length <= 12, $"Quedaron {digitos.Length} digitos.");
        Assert.True(MoneyInput.Parse(muchos) > 0);
    }

    // ------------------------------------------------------------------
    // Ida y vuelta
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(50000)]
    [InlineData(183707)]
    [InlineData(50943)]
    [InlineData(1)]
    [InlineData(0)]
    public void Formatear_y_reparsear_conserva_el_valor(decimal valor)
    {
        var texto = MoneyInput.FormatValue(valor);
        Assert.Equal(valor, MoneyInput.Parse(texto));
    }
}
