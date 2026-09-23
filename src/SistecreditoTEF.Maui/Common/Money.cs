using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// QA A-8: ÚNICA fuente de verdad para convertir dinero entre pesos y centavos.
///
/// Antes coexistían tres conversiones con comportamientos distintos:
///   - <c>MoneyConverter.FromPesosToCents</c>: <c>(long)(pesos * 100.0)</c> → TRUNCABA
///     (8,29 pesos daba 828 centavos, perdiendo un centavo).
///   - <c>ConfirmacionViewModel.ToCents</c> y <c>ReciboPagoViewModel.ToCents</c>:
///     <c>Math.Round(pesos * 100)</c> duplicado, y con redondeo bancario (ToEven).
///
/// Reglas de esta clase:
///   1. La aritmética se hace en <see cref="decimal"/>, no en <c>double</c>. El
///      binario flotante no representa exactamente los decimales de dinero:
///      8,29 es en realidad 8,289999999999999147... y truncar pierde el centavo.
///   2. El redondeo es <see cref="MidpointRounding.AwayFromZero"/> (redondeo
///      comercial colombiano), NO el <c>ToEven</c> por defecto de .NET.
///   3. Se formatea siempre con <see cref="CultureInfo.InvariantCulture"/>: el
///      valor viaja a HioPos y a la DIAN, no se muestra al usuario.
/// </summary>
public static class Money
{
    /// <summary>Pesos → centavos, redondeando al centavo más cercano.</summary>
    public static long ToCents(decimal pesos) =>
        (long)Math.Round(pesos * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Sobrecarga para el código que todavía maneja <c>double</c> (modelos de
    /// dominio de Credinet). Convierte a decimal ANTES de multiplicar, para no
    /// arrastrar el error del flotante.
    /// </summary>
    public static long ToCents(double pesos) => ToCents(ToDecimal(pesos));

    /// <summary>Pesos → centavos como string, listo para los extras del Intent.</summary>
    public static string ToCentsString(decimal pesos) =>
        ToCents(pesos).ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="ToCentsString(decimal)"/>
    public static string ToCentsString(double pesos) =>
        ToCents(pesos).ToString(CultureInfo.InvariantCulture);

    /// <summary>Centavos → pesos.</summary>
    public static decimal FromCents(long cents) => cents / 100m;

    /// <summary>
    /// Centavos (string, como llegan de HioPos) → pesos. null si viene vacío o
    /// no es un entero válido.
    /// </summary>
    public static decimal? FromCentsString(string? cents)
    {
        if (string.IsNullOrWhiteSpace(cents)) return null;
        return long.TryParse(cents.Trim(), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out var v)
            ? FromCents(v)
            : null;
    }

    /// <summary>
    /// <c>double</c> → <c>decimal</c> redondeando a 2 decimales. Los modelos de
    /// dominio reciben <c>double</c> del JSON de Credinet; este es el único punto
    /// donde se normaliza el error del flotante.
    /// </summary>
    public static decimal ToDecimal(double pesos) =>
        Math.Round((decimal)pesos, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Formato sin separadores de miles para campos numéricos fiscales
    /// (la factura DIAN no acepta "34,022", espera "34022").
    /// </summary>
    public static string ToFiscalAmount(double pesos) =>
        ToDecimal(pesos).ToString("F0", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="ToFiscalAmount(double)"/>
    public static string ToFiscalAmount(decimal pesos) =>
        pesos.ToString("F0", CultureInfo.InvariantCulture);
}
