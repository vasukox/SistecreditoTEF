using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

public class MoneyConverterTests
{
    [Fact]
    public void FromCentsToPesos_50000000_returns_500000()
    {
        var pesos = MoneyConverter.FromCentsToPesos("50000000");
        Assert.Equal(500000.0, pesos);
    }

    [Fact]
    public void FromCentsToPesos_5000_returns_50()
    {
        var pesos = MoneyConverter.FromCentsToPesos("5000");
        Assert.Equal(50.0, pesos);
    }

    [Fact]
    public void FromCentsToPesos_1_returns_001()
    {
        var pesos = MoneyConverter.FromCentsToPesos("1");
        Assert.Equal(0.01, pesos);
    }

    [Fact]
    public void FromCentsToPesos_null_returns_null()
    {
        Assert.Null(MoneyConverter.FromCentsToPesos(null));
        Assert.Null(MoneyConverter.FromCentsToPesos(""));
        Assert.Null(MoneyConverter.FromCentsToPesos("abc"));
    }

    [Fact]
    public void FromPesosToCents_500000_returns_50000000()
    {
        Assert.Equal(50_000_000L, MoneyConverter.FromPesosToCents(500_000.0));
    }
}
