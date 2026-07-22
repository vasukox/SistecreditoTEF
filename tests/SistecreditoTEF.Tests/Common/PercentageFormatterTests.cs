using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Tests.Common;

/// <summary>
/// La TEA de CREDINET llega como fraccion (0.2832 = 28.32%). El formateador
/// la normaliza con una heuristica defensiva (si &lt; 1, x100).
/// </summary>
public class PercentageFormatterTests
{
    [Fact]
    public void Fraccion_se_convierte_a_porcentaje()
    {
        Assert.Equal("28.32%", 0.2832.ToColombianPercentage());
    }

    [Fact]
    public void Ya_en_porcentaje_se_deja_igual()
    {
        // Datos demo usan porcentaje directo (28.32).
        Assert.Equal("28.32%", 28.32.ToColombianPercentage());
    }

    [Fact]
    public void Valor_real_del_log_025_da_25_por_ciento()
    {
        Assert.Equal("25.00%", 0.25.ToColombianPercentage());
    }

    [Fact]
    public void Tasa_muy_baja_se_normaliza_igual()
    {
        Assert.Equal("0.25%", 0.0025.ToColombianPercentage());
    }

    [Fact]
    public void Nulo_muestra_guion()
    {
        double? sinValor = null;
        Assert.Equal("—", sinValor.ToColombianPercentage());
    }
}
