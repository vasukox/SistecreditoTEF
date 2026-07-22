using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Genera el XML del comprobante (MerchantReceipt / CustomerReceipt) que
/// HioPosCloud parsea para imprimir en el POS.
///
/// HU-134 (regla de oro): el MerchantReceipt es un VOUCHER corto que se
/// imprime ADOSADO a la factura original; NO es una factura. Por eso:
///   - NO lleva header de empresa (nada de "PERMODA - SISTECREDITO"),
///     ni RUT/NIT, ni lineas de productos, ni subtotales/IVA/totales.
///   - Un solo CUT_PAPER, al final (nunca en la mitad).
/// Si el voucher parece una factura, HioPosCloud lo interpreta como un
/// documento nuevo y lo reenvia a DIAN (doble envio). Doc §5.
///
/// Alineacion (HU-134): la impresora termica es monoespaciada a
/// [NumCols]=42 columnas. Para que quede "como una factura real":
///   - El titulo y el pie van CENTRADOS.
///   - Cada dato es una fila etiqueta-izquierda / valor-derecha, con el
///     valor pegado al margen derecho (columna 42).
/// El ancho se calcula sobre el texto CRUDO (antes de escapar XML), porque
/// escapar & -> &amp; no cambia el ancho impreso.
///
/// Formatos disponibles: BOLD, NORMAL, DOUBLE_HEIGHT, DOUBLE_WIDTH, UNDERLINE.
/// (No hay formato CENTER/RIGHT: la alineacion se hace con espacios.)
/// </summary>
public class ReceiptBuilder
{
    private const int NumCols = 42;

    /// <summary>Comprobante de CREDITO (POST /create). Voucher, no factura.</summary>
    public string BuildMerchantReceipt(
        DateTime fecha,
        string creditNumber,
        string cliente,
        string documento,
        string valorFinanciado,
        string cuotaInicial,
        string cuotaMensual,
        int plazoCuotas,
        double tasaEfectivaAnual,
        string primeraFechaPago,
        string tienda = "")
    {
        var sep = new string('=', NumCols);
        // La TEA llega como fraccion (ej. 0.2832); el formateador la normaliza.
        var tasa = tasaEfectivaAnual.ToColombianPercentage();

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Receipt numCols=""{NumCols}"">
  {TitleLine("COMPROBANTE DE CREDITO")}
  {TextLine(sep)}
  {Row("Fecha", fecha.ToString("dd/MM/yyyy HH:mm"))}
  {Row("Numero de Credito", creditNumber, bold: true)}
  {Row("Cliente", cliente)}
  {Row("C.C.", documento)}
  {Row("Tienda", tienda)}
  {TextLine(sep)}
  {Row("Valor financiado", valorFinanciado)}
  {Row("Cuota inicial", cuotaInicial)}
  {Row("Cuota mensual", cuotaMensual, bold: true)}
  {Row("Plazo", $"{plazoCuotas} cuotas")}
  {Row("Tasa E.A.", tasa)}
  {Row("Primer pago", primeraFechaPago)}
  {TextLine(sep)}
  {CenterLine("Recibiras un SMS con la clave")}
  {CenterLine("de confirmacion en tu celular.")}
  {TextLine("")}
  {CenterLine("GRACIAS POR SU COMPRA", bold: true)}
  <ReceiptLine type=""CUT_PAPER""/>
</Receipt>";
    }

    /// <summary>
    /// Comprobante de CREDITO para el cliente. Mismo voucher que el del
    /// comercio (ambos cortos, sin datos fiscales).
    /// </summary>
    public string BuildCustomerReceipt(
        DateTime fecha,
        string creditNumber,
        string cliente,
        string documento,
        string valorFinanciado,
        string cuotaInicial,
        string cuotaMensual,
        int plazoCuotas,
        double tasaEfectivaAnual,
        string primeraFechaPago,
        string tienda = "")
        => BuildMerchantReceipt(fecha, creditNumber, cliente, documento,
            valorFinanciado, cuotaInicial, cuotaMensual, plazoCuotas,
            tasaEfectivaAnual, primeraFechaPago, tienda);

    /// <summary>Comprobante de PAGO (POST /payCredit). Voucher, no factura.</summary>
    public string BuildPaymentReceipt(
        string tienda,
        DateTime fecha,
        string cajero,
        string paymentNumber,
        string creditNumber,
        double capitalPagado,
        double saldoRestante,
        string proximoPago,
        double proximoMinimo,
        string cliente = "")
    {
        var sep = new string('=', NumCols);

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Receipt numCols=""{NumCols}"">
  {TitleLine("COMPROBANTE DE PAGO")}
  {TextLine(sep)}
  {Row("Fecha", fecha.ToString("dd/MM/yyyy HH:mm"))}
  {Row("Pago #", paymentNumber, bold: true)}
  {Row("Credito", creditNumber)}
  {Row("Cliente", cliente)}
  {TextLine(sep)}
  {Row("Capital pagado", Money(capitalPagado))}
  {Row("Saldo restante", Money(saldoRestante))}
  {Row("Proximo pago", proximoPago)}
  {Row("Minimo proximo", Money(proximoMinimo))}
  {TextLine(sep)}
  {CenterLine("GRACIAS POR SU PAGO", bold: true)}
  <ReceiptLine type=""CUT_PAPER""/>
</Receipt>";
    }

    // ------------------------------------------------------------------
    // Helpers de layout (monoespaciado, NumCols columnas)
    // ------------------------------------------------------------------

    /// <summary>
    /// Bloque &lt;Formats&gt; OBLIGATORIO en toda linea de texto. HioPos
    /// (manual pag. 25) IGNORA en silencio cualquier &lt;ReceiptLine&gt; de
    /// texto cuyo contenido no este englobado por un &lt;Formats&gt; -> por eso
    /// antes solo salian las lineas en negrita. Siempre BOLD o NORMAL.
    /// </summary>
    private static string FormatBlock(bool bold) =>
        $"<Formats><Format from=\"0\" to=\"{NumCols}\">{(bold ? "BOLD" : "NORMAL")}</Format></Formats>";

    /// <summary>
    /// Construye una &lt;ReceiptLine&gt; de texto COMPLETA en una sola linea
    /// fisica y con &lt;Formats&gt; siempre presente (los dos requisitos del
    /// parser de recibos de HioPos).
    /// </summary>
    private static string Line(string text, bool bold = false) =>
        $"<ReceiptLine type=\"TEXT\">{FormatBlock(bold)}<Text>{Escape(text)}</Text></ReceiptLine>";

    /// <summary>Titulo centrado, en negrita.</summary>
    private static string TitleLine(string text) => Line(Center(text), bold: true);

    /// <summary>Linea de texto simple (separadores, lineas en blanco).</summary>
    private static string TextLine(string text) => Line(text, bold: false);

    /// <summary>Linea de texto centrada (pie del voucher).</summary>
    private static string CenterLine(string text, bool bold = false) => Line(Center(text), bold);

    /// <summary>
    /// Fila etiqueta-izquierda / valor-derecha alineada a NumCols. Si el
    /// valor viene vacio, la fila se omite (dato opcional). Si etiqueta+valor
    /// no caben, se deja un espacio minimo entre ambos.
    /// </summary>
    private static string Row(string label, string value, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        label ??= string.Empty;
        var gap = NumCols - label.Length - value.Length;
        if (gap < 1) gap = 1;
        return Line(label + new string(' ', gap) + value, bold);
    }

    /// <summary>Centra el texto en NumCols columnas con espacios a la izquierda.</summary>
    private static string Center(string text)
    {
        text ??= string.Empty;
        if (text.Length >= NumCols) return text;
        var left = (NumCols - text.Length) / 2;
        return new string(' ', left) + text;
    }

    private static string Money(double pesos) =>
        "$ " + pesos.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Escapa caracteres especiales para XML seguro.</summary>
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
