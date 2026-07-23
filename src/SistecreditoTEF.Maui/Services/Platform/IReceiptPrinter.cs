namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973 standalone: servicio de impresion del comprobante de pago
/// cuando la app corre fuera del kiosko de HI-POS (modo launcher).
///
/// Como en standalone NO hay HI-POS que se encargue de imprimir, la app
/// tiene que hacerlo sola. Las POS Android tipicas (Sunmi, PAX, etc.)
/// traen una impresora termica integrada que se accede via SDK del
/// fabricante.
///
/// Estrategia:
///   - SunmiPrinter (principal): usa AIDL de com.sunmi.printerservice
///     (la mayoria de POS HI-POS en Colombia son Sunmi).
///   - PdfReceiptPrinter (fallback): genera un PDF del recibo y abre
///     el share intent para que el cajero imprima/comparta por otro medio.
/// </summary>
public interface IReceiptPrinter
{
    /// <summary>
    /// Nombre legible del printer (para mostrar en UI/logs).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// True si este printer esta disponible en el POS actual (SDK
    /// instalado y operativo).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Imprime el comprobante. Retorna true si se imprimio o se abrio
    /// el share intent, false si fallo.
    /// </summary>
    Task<bool> PrintAsync(StandaloneReceipt receipt);
}

/// <summary>
/// DTO con los datos del comprobante para impresion standalone.
/// </summary>
public record StandaloneReceipt(
    string Tienda,
    DateTime Fecha,
    string Cajero,
    string PaymentNumber,
    string CreditNumber,
    string Cliente,
    string ClienteDocumento,
    decimal CapitalPagado,
    decimal SaldoRestante,
    DateTime ProximoPago,
    decimal ProximoMinimo);