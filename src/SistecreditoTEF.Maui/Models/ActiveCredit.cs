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

    /// <summary>
    /// Fecha en que se abrio el credito, en dd/MM/yyyy. Cadena vacia si Credinet
    /// no la manda o no se puede interpretar: preferimos no mostrar la fila antes
    /// que mostrar un guion.
    /// </summary>
    public string CreateDateDisplay =>
        DateTime.TryParse(CreateDate, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>¿Se puede mostrar la fecha de apertura?</summary>
    public bool TieneFechaDeApertura => CreateDateDisplay.Length > 0;

    /// <summary>
    /// True si el credito esta en mora. Se expone para la UI: es un dato que
    /// cambia la decision del cajero y explica por que el pago minimo puede no
    /// coincidir con la cuota.
    /// </summary>
    public bool EstaEnMora => ArrearsDays > 0;
}
