namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Conversion entre el formato de HioPosCloud (centavos) y pesos dobles
/// para CREDINET (que recibe pesos).
///
/// NUEVO en .NET: el Kotlin original no lo necesitaba porque no tenia
/// integracion con HioPosCloud. CREDINET trabaja en PESOS; HioPosCloud
/// envia CENTAVOS (string con ultimos 2 digitos como decimales).
///
/// Ejemplos:
///   "50000000" / 100  = 500000.00 pesos  ($500.000 COP)
///   "5000"     / 100  = 50.00 pesos      ($50 COP)
///   "1"        / 100  = 0.01 pesos      ($0,01 COP)
/// </summary>
public static class MoneyConverter
{
    /// <summary>
    /// Centavos (string) -> Pesos (double). Null/unparseable -> null.
    /// </summary>
    public static double? FromCentsToPesos(string? centsString)
    {
        if (string.IsNullOrWhiteSpace(centsString)) return null;
        if (!long.TryParse(centsString, out var cents)) return null;
        return cents / 100.0;
    }

    /// <summary>
    /// Pesos (double) -> Centavos (long). Idempotente, sin perdida.
    /// </summary>
    public static long FromPesosToCents(double pesos) =>
        (long)(pesos * 100.0);
}
