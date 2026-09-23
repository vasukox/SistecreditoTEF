using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// QA A-8: la conversión pesos→centavos estaba duplicada en tres lugares con dos
/// comportamientos distintos, y la de [MoneyConverter] TRUNCABA en vez de
/// redondear, perdiendo un centavo.
///
/// Por qué el test viejo no lo detectaba: <c>MoneyConverterTests</c> solo probaba
/// 500.000,0, un valor exactamente representable en binario donde truncar y
/// redondear coinciden. Cobertura sin casos límite da confianza falsa.
/// </summary>
public class MoneyTests
{
    [Theory]
    // Estos tres son los que fallaban: el double no representa exactamente el
    // decimal, así que (long)(x * 100.0) truncaba hacia abajo.
    [InlineData(8.29, 829L)]
    [InlineData(0.29, 29L)]
    [InlineData(70.07, 7007L)]
    // Valores "redondos": ya funcionaban, se conservan como regresión.
    [InlineData(500000.0, 50000000L)]
    [InlineData(1234.56, 123456L)]
    [InlineData(19999.99, 1999999L)]
    [InlineData(0.0, 0L)]
    public void ToCents_redondea_no_trunca(double pesos, long esperado)
    {
        Assert.Equal(esperado, Money.ToCents(pesos));
    }

    [Theory]
    [InlineData(8.29, 829L)]
    [InlineData(0.005, 1L)]      // AwayFromZero, no el ToEven por defecto de .NET
    [InlineData(0.015, 2L)]      // con ToEven daría 1
    [InlineData(0.025, 3L)]      // con ToEven daría 2
    public void ToCents_decimal_usa_redondeo_comercial(decimal pesos, long esperado)
    {
        Assert.Equal(esperado, Money.ToCents(pesos));
    }

    [Fact]
    public void MoneyConverter_y_Money_coinciden_siempre()
    {
        // La divergencia entre las dos conversiones era el defecto: una truncaba y
        // la otra redondeaba, así que el mismo monto podía viajar al POS y a la
        // DIAN con un centavo de diferencia según qué camino lo formateara.
        for (var cents = 0L; cents <= 20_000L; cents++)
        {
            var pesos = cents / 100.0;
            Assert.Equal(Money.ToCents(pesos), MoneyConverter.FromPesosToCents(pesos));
        }
    }

    [Fact]
    public void Ida_y_vuelta_conserva_el_valor()
    {
        for (var cents = 0L; cents <= 50_000L; cents += 7)
        {
            var pesos = Money.FromCents(cents);
            Assert.Equal(cents, Money.ToCents(pesos));
        }
    }

    [Theory]
    [InlineData("50000000", 500000)]
    [InlineData("1", 0.01)]
    [InlineData("0", 0)]
    public void FromCentsString_parsea_los_centavos_de_HioPos(string cents, decimal esperado)
    {
        Assert.Equal(esperado, Money.FromCentsString(cents));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.5")]     // HioPos manda enteros; un decimal es entrada inválida
    public void FromCentsString_devuelve_null_ante_entrada_invalida(string? cents)
    {
        Assert.Null(Money.FromCentsString(cents));
    }

    [Fact]
    public void ToFiscalAmount_no_lleva_separador_de_miles()
    {
        // La factura fiscal DIAN no acepta "34,022": espera "34022".
        Assert.Equal("34022", Money.ToFiscalAmount(34022.0));
        Assert.Equal("1500000", Money.ToFiscalAmount(1_500_000.0));
    }
}
