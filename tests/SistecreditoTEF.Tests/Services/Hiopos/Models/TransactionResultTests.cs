using SistecreditoTEF.Maui.Services.Hiopos.Models;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos.Models;

/// <summary>
/// Tests de TransactionResult.ToWire() - V12 fix.
/// </summary>
public class TransactionResultTests
{
    [Theory]
    [InlineData(TransactionResult.Accepted,      "ACCEPTED")]
    [InlineData(TransactionResult.Failed,        "FAILED")]
    [InlineData(TransactionResult.UnknownResult, "UNKNOWN_RESULT")]
    public void ToWire_retorna_wire_format_exacto(TransactionResult value, string expected)
    {
        Assert.Equal(expected, value.ToWire());
    }

    [Fact]
    public void ToWire_para_enum_invalido_lanza_excepcion()
    {
        var invalido = (TransactionResult)999;
        Assert.Throws<System.ArgumentOutOfRangeException>(() => invalido.ToWire());
    }
}
