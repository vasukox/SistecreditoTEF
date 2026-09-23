namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Reglas sobre números de documento de identidad.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ EXISTE: EL CLIENTE GENÉRICO DE HioPos
/// ─────────────────────────────────────────────────────────────────────────────
/// El documento de venta de HioPos puede traer un **cliente genérico** —el
/// marcador que el POS usa para ventas anónimas, típicamente
/// <c>222222222222</c> (doce dos) o <c>999999999</c>— además del cliente
/// realmente asignado a la venta.
///
/// Autocompletar la cédula con ese marcador es PEOR que dejar el campo vacío:
/// el cajero ve un número ya puesto, no lo revisa, y valida contra Credinet a
/// una persona que no es la que está comprando. Si ese documento genérico
/// existiera como cliente real en Credinet, se le podría crear un crédito a
/// nombre equivocado.
///
/// Por eso el módulo nunca autocompleta un documento con forma de marcador:
/// deja el campo en blanco para captura manual.
/// </summary>
public static class DocumentNumber
{
    /// <summary>Largo mínimo razonable para una cédula colombiana.</summary>
    private const int MinLength = 5;

    /// <summary>Largo máximo aceptado (cubre NIT y documentos extranjeros).</summary>
    private const int MaxLength = 15;

    /// <summary>
    /// Marcadores conocidos de "cliente genérico" en POS colombianos.
    /// La detección por dígito repetido ya cubre la mayoría; esta lista es para
    /// los que no siguen ese patrón.
    /// </summary>
    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        "123456789",
        "1234567890",
        "0",
        "00000000"
    };

    /// <summary>
    /// Normaliza el documento como lo exige Credinet: solo dígitos, sin puntos,
    /// comas, espacios ni guiones.
    ///
    /// El teclado numérico de Android permite el separador decimal, así que el
    /// cajero puede dejar un '.' en la cédula; un idDocument con separadores hace
    /// que Credinet responda 224 CustomerNotFound.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True si el documento tiene forma de marcador genérico y NO debe usarse
    /// para autocompletar.
    ///
    /// Reglas:
    ///   - vacío o más corto que <see cref="MinLength"/>;
    ///   - más largo que <see cref="MaxLength"/>;
    ///   - todos los dígitos iguales (222222222222, 999999999, 111111111…);
    ///   - presente en <see cref="KnownPlaceholders"/>.
    /// </summary>
    public static bool IsGenericPlaceholder(string? documento)
    {
        var d = Normalize(documento);

        if (d.Length == 0) return true;
        if (d.Length < MinLength) return true;
        if (d.Length > MaxLength) return true;
        if (KnownPlaceholders.Contains(d)) return true;

        // Todos los dígitos iguales: 222222222222, 99999999, 0000000...
        var primero = d[0];
        var todosIguales = true;
        for (var i = 1; i < d.Length; i++)
        {
            if (d[i] == primero) continue;
            todosIguales = false;
            break;
        }
        return todosIguales;
    }

    /// <summary>
    /// True si el documento sirve para autocompletar: normalizable, con largo
    /// plausible y sin forma de marcador genérico.
    /// </summary>
    public static bool IsUsableForAutocomplete(string? documento) =>
        !IsGenericPlaceholder(documento);
}
