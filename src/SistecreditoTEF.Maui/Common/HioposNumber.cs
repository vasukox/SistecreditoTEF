using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Parseo de los valores numéricos del documento de venta de HioPosCloud.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL PROBLEMA QUE RESUELVE
/// ─────────────────────────────────────────────────────────────────────────────
/// HioPos emite los importes con **coma como separador decimal y cuatro
/// decimales**, sin separador de miles. Capturado del propio SaleXML que HioPos
/// envía a su módulo fiscal en el terminal:
///
///   TaxesAmount = 57415,0000
///   NetAmount   = 359600,0000       ← seis cifras SIN separador de miles
///   CurrencyExchangeRate = 1,0000
///
/// El parseo anterior era:
///
///   decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out v)
///
/// Con <see cref="CultureInfo.InvariantCulture"/> la coma es separador de MILES, y
/// <c>NumberStyles.Any</c> lo permite. Resultado medido:
///
///   "57415,0000"   → 574150000     (esperado 57415)      x10.000
///   "359600,0000"  → 3596000000    (esperado 359600)     x10.000
///   "1,0000"       → 10000         (esperado 1)
///
/// Y lo peor: <c>TryParse</c> devolvía <c>true</c>. No había error, solo un número
/// diez mil veces mayor, en silencio. Ese valor alimenta
/// <c>SaleDocument.Total</c>, que es el monto con el que se precarga el crédito
/// cuando el Intent no trae el extra <c>Amount</c>.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// REGLA APLICADA
/// ─────────────────────────────────────────────────────────────────────────────
/// Como HioPos NO usa separador de miles (359600 va sin puntos ni comas), un
/// separador único siempre es el DECIMAL. Si aparecieran los dos, el último es el
/// decimal y el otro se descarta como agrupación.
/// </summary>
public static class HioposNumber
{
    /// <summary>
    /// Convierte un valor numérico de HioPos a <see cref="decimal"/>.
    /// null si viene vacío o no es un número reconocible.
    /// </summary>
    public static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();

        var ultimoPunto = s.LastIndexOf('.');
        var ultimaComa = s.LastIndexOf(',');

        string normalizado;

        if (ultimoPunto >= 0 && ultimaComa >= 0)
        {
            // Vienen los dos: el ÚLTIMO es el decimal, el otro es agrupación.
            var posDecimal = Math.Max(ultimoPunto, ultimaComa);
            var entero = s[..posDecimal].Replace(".", string.Empty, StringComparison.Ordinal)
                                        .Replace(",", string.Empty, StringComparison.Ordinal);
            var fraccion = s[(posDecimal + 1)..];
            normalizado = entero + "." + fraccion;
        }
        else if (ultimaComa >= 0)
        {
            // Solo coma: es el separador decimal (HioPos no agrupa).
            // Si hubiera varias comas, la última manda y las otras se descartan.
            var entero = s[..ultimaComa].Replace(",", string.Empty, StringComparison.Ordinal);
            normalizado = entero + "." + s[(ultimaComa + 1)..];
        }
        else if (ultimoPunto >= 0)
        {
            var entero = s[..ultimoPunto].Replace(".", string.Empty, StringComparison.Ordinal);
            normalizado = entero + "." + s[(ultimoPunto + 1)..];
        }
        else
        {
            normalizado = s;
        }

        // Float (no Any): ya normalizamos la agrupación a mano, así que aceptar
        // separadores de miles aquí volvería a abrir la puerta al mismo error.
        return decimal.TryParse(
            normalizado,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    /// <summary>Igual que <see cref="ParseDecimal"/>, con valor por defecto.</summary>
    public static decimal ParseDecimalOrZero(string? raw) => ParseDecimal(raw) ?? 0m;
}
