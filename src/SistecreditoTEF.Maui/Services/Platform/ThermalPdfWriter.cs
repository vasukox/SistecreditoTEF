using System.Globalization;
using System.Text;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// QA (impresión de abonos): genera un PDF de una página con el comprobante en
/// fuente monoespaciada, dimensionado para rollo térmico de 80 mm.
///
/// POR QUÉ EXISTE: [AndroidPrintPrinter] escribía **texto plano** en el
/// ParcelFileDescriptor del trabajo de impresión mientras declaraba
/// <c>PrintContentType.Document</c>. El Android Print Framework espera un PDF
/// en ese descriptor, así que el trabajo salía inválido o en blanco — y el
/// método devolvía <c>true</c> de todas formas. Ese era el motivo real de que
/// "los abonos no imprimen".
///
/// Se escribe el PDF a mano (sin dependencias nuevas) porque el documento es
/// trivial: un solo objeto de página, la fuente Type1 estándar Courier (que
/// todo visor y print service trae) y un único stream de contenido.
///
/// Geometría: 80 mm = 226,77 pt. Courier tiene un ancho de avance de 0,6 em, así
/// que con 42 columnas (las mismas de [ReceiptTextBuilder]) el cuerpo de texto
/// mide 42 × 0,6 × tamaño. Con 8,5 pt son 214,2 pt, que caben en 226,77 con
/// márgenes de ~6 pt a cada lado.
///
/// Es código puro (sin Android): se testea verificando la estructura del PDF.
/// </summary>
public static class ThermalPdfWriter
{
    private const double PointsPerMm = 72.0 / 25.4;   // 2.8346
    private const double PaperWidthMm = 80.0;
    private const double FontSize = 8.5;
    private const double LineHeight = 10.5;
    private const double MarginX = 6.0;
    private const double MarginY = 10.0;

    /// <summary>Ancho de página en puntos PDF (80 mm).</summary>
    public static double PageWidthPoints => PaperWidthMm * PointsPerMm;

    /// <summary>
    /// Construye el PDF. La altura crece con la cantidad de líneas, así el rollo
    /// térmico corta justo después del contenido en vez de avanzar una hoja A4.
    /// </summary>
    public static byte[] Build(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var pageWidth = PageWidthPoints;
        var pageHeight = (MarginY * 2) + (Math.Max(lines.Count, 1) * LineHeight);

        var content = BuildContentStream(lines, pageHeight);
        // Latin-1: Courier con /WinAnsiEncoding cubre los acentos y la ñ del
        // nombre del cliente. UTF-8 saldria con caracteres corruptos.
        var contentBytes = Encoding.Latin1.GetBytes(content);

        var objects = new List<byte[]>
        {
            Latin1("<< /Type /Catalog /Pages 2 0 R >>"),
            Latin1("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Latin1(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.###} {pageHeight:0.###}] " +
                $"/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")),
            Latin1("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"),
            BuildStreamObject(contentBytes)
        };

        return Assemble(objects);
    }

    /// <summary>Operadores de texto PDF: una línea por <c>Td</c> + <c>Tj</c>.</summary>
    private static string BuildContentStream(IReadOnlyList<string> lines, double pageHeight)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n");
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"/F1 {FontSize:0.###} Tf\n"));
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"{LineHeight:0.###} TL\n"));

        // El origen PDF esta abajo-izquierda: se arranca desde arriba.
        var startY = pageHeight - MarginY - FontSize;
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"1 0 0 1 {MarginX:0.###} {startY:0.###} Tm\n"));

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append("T*\n");
            sb.Append('(').Append(EscapePdfString(lines[i] ?? string.Empty)).Append(") Tj\n");
        }

        sb.Append("ET\n");
        return sb.ToString();
    }

    /// <summary>
    /// En un string literal PDF hay que escapar <c>\</c>, <c>(</c> y <c>)</c>;
    /// si no, un nombre de cliente con paréntesis rompe el documento entero.
    /// </summary>
    internal static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(':  sb.Append("\\(");  break;
                case ')':  sb.Append("\\)");  break;
                case '\r': break;
                case '\n': sb.Append(' ');    break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }

    private static byte[] BuildStreamObject(byte[] contentBytes)
    {
        var header = Latin1(string.Create(CultureInfo.InvariantCulture,
            $"<< /Length {contentBytes.Length} >>\nstream\n"));
        var footer = Latin1("\nendstream");

        var buffer = new byte[header.Length + contentBytes.Length + footer.Length];
        header.CopyTo(buffer, 0);
        contentBytes.CopyTo(buffer, header.Length);
        footer.CopyTo(buffer, header.Length + contentBytes.Length);
        return buffer;
    }

    /// <summary>
    /// Ensambla el archivo con la tabla xref. Los offsets se calculan sobre los
    /// BYTES ya codificados (no sobre la longitud del string), porque un acento
    /// desplazaría la tabla y el PDF quedaría corrupto.
    /// </summary>
    private static byte[] Assemble(List<byte[]> objects)
    {
        using var ms = new MemoryStream();

        void WriteAscii(string s)
        {
            var b = Latin1(s);
            ms.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4\n");
        // Comentario binario: marca el archivo como binario para herramientas
        // que transfieren en modo texto.
        ms.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A], 0, 6);

        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            WriteAscii(string.Create(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n"));
            ms.Write(objects[i], 0, objects[i].Length);
            WriteAscii("\nendobj\n");
        }

        var xrefOffset = ms.Position;
        WriteAscii("xref\n");
        WriteAscii(string.Create(CultureInfo.InvariantCulture, $"0 {objects.Count + 1}\n"));
        WriteAscii("0000000000 65535 f \n");
        foreach (var offset in offsets)
            WriteAscii(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");

        WriteAscii("trailer\n");
        WriteAscii(string.Create(CultureInfo.InvariantCulture,
            $"<< /Size {objects.Count + 1} /Root 1 0 R >>\n"));
        WriteAscii("startxref\n");
        WriteAscii(string.Create(CultureInfo.InvariantCulture, $"{xrefOffset}\n"));
        WriteAscii("%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);
}
