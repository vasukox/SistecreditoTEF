using System.Globalization;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// QA: constructor ÚNICO del texto del comprobante de abono para impresión
/// local (modo standalone / launcher).
///
/// Antes cada printer tenía su propio layout: <c>SunmiPrinter</c> usaba tags
/// inventados (<c>{center}{b}</c>), <c>PdfReceiptPrinter</c> y
/// <c>AndroidPrintPrinter</c> tenían dos formatos distintos, y ninguno coincidía
/// con el voucher que HioPos imprime vía [ReceiptBuilder]. El cliente recibía un
/// comprobante distinto según por dónde saliera.
///
/// Ahora los tres consumen esta clase, con las MISMAS 42 columnas que
/// [ReceiptBuilder.NumCols] usa para el voucher de HioPos, de modo que el
/// comprobante se ve igual en los dos modos (facturación y abono standalone).
///
/// Es código puro (sin Android): se testea en xUnit.
/// </summary>
public static class ReceiptTextBuilder
{
    /// <summary>Mismo ancho que el voucher de HioPos (impresora térmica 80mm).</summary>
    public const int NumCols = 42;

    /// <summary>
    /// Líneas del comprobante, ya alineadas a <see cref="NumCols"/>.
    /// Sin tags de formato: cada printer decide cómo renderizarlas.
    /// </summary>
    public static IReadOnlyList<string> BuildLines(StandaloneReceipt r)
    {
        var sep = new string('=', NumCols);
        var lines = new List<string>
        {
            Center("COMPROBANTE DE PAGO"),
            Center("SISTECREDITO"),
            sep
        };

        AddRow(lines, "Fecha", r.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        AddRow(lines, "Pago #", r.PaymentNumber);
        AddRow(lines, "Credito", r.CreditNumber);
        AddRow(lines, "Cliente", r.Cliente);
        // Cédula COMPLETA: el comprobante se entrega al titular del crédito, que
        // necesita verificar que el abono se aplicó a su documento. Es la práctica
        // habitual en un recibo de pago.
        //
        // (El enmascarado sigue aplicando en logs y en la auditoría, donde el dato
        // no cumple ninguna función y solo agrega exposición.)
        AddRow(lines, "C.C.", DocumentoParaImprimir(r.ClienteDocumento));
        AddRow(lines, "Tienda", r.Tienda);
        AddRow(lines, "Cajero", r.Cajero);

        lines.Add(sep);

        // ─────────────────────────────────────────────────────────────────────
        // PRIMERO LO QUE PAGO EL CLIENTE
        // ─────────────────────────────────────────────────────────────────────
        // Antes la primera —y unica— cifra era "Capital pagado", que es solo la
        // parte que Credinet aplica a capital. El cliente entregaba $100.000 y el
        // comprobante encabezaba con $70.000, sin decir en ningun lado cuanto
        // habia pagado. En caja eso se lee como un comprobante equivocado.
        //
        // El total va arriba porque es el dato que se compara contra la plata, y
        // el desglose va debajo para que se pueda verificar que suma.
        AddRow(lines, "TOTAL PAGADO", FormatMoney(r.TotalPagado));

        // Desglose: solo los conceptos con valor. Imprimir "Mora $ 0" en un abono
        // sin mora agrega ruido y, peor, hace dudar de si hay mora.
        AddRowSiHayValor(lines, "  Abonado a capital", r.CapitalPagado);
        AddRowSiHayValor(lines, "  Intereses", r.InteresesPagados);
        AddRowSiHayValor(lines, "  Mora", r.MoraPagada);
        AddRowSiHayValor(lines, "  Aval", r.AvalPagado);
        AddRowSiHayValor(lines, "  Otros cargos", r.OtrosCargos);

        lines.Add(new string('-', NumCols));
        AddRow(lines, "Saldo restante", FormatMoney(r.SaldoRestante));
        AddRow(lines, "Proximo pago", r.ProximoPago.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        AddRow(lines, "Minimo proximo", FormatMoney(r.ProximoMinimo));
        lines.Add(sep);
        lines.Add(string.Empty);
        lines.Add(Center("GRACIAS POR SU PAGO"));

        return lines;
    }

    /// <summary>Comprobante como texto plano con saltos de línea.</summary>
    public static string BuildText(StandaloneReceipt r) =>
        string.Join(Environment.NewLine, BuildLines(r));

    // ------------------------------------------------------------------
    // Layout monoespaciado
    // ------------------------------------------------------------------

    /// <summary>
    /// Fila etiqueta-izquierda / valor-derecha alineada a NumCols. Si el valor
    /// viene vacío la fila se omite (dato opcional), igual que en el voucher
    /// de HioPos.
    /// </summary>
    private static void AddRow(List<string> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var gap = NumCols - label.Length - value.Length;
        if (gap < 1) gap = 1;
        lines.Add(label + new string(' ', gap) + value);
    }

    /// <summary>
    /// Fila del desglose, solo si el concepto tiene valor. Un cero no se imprime:
    /// en un abono sin mora, "Mora $ 0" no informa y hace dudar.
    /// </summary>
    private static void AddRowSiHayValor(List<string> lines, string label, decimal valor)
    {
        if (valor == 0m) return;
        AddRow(lines, label, FormatMoney(valor));
    }

    private static string Center(string text)
    {
        if (text.Length >= NumCols) return text;
        return new string(' ', (NumCols - text.Length) / 2) + text;
    }

    /// <summary>
    /// Formato de moneda colombiana para impresión: separador de miles con
    /// punto y sin decimales. Cultura invariante fija (QA B-7): el POS puede
    /// tener cualquier locale y el comprobante debe verse igual siempre.
    /// </summary>
    private static string FormatMoney(decimal pesos) =>
        "$ " + pesos.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.');

    /// <summary>
    /// Documento tal como va impreso en el comprobante: normalizado (solo dígitos)
    /// pero completo.
    /// </summary>
    private static string DocumentoParaImprimir(string? doc) =>
        SistecreditoTEF.Maui.Common.DocumentNumber.Normalize(doc);
}
