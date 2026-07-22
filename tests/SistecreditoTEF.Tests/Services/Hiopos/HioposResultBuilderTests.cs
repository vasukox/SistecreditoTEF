using SistecreditoTEF.Maui.Services.Hiopos;
using HioposResultCodes = SistecreditoTEF.Maui.Services.Hiopos.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// Tests del HioposResultBuilder.
/// B2/B3/B4/B5/B6: verifica que el resultado respeta el wire format
/// exacto que HioPosCloud espera.
/// </summary>
public class HioposResultBuilderTests
{
    private readonly HioposResultBuilder _builder;

    public HioposResultBuilderTests()
    {
        _builder = new HioposResultBuilder(new ReceiptBuilder());
    }

    [Fact]
    public void BuildBehavior_retorna_los_18_flags_en_extras()
    {
        var response = _builder.BuildBehavior(HioposActions.GetBehavior);

        Assert.Equal(HioposResultCodes.Result.OkValue, response.ResultCode);
        Assert.Contains(HioposExtras.SupportsCredit, response.StringExtras.Keys);
        Assert.Contains(HioposExtras.HasCustomParams, response.StringExtras.Keys);
        Assert.Contains(HioposExtras.CanAudit, response.StringExtras.Keys);
        Assert.Contains(HioposExtras.OnlyUseDocumentPath, response.StringExtras.Keys);

        // Valores correctos segun HioposCapabilities (doc §3).
        Assert.Equal("true",  response.StringExtras[HioposExtras.SupportsCredit]);
        Assert.Equal("true",  response.StringExtras[HioposExtras.HasCustomParams]);
        Assert.Equal("true",  response.StringExtras[HioposExtras.CanAudit]);
        Assert.Equal("true",  response.StringExtras[HioposExtras.OnlyUseDocumentPath]);
        Assert.Equal("false", response.StringExtras[HioposExtras.CanChargeCard]);
        Assert.Equal("false", response.StringExtras[HioposExtras.CanPrint]);
    }

    [Fact]
    public void BuildVersion_retorna_extra_Version_no_TransactionResult()
    {
        var response = _builder.BuildVersion(HioposActions.GetVersion, "1.2.3");

        // B4: debe usar HioposExtras.Version
        Assert.True(response.StringExtras.ContainsKey(HioposExtras.Version));
        Assert.Equal("1.2.3", response.StringExtras[HioposExtras.Version]);
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.TransactionResult));
    }

    [Fact]
    public void BuildTransactionAccepted_usa_wire_format_mayusculas()
    {
        var response = _builder.BuildTransactionAccepted(
            merchantReceiptXml: "<Receipt/>",
            customerReceiptXml: "<Receipt/>",
            authorizationId: "credit-1");

        // B2: "ACCEPTED" en MAYUSCULAS, no "Accepted"
        Assert.Equal("ACCEPTED", response.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("credit-1", response.StringExtras[HioposExtras.AuthorizationId]);
        Assert.Equal("<Receipt/>", response.StringExtras[HioposExtras.MerchantReceipt]);
        Assert.Equal("<Receipt/>", response.StringExtras[HioposExtras.CustomerReceipt]);
        Assert.Equal("Sistecredito", response.StringExtras[HioposExtras.CardType]);
    }

    [Fact]
    public void BuildTransactionFailed_incluye_ErrorMessage_no_ErrorCode()
    {
        var response = _builder.BuildTransactionFailed("Cliente no encontrado");

        // B5: solo ErrorMessage + ErrorMessageTitle; sin ErrorCode
        Assert.Equal("FAILED", response.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("Cliente no encontrado", response.StringExtras[HioposExtras.ErrorMessage]);
        Assert.False(response.StringExtras.ContainsKey("ErrorCode"));
    }

    [Fact]
    public void BuildCustomParams_incluye_Logo_como_binario()
    {
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var response = _builder.BuildCustomParams(HioposActions.GetCustomParams, logo);

        Assert.Equal("Sistecredito", response.StringExtras[HioposExtras.Name]);
        Assert.NotNull(response.BinaryExtras);
        Assert.Equal(logo, response.BinaryExtras![HioposExtras.Logo]);
    }

    [Fact]
    public void BuildCanceled_retorna_resultCode_canceled()
    {
        var response = _builder.BuildCanceled(HioposActions.ReadCard);

        Assert.Equal(HioposResultCodes.Result.CanceledValue, response.ResultCode);
    }
}
