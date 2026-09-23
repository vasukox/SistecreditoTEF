using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// QA (impresión de abonos): cadena de impresión con fallback REAL en runtime.
///
/// EL BUG QUE CORRIGE: antes el printer se elegía UNA vez, al resolver el DI:
/// <code>
///   if (sunmi.IsAvailable) return sunmi;
///   if (android.IsAvailable) return android;
///   return pdf;
/// </code>
/// Dos consecuencias:
///   1. Si el elegido fallaba al imprimir, NO había fallback: el abono no salía
///      y nadie se enteraba.
///   2. <c>AndroidPrintPrinter.IsAvailable</c> devuelve true en cualquier
///      dispositivo Android (PrintManager siempre existe), así que
///      <c>PdfReceiptPrinter</c> era código muerto: jamás se alcanzaba.
///
/// Ahora se recorren en orden y se pasa al siguiente si uno devuelve false o
/// lanza. El comprobante del abono sale por el primer medio que funcione.
///
/// La lógica de recorrido es pura: se testea con printers falsos.
/// </summary>
public sealed class CompositeReceiptPrinter : IReceiptPrinter
{
    private readonly IReadOnlyList<IReceiptPrinter> _printers;

    public CompositeReceiptPrinter(IReadOnlyList<IReceiptPrinter> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        _printers = printers;
    }

    /// <summary>Nombre del printer que realmente imprimió el último comprobante.</summary>
    public string LastUsedPrinter { get; private set; } = string.Empty;

    public string Name => _printers.Count == 0
        ? "Sin impresora"
        : string.Join(" → ", _printers.Select(p => p.Name));

    public bool IsAvailable => _printers.Any(p => SafeIsAvailable(p));

    public async Task<bool> PrintAsync(StandaloneReceipt receipt)
    {
        if (_printers.Count == 0)
        {
            AppLogger.E("IReceiptPrinter", "No hay ningun printer registrado; el abono no se imprimio.");
            return false;
        }

        foreach (var printer in _printers)
        {
            if (!SafeIsAvailable(printer))
            {
                AppLogger.I("IReceiptPrinter", $"{printer.Name}: no disponible, se pasa al siguiente.");
                continue;
            }

            try
            {
                AppLogger.I("IReceiptPrinter", $"Intentando imprimir con {printer.Name}...");
                if (await printer.PrintAsync(receipt))
                {
                    LastUsedPrinter = printer.Name;
                    AppLogger.I("IReceiptPrinter", $"Comprobante impreso con {printer.Name}.");
                    return true;
                }
                AppLogger.W("IReceiptPrinter", $"{printer.Name} no pudo imprimir; se intenta el siguiente.");
            }
            catch (Exception ex)
            {
                // Un printer que lanza NO debe cortar la cadena: el siguiente
                // puede funcionar.
                AppLogger.E("IReceiptPrinter", $"{printer.Name} lanzo excepcion; se intenta el siguiente.", ex);
            }
        }

        AppLogger.E("IReceiptPrinter",
            "Ningun printer pudo imprimir el comprobante del abono.");
        return false;
    }

    private static bool SafeIsAvailable(IReceiptPrinter printer)
    {
        try { return printer.IsAvailable; }
        catch (Exception ex)
        {
            AppLogger.W("IReceiptPrinter", $"{printer.Name}.IsAvailable lanzo: {ex.Message}");
            return false;
        }
    }
}
