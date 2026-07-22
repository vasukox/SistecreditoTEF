using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Formatea tasas/porcentajes de CREDINET.
///
/// Segun la documentacion (M-SCL-03 v05, pag. 23 y 49), los campos de
/// porcentaje se retornan como FRACCION DECIMAL:
///   effectiveAnnualRate = 0.283200  -> 28.32% EA
///   interestRate        = 0.020997  -> 2.10% por periodo
///   downPaymentPercentage = 0.10    -> 10.00%
///
/// Heuristica defensiva: si el valor es 0 &lt; v &lt; 1 se trata como
/// fraccion y se multiplica x100; si ya viene &gt;= 1 se asume que llego en
/// porcentaje y se deja igual. Asi cubre tanto la respuesta real de la API
/// (fraccion) como los datos demo (que usan porcentaje, ej. 28.32).
///
/// Caso patologico ignorado a proposito: una TEA &gt; 100% que llegara como
/// fraccion (ej. 1.50 = 150%) no se multiplicaria; en credito de consumo
/// colombiano no existe, por lo que no se cubre.
/// </summary>
public static class PercentageFormatter
{
    public static string ToColombianPercentage(this double rate)
    {
        var value = rate;
        if (value > 0.0 && value < 1.0) value *= 100.0;
        return value.ToString("N2", CultureInfo.InvariantCulture) + "%";
    }

    public static string ToColombianPercentage(this double? rate) =>
        rate.HasValue ? ToColombianPercentage(rate.Value) : "—";
}
