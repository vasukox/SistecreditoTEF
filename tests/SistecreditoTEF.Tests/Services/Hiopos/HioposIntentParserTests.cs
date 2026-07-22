using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// Tests del parser de Intents HioPosCloud.
/// Verifica que extrae todos los campos del doc §4 (incluidos
/// IsAdvancedPayment, OverPaymentType, etc. que el parser original
/// ignoraba).
/// </summary>
public class HioposIntentParserTests
{
    private readonly HioposIntentParser _parser = new();

    [Fact]
    public void Parse_devuelve_nulls_si_extras_vacios()
    {
        var extras = new Dictionary<string, string?>();

        var tx = _parser.Parse(extras);

        Assert.Null(tx.TransactionType);
        Assert.Null(tx.AmountCents);
        Assert.False(tx.IsAdvancedPayment);
        Assert.Equal(0, tx.OverPaymentType);
    }

    [Fact]
    public void Parse_extrae_campos_basicos()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = "SALE",
            [HioposExtras.TenderType]      = "CREDIT",
            [HioposExtras.CurrencyIso]     = "COP",
            [HioposExtras.LanguageIso]     = "es",
            [HioposExtras.Amount]          = "50000000",
            [HioposExtras.TransactionId]   = "uuid-1234"
        };

        var tx = _parser.Parse(extras);

        Assert.True(tx.IsSale);
        Assert.Equal("COP", tx.CurrencyIso);
        Assert.Equal("uuid-1234", tx.TransactionId);
        Assert.Equal("50000000", tx.AmountCents);
    }

    [Fact]
    public void Parse_extrae_flags_de_pago_avanzado()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.IsAdvancedPayment] = "true",
            [HioposExtras.OverPaymentType]   = "-1"
        };

        var tx = _parser.Parse(extras);

        Assert.True(tx.IsAdvancedPayment);
        Assert.Equal(-1, tx.OverPaymentType);
    }

    [Fact]
    public void Parse_devuelve_null_para_strings_vacios_o_whitespace()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.Amount]    = "   ",
            [HioposExtras.ShopData]  = ""
        };

        var tx = _parser.Parse(extras);

        Assert.Null(tx.AmountCents);
        Assert.Null(tx.ShopData);
    }
}
