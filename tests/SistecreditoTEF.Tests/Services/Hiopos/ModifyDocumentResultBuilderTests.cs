using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Hiopos;

/// <summary>
/// HU-134: el ModifyDocumentResult SOLO debe tocar PaymentMeans. Si incluyera
/// lineas de producto o datos de empresa, HioPosCloud lo trataria como un
/// documento nuevo y lo reenviaria a DIAN.
/// </summary>
public class ModifyDocumentResultBuilderTests
{
    private static string Build() =>
        new ModifyDocumentResultBuilder().Build(
            paymentMeanId: "7",
            type: "0",
            lineNumber: "1",
            amount: "29900",
            authorizationId: "fc69776b-90ad-ce46",
            transactionId: "TX-555",
            customFields: new[]
            {
                ("CreditId", "fc69776b-90ad-ce46"),
                ("CreditNumber", "000596"),
                ("TEA", "0.2512"),
                ("Fees", "1"),
            });

    [Fact]
    public void Solo_modifica_payment_means_no_lineas()
    {
        var xml = Build();

        Assert.Contains("<PaymentMeans>", xml);
        Assert.DoesNotContain("<Line>", xml);
        Assert.DoesNotContain("<LineField", xml);
        Assert.DoesNotContain("HeaderField", xml);
    }

    [Fact]
    public void Incluye_payment_mean_id_y_campos_estandar()
    {
        var xml = Build();

        Assert.Contains("<PaymentMeanField Key=\"PaymentMeanId\">7</PaymentMeanField>", xml);
        Assert.Contains("<PaymentMeanField Key=\"AuthorizationId\">fc69776b-90ad-ce46</PaymentMeanField>", xml);
    }

    [Fact]
    public void Incluye_custom_fields_del_credito()
    {
        var xml = Build();

        Assert.Contains("<CustomPaymentMeanField Key=\"CreditNumber\">000596</CustomPaymentMeanField>", xml);
        Assert.Contains("<CustomPaymentMeanField Key=\"TEA\">0.2512</CustomPaymentMeanField>", xml);
    }

    [Fact]
    public void Omite_campos_opcionales_vacios()
    {
        var xml = new ModifyDocumentResultBuilder().Build(
            paymentMeanId: "7",
            type: "0",
            lineNumber: "1",
            amount: "29900",
            authorizationId: "cred-1",
            transactionId: null, // sin TransactionId
            customFields: System.Array.Empty<(string, string)>());

        Assert.DoesNotContain("TransactionId", xml);
    }
}
