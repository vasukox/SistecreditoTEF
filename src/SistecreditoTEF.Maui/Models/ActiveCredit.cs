namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio: Credito activo del cliente.
/// </summary>
public record ActiveCredit(
    string TypeDocument,
    string IdDocument,
    string CreditId,
    int CreditNumber,
    string CreateDate,
    double CreditValue,
    int ArrearsDays,
    double MinimumPayment,
    double TotalPayment,
    double FeeValue,
    string StoreName,
    double Balance,
    string DueDate)
{
    /// <summary>
    /// HU8-973: fecha de vencimiento formateada para UI (dd/MM/yyyy). La API la
    /// devuelve como ISO (2026-08-03T00:00:00); si no parsea, muestra el valor tal cual.
    /// </summary>
    public string DueDateDisplay =>
        DateTime.TryParse(DueDate, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : (DueDate ?? "-");
}
