namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Construye el XML <c>ModifyDocumentResult</c> (doc pag. 23-24) que enriquece
/// el medio de pago Sistecredito del documento de venta con los datos del
/// credito.
///
/// REGLA CRITICA (HU-134): SOLO se tocan <c>PaymentMeans</c>. NUNCA se agregan
/// lineas de producto, datos de empresa ni totales: si el resultado parece un
/// documento nuevo, HioPosCloud lo reenvia a DIAN (doble envio). Por eso este
/// builder emite exclusivamente un &lt;PaymentMean&gt; con sus campos estandar
/// y los &lt;CustomPaymentMeanFields&gt;.
///
/// El <c>PaymentMeanId</c> y el <c>Amount</c> se pasan desde afuera: se toman
/// del documento real (leido por [XmlDocumentReader]) o de configuracion, NO
/// se inventan.
/// </summary>
public class ModifyDocumentResultBuilder
{
    public string Build(
        string paymentMeanId,
        string type,
        string lineNumber,
        string amount,
        string authorizationId,
        string? transactionId,
        IReadOnlyList<(string Key, string Value)> customFields)
    {
        var standard =
            Field("PaymentMeanId", paymentMeanId) +
            Field("Type", type) +
            Field("LineNumber", lineNumber) +
            Field("Amount", amount) +
            Field("AuthorizationId", authorizationId) +
            Field("TransactionId", transactionId);

        var custom = string.Concat(
            customFields.Select(f => CustomField(f.Key, f.Value)));

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<ModifyDocumentResult>
  <PaymentMeans>
    <PaymentMean>
{standard}      <CustomPaymentMeanFields>
{custom}      </CustomPaymentMeanFields>
    </PaymentMean>
  </PaymentMeans>
</ModifyDocumentResult>";
    }

    /// <summary>Campo estandar. Se omite si el valor viene vacio.</summary>
    private static string Field(string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"      <PaymentMeanField Key=\"{Escape(key)}\">{Escape(value)}</PaymentMeanField>\n";

    /// <summary>Campo custom. Se omite si el valor viene vacio.</summary>
    private static string CustomField(string key, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"        <CustomPaymentMeanField Key=\"{Escape(key)}\">{Escape(value)}</CustomPaymentMeanField>\n";

    private static string Escape(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("\r", string.Empty)
            .Replace("\n", " ");
    }
}
