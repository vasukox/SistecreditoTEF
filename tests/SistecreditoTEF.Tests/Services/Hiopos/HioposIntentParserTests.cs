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

    // HU8-973 (Fase 2): distinguir SALE vs REFUND en el Intent de HioPos.
    // La accion es la misma (TRANSACTION); lo que cambia es el extra
    // TransactionType. MainActivity enruta segun esto: SALE -> CapturaCedula,
    // REFUND -> CreditosActivos (recaudo).
    [Fact]
    public void Parse_SALE_marca_IsRefund_false()
    {
        var tx = _parser.Parse(new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = "SALE"
        });

        Assert.True(tx.IsSale);
        Assert.False(tx.IsRefund);
    }

    [Fact]
    public void Parse_REFUND_marca_IsRefund_true()
    {
        var tx = _parser.Parse(new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = "REFUND"
        });

        Assert.True(tx.IsRefund);
        Assert.False(tx.IsSale);
    }

    [Fact]
    public void Parse_REFUND_es_case_insensitive()
    {
        // El doc ICG §4 define el enum en MAYUSCULAS, pero no se debe
        // romper si HioPos manda otra capitalizacion (lo mismo que IsSale).
        var tx1 = _parser.Parse(new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = "refund"
        });
        var tx2 = _parser.Parse(new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = "Refund"
        });

        Assert.True(tx1.IsRefund);
        Assert.True(tx2.IsRefund);
    }

    [Fact]
    public void Parse_sin_TransactionType_no_es_REFUND()
    {
        // Por seguridad: si HioPos olvida mandar el extra, NO enrutamos
        // accidentalmente al flujo de recaudo. Mejor caer en el default
        // (SALE) y mostrar el menu, que cobrarse el riesgo de hacer un
        // REFUND no solicitado.
        var tx = _parser.Parse(new Dictionary<string, string?>());

        Assert.False(tx.IsRefund);
        Assert.False(tx.IsSale);
    }
}
