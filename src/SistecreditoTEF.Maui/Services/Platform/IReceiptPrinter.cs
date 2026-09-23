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
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DESGLOSE COMPLETO, NO SOLO EL CAPITAL
/// ─────────────────────────────────────────────────────────────────────────────
/// El comprobante mostraba unicamente <c>CapitalPagado</c>, rotulado "Capital
/// pagado", y NO la plata que el cliente entrego. Son cosas distintas: Credinet
/// reparte el pago entre capital, intereses, mora, aval y cargos, asi que el
/// capital es SIEMPRE menor o igual al total. En caja se leia como que el
/// comprobante decia un monto que no era el cobrado.
///
/// Ahora viajan los cinco componentes. El total no se manda como un campo
/// aparte a proposito: se calcula sumandolos (ver <see cref="TotalPagado"/>), asi
/// no puede quedar un total que no cuadre con su propio desglose.
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
    decimal ProximoMinimo,
    decimal InteresesPagados = 0m,
    decimal MoraPagada = 0m,
    decimal AvalPagado = 0m,
    decimal OtrosCargos = 0m)
{
    /// <summary>
    /// Lo que efectivamente pago el cliente: la suma de todo lo que Credinet
    /// reporta como aplicado. Es el numero que el cajero y el cliente comparan
    /// contra la plata que se entrego.
    /// </summary>
    public decimal TotalPagado =>
        CapitalPagado + InteresesPagados + MoraPagada + AvalPagado + OtrosCargos;
}