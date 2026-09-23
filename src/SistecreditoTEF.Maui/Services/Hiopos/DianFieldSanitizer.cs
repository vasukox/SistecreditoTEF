using System.Globalization;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Normaliza los identificadores que viajan al módulo fiscal de ICG
/// (<c>icg.hioposapifiscal</c>) y de ahí a la DIAN.
///
/// QA B-2: esta lógica estaba DUPLICADA literalmente en
/// [ConfirmacionViewModel] y [ReciboPagoViewModel] (mismo método
/// <c>SanitizeForDian</c>, misma constante, copiado y pegado). Es una función
/// con reglas de un tercero y consecuencias fiscales: tener dos copias garantiza
/// que tarde o temprano divergen. Ahora hay una sola, y con tests.
///
/// Reglas (del manual de HioPos y la API SIAT/DIAN):
///   - <c>AuthorizationId</c> es <c>varchar(40)</c>.
///   - No se aceptan guiones, espacios ni vacíos.
///   - Si no hay identificador, se usa el número consecutivo con padding.
/// </summary>
public static class DianFieldSanitizer
{
    /// <summary>Largo máximo de AuthorizationId según el manual de HioPos.</summary>
    public const int AuthorizationIdMaxLength = 40;

    /// <summary>
    /// Devuelve el identificador alfanumérico sin guiones ni espacios, truncado
    /// a 40 caracteres. Si viene vacío, usa <paramref name="fallbackNumber"/>
    /// con padding a 6 dígitos.
    /// </summary>
    public static string AuthorizationId(string? identifier, int fallbackNumber)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return Fallback(fallbackNumber);

        var clean = identifier
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (clean.Length == 0) return Fallback(fallbackNumber);

        return clean.Length > AuthorizationIdMaxLength
            ? clean[..AuthorizationIdMaxLength]
            : clean;
    }

    private static string Fallback(int number) =>
        number.ToString("D6", CultureInfo.InvariantCulture);
}
