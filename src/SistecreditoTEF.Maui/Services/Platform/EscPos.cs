using System.Text;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Genera el flujo de bytes ESC/POS del comprobante de abono.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ ESC/POS Y NO PDF
/// ─────────────────────────────────────────────────────────────────────────────
/// La impresora del POS es una térmica USB de clase 07 (impresora). Una térmica
/// **no interpreta PDF**: habla ESC/POS, un protocolo de bytes de comando
/// (inicializar, alinear, negrita, avanzar papel, cortar).
///
/// Se confirmó en el terminal capturando el log de HioPos mientras imprimía:
///
///   E/DBG(HioPos): file:/dev/bus/usb/002/006; vid:1155; pid:22304;
///                  product:SOL801V Printer; class:7; subcl:1; proto:2;
///                  endp:0; addr:1; maxpacksize:64;    (bulk OUT)
///   D/UsbDeviceConnectionJNI(HioPos): close
///
/// HioPos abre la impresora con la API USB Host de Android y le escribe directo.
/// Este generador produce lo que hay que escribirle.
///
/// El ancho es de 42 columnas, el mismo que usa [ReceiptBuilder] para el voucher
/// que HioPos imprime en esta impresora: ya está validado por el propio POS.
///
/// Es código puro (sin Android): se testea verificando los bytes emitidos.
/// </summary>
public static class EscPos
{
    // ---- Comandos ESC/POS ----
    private const byte ESC = 0x1B;
    private const byte GS  = 0x1D;
    private const byte LF  = 0x0A;

    /// <summary>ESC @ — inicializa la impresora (limpia formato previo).</summary>
    private static readonly byte[] Initialize = [ESC, 0x40];

    /// <summary>
    /// ESC t 2 — selecciona la tabla de caracteres PC850 (multilingüe), que es la
    /// que trae los acentos y la ñ del español.
    /// </summary>
    private static readonly byte[] SelectCodePage850 = [ESC, 0x74, 0x02];

    /// <summary>ESC a 0 — alineación a la izquierda (el layout ya centra con espacios).</summary>
    private static readonly byte[] AlignLeft = [ESC, 0x61, 0x00];

    /// <summary>ESC d n — avanza n líneas (despeja el texto de la cuchilla antes de cortar).</summary>
    private static byte[] Feed(byte lines) => [ESC, 0x64, lines];

    /// <summary>GS V 66 0 — avanza y hace corte parcial.</summary>
    private static readonly byte[] CutPartial = [GS, 0x56, 0x42, 0x00];

    /// <summary>Líneas de avance antes del corte, para que el comprobante salga completo.</summary>
    private const byte FeedLinesBeforeCut = 4;

    /// <summary>
    /// Construye el flujo completo: inicializar, tabla de caracteres, las líneas
    /// del comprobante, avance y corte.
    /// </summary>
    public static byte[] Build(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        using var ms = new MemoryStream();

        ms.Write(Initialize);
        ms.Write(SelectCodePage850);
        ms.Write(AlignLeft);

        foreach (var line in lines)
        {
            var bytes = EncodeCp850(line ?? string.Empty);
            ms.Write(bytes);
            ms.WriteByte(LF);
        }

        ms.Write(Feed(FeedLinesBeforeCut));
        ms.Write(CutPartial);

        return ms.ToArray();
    }

    /// <summary>
    /// Codifica el texto en PC850.
    ///
    /// Se hace a mano en vez de con <c>Encoding.GetEncoding(850)</c> porque esa
    /// página de códigos no está disponible en .NET moderno sin agregar el paquete
    /// System.Text.Encoding.CodePages. Solo hace falta el subconjunto español, y
    /// hacerlo explícito evita una dependencia y deja el mapeo a la vista.
    ///
    /// Cualquier carácter fuera del mapa se reduce a su equivalente ASCII (o '?'),
    /// para que un nombre inesperado no imprima basura.
    /// </summary>
    internal static byte[] EncodeCp850(string text)
    {
        var result = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            result[i] = c switch
            {
                <= (char)0x7F => (byte)c,          // ASCII pasa directo

                // Minúsculas acentuadas
                'á' => 0xA0, 'é' => 0x82, 'í' => 0xA1, 'ó' => 0xA2, 'ú' => 0xA3,
                'ü' => 0x81, 'ñ' => 0xA4,
                // Mayúsculas acentuadas
                'Á' => 0xB5, 'É' => 0x90, 'Í' => 0xD6, 'Ó' => 0xE0, 'Ú' => 0xE9,
                'Ü' => 0x9A, 'Ñ' => 0xA5,
                // Signos de uso frecuente
                '¿' => 0xA8, '¡' => 0xAD, '°' => 0xF8, 'º' => 0xA7, 'ª' => 0xA6,

                _ => AsciiFold(c)
            };
        }
        return result;
    }

    /// <summary>
    /// Reduce un carácter desconocido a ASCII imprimible. Preferimos una letra sin
    /// tilde antes que un byte que la impresora renderice como símbolo raro.
    /// </summary>
    private static byte AsciiFold(char c) => c switch
    {
        'à' or 'â' or 'ä' or 'ã' or 'å' => (byte)'a',
        'è' or 'ê' or 'ë' => (byte)'e',
        'ì' or 'î' or 'ï' => (byte)'i',
        'ò' or 'ô' or 'ö' or 'õ' => (byte)'o',
        'ù' or 'û' => (byte)'u',
        'ç' => (byte)'c',
        'À' or 'Â' or 'Ä' or 'Ã' => (byte)'A',
        'È' or 'Ê' or 'Ë' => (byte)'E',
        'Ì' or 'Î' or 'Ï' => (byte)'I',
        'Ò' or 'Ô' or 'Ö' or 'Õ' => (byte)'O',
        'Ù' or 'Û' => (byte)'U',
        'Ç' => (byte)'C',
        '–' or '—' => (byte)'-',
        '“' or '”' or '„' => (byte)'"',
        '‘' or '’' => (byte)'\'',
        ' ' => (byte)' ',      // espacio duro
        _ => (byte)'?'
    };

    /// <summary>
    /// Texto que se enviaría, reconstruido desde los bytes. Solo para diagnóstico
    /// en tests; no se usa en producción.
    /// </summary>
    internal static string DecodeForDiagnostics(byte[] bytes) =>
        string.Concat(bytes.Select(b => b is >= 0x20 and <= 0x7E ? ((char)b).ToString() : "."));
}
