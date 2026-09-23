using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Formateo y parseo del monto que el cajero digita, en formato de pesos
/// colombianos (separador de miles con punto, sin decimales).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ NO SE BINDEA UN decimal DIRECTAMENTE
/// ─────────────────────────────────────────────────────────────────────────────
/// La tentación es dejar el <c>Entry</c> bindeado a un <c>decimal</c> con
/// <c>Mode=TwoWay</c> y formatear el texto con puntos. Es peligroso: al reparsear,
/// el punto es separador de MILES en es-CO pero separador DECIMAL en cultura
/// invariante. La misma cadena "50.000" vale 50.000 o 50 según la cultura activa
/// en el dispositivo.
///
/// En una pantalla de cobro eso significa cobrar $50 en vez de $50.000: un error de
/// mil veces, silencioso, y dependiente de la configuración del POS.
///
/// Por eso el campo se maneja como TEXTO y el valor se obtiene contando DÍGITOS:
/// sin ambigüedad de cultura, sin importar cómo esté configurado el terminal.
///
/// Los montos se manejan en pesos enteros. Los importes que devuelve Credinet en
/// los créditos observados son enteros, y el teclado numérico de un POS no debería
/// pedirle centavos al cajero.
/// </summary>
public static class MoneyInput
{
    /// <summary>Tope de dígitos aceptados: evita desbordes por tecleo accidental.</summary>
    private const int MaxDigits = 12;

    /// <summary>
    /// Deja solo los dígitos de lo que el cajero escribió. Es la fuente de verdad
    /// del valor: no interpreta puntos ni comas como separadores decimales.
    /// </summary>
    public static string OnlyDigits(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!char.IsDigit(c)) continue;
            if (sb.Length >= MaxDigits) break;
            sb.Append(c);
        }

        // Se quitan los ceros a la izquierda para que "0050000" quede "50000",
        // pero se conserva un "0" si el cajero escribió solo ceros.
        var digits = sb.ToString().TrimStart('0');
        return digits.Length == 0 && sb.Length > 0 ? "0" : digits;
    }

    /// <summary>
    /// Valor numérico de lo digitado, en pesos. Cero si no hay dígitos.
    /// </summary>
    public static decimal Parse(string? raw)
    {
        var digits = OnlyDigits(raw);
        if (digits.Length == 0) return 0m;
        return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0m;
    }

    /// <summary>
    /// Formatea lo digitado con separador de miles: "50000" → "50.000".
    /// Cadena vacía si no hay dígitos, para que el placeholder siga visible.
    /// </summary>
    public static string Format(string? raw)
    {
        var digits = OnlyDigits(raw);
        return digits.Length == 0 ? string.Empty : FormatValue(Parse(digits));
    }

    /// <summary>Formatea un valor numérico: 50000 → "50.000".</summary>
    public static string FormatValue(decimal value)
    {
        // Se usa cultura invariante y se sustituye la coma por punto, para no depender
        // de la configuración regional del terminal.
        return Math.Truncate(value)
            .ToString("#,##0", CultureInfo.InvariantCulture)
            .Replace(',', '.');
    }
}
