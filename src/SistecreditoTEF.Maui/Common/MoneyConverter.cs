namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Conversion entre el formato de HioPosCloud (centavos) y pesos para CREDINET
/// (que recibe pesos).
///
/// QA A-8: esta clase quedó como fachada de compatibilidad. Toda la aritmética
/// vive ahora en [Money], que usa <c>decimal</c> y redondeo comercial.
/// El bug corregido: <c>FromPesosToCents</c> hacía <c>(long)(pesos * 100.0)</c>,
/// que TRUNCA. Con 8,29 pesos devolvía 828 centavos en vez de 829, porque el
/// double 8,29 es en realidad 8,28999999999999914734871708787977695465087890625.
/// El comentario anterior afirmaba "Idempotente, sin perdida": era falso.
///
/// Ejemplos:
///   "50000000" / 100  = 500000.00 pesos  ($500.000 COP)
///   "5000"     / 100  = 50.00 pesos      ($50 COP)
///   "1"        / 100  = 0.01 pesos       ($0,01 COP)
/// </summary>
public static class MoneyConverter
{
    /// <summary>
    /// Centavos (string) -> Pesos (double). Null/unparseable -> null.
    /// </summary>
    public static double? FromCentsToPesos(string? centsString)
    {
        var pesos = Money.FromCentsString(centsString);
        return pesos is null ? null : (double)pesos.Value;
    }

    /// <summary>
    /// Pesos -> Centavos, redondeando al centavo mas cercano (AwayFromZero).
    /// </summary>
    public static long FromPesosToCents(double pesos) => Money.ToCents(pesos);

    /// <inheritdoc cref="FromPesosToCents(double)"/>
    public static long FromPesosToCents(decimal pesos) => Money.ToCents(pesos);
}
