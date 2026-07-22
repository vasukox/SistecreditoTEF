using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Formatea montos en pesos colombianos: $80.000.000 (punto como
/// separador de miles). DRY: cualquier Double/BigDecimal que la UI
/// muestre como dinero usa estas extensiones en vez de formatear
/// in-line.
///
/// Locale es-CO produce el formato colombiano automaticamente.
/// </summary>
public static class MoneyFormatter
{
    private static readonly CultureInfo CoCulture = CultureInfo.GetCultureInfo("es-CO");

    public static string ToColombianCurrency(this double value) =>
        string.Format(CoCulture, "${0:N0}", value);

    public static string ToColombianCurrency(this decimal value) =>
        string.Format(CoCulture, "${0:N0}", value);

    public static string ToColombianCurrency(this long value) =>
        string.Format(CoCulture, "${0:N0}", value);
}
