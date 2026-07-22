using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Helpers para manejo de fechas en pantallas de UI.
/// KISS: usa DateTime + CultureInfo para evitar Calendar (mas complejo
/// que en Kotlin porque .NET tiene DateTimeOffset, TimeZoneInfo, etc.).
/// </summary>
public static class DateHelper
{
    private static readonly CultureInfo CoCulture = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>
    /// Calcula la primera fecha estimada de pago como hoy + 30 dias.
    /// CREDINET no devuelve este campo en la respuesta de create;
    /// como asumimos frecuencia 30 (mensual), hoy + 30 dias es
    /// razonable.
    /// Output: "15 de agosto de 2026" (formato dd 'de' MMMM 'de' yyyy,
    /// locale es-CO para que "agosto" salga en espanol).
    /// </summary>
    public static string CalcularPrimeraFechaEstimadaPago(
        DateTime? desde = null,
        int sumarDias = 30)
    {
        var fecha = (desde ?? DateTime.Now).AddDays(sumarDias);
        var formato = CoCulture.DateTimeFormat;
        var mesNombre = formato.GetMonthName(fecha.Month);
        return $"{fecha.Day} de {mesNombre} de {fecha.Year}";
    }
}
