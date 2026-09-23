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
/// escapar el ampersand (<c>&amp;</c> pasa a <c>&amp;amp;</c>) no cambia el ancho
/// impreso.
///
/// Formatos disponibles: BOLD, NORMAL, DOUBLE_HEIGHT, DOUBLE_WIDTH, UNDERLINE.
/// (No hay formato CENTER/RIGHT: la alineacion se hace con espacios.)
/// </summary>
public class ReceiptBuilder
{
    private const int NumCols = 42;

    /// <summary>
    /// Comprobante de CREDITO (POST /create). Voucher, no factura.
    /// </summary>
    /// <param name="otpDestination">
    /// Canal por el que Credinet envia la clave: 1 = WhatsApp, 0 = SMS.
    /// QA M-15: el texto del voucher decia SIEMPRE "Recibiras un SMS", pero el
    /// canal confirmado por Sistecredito para test y produccion es WhatsApp
    /// (<c>OtpDestination=1</c>, que es lo que trae appsettings.json). El cliente
    /// se iba esperando un SMS que nunca llegaba.
    /// </param>
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
        string tienda = "",
        int otpDestination = 1)
    {
        var sep = new string('=', NumCols);
        // La TEA llega como fraccion (ej. 0.2832); el formateador la normaliza.
        var tasa = tasaEfectivaAnual.ToColombianPercentage();
        var canal = otpDestination == 1 ? "WhatsApp" : "SMS";

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Receipt numCols=""{NumCols}"">
  {TitleLine("COMPROBANTE DE CREDITO")}
  {TextLine(sep)}
  {Row("Fecha", fecha.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture))}
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
  {CenterLine($"Recibiras un {canal} con la clave")}
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
        string tienda = "",
        int otpDestination = 1)
        => BuildMerchantReceipt(fecha, creditNumber, cliente, documento,
            valorFinanciado, cuotaInicial, cuotaMensual, plazoCuotas,
            tasaEfectivaAnual, primeraFechaPago, tienda, otpDestination);

    /// <summary>
    /// Comprobante de PAGO (POST /payCredit). Voucher, no factura.
    ///
    /// Lleva el TOTAL pagado arriba y el desglose debajo. Antes la unica cifra era
    /// <c>capitalPagado</c>, que es solo la parte aplicada a capital y por lo tanto
    /// NO es la plata que entrego el cliente: Credinet reparte el abono entre
    /// capital, intereses, mora, aval y cargos. El comprobante encabezaba con un
    /// monto menor al cobrado y en caja se leia como un error.
    ///
    /// El total ya se calculaba en [ReciboPagoViewModel] para devolverselo al POS
    /// —o sea que el dato estaba— pero al voucher solo le llegaba el capital.
    /// </summary>
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
        string cliente = "",
        double interesesPagados = 0,
        double moraPagada = 0,
        double avalPagado = 0,
        double otrosCargos = 0)
    {
        var sep = new string('=', NumCols);
        var subSep = new string('-', NumCols);

        // Se suma aca y no se recibe como parametro: un total que llega aparte
        // puede no cuadrar con su propio desglose.
        var totalPagado = capitalPagado + interesesPagados + moraPagada
                        + avalPagado + otrosCargos;

        // Solo los conceptos con valor. "Mora $ 0" en un abono sin mora no informa
        // y hace dudar de si hay mora.
        var desglose = string.Concat(
            RowSiHayValor("  Abonado a capital", capitalPagado),
            RowSiHayValor("  Intereses", interesesPagados),
            RowSiHayValor("  Mora", moraPagada),
            RowSiHayValor("  Aval", avalPagado),
            RowSiHayValor("  Otros cargos", otrosCargos));

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Receipt numCols=""{NumCols}"">
  {TitleLine("COMPROBANTE DE PAGO")}
  {TextLine(sep)}
  {Row("Fecha", fecha.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture))}
  {Row("Pago #", paymentNumber, bold: true)}
  {Row("Credito", creditNumber)}
  {Row("Cliente", cliente)}
  {TextLine(sep)}
  {Row("TOTAL PAGADO", Money(totalPagado), bold: true)}
{desglose}  {TextLine(subSep)}
  {Row("Saldo restante", Money(saldoRestante))}
  {Row("Proximo pago", proximoPago)}
  {Row("Minimo proximo", Money(proximoMinimo))}
  {TextLine(sep)}
  {CenterLine("GRACIAS POR SU PAGO", bold: true)}
  <ReceiptLine type=""CUT_PAPER""/>
</Receipt>";
    }

    /// <summary>Fila del desglose, o cadena vacia si el concepto no tiene valor.</summary>
    private string RowSiHayValor(string label, double valor) =>
        valor == 0 ? string.Empty : "  " + Row(label, Money(valor)) + "\n";

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
