using Android.Content;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Último recurso de la cadena de impresión: genera el comprobante como **PDF**
/// y abre el chooser del sistema para que el cajero lo imprima, lo guarde o lo
/// envíe al cliente por WhatsApp.
///
/// QA — cambios respecto a la versión anterior:
///   • Antes generaba un <c>.txt</c> con <c>mimeType="text/plain"</c>. Un .txt no
///     se puede mandar a imprimir desde el chooser en la mayoría de POS, así que
///     el "fallback de impresión" no imprimía. Ahora es un PDF
///     (<c>application/pdf</c>), imprimible y compartible.
///   • Antes tenía su propio layout de texto, distinto al de los otros dos
///     printers y al del voucher de HioPos. Ahora usa [ReceiptTextBuilder], así
///     que el comprobante es idéntico por cualquier vía.
///   • Se limpian los comprobantes viejos de la caché para no acumular datos
///     personales indefinidamente en disco.
///
/// Nota: este printer no puede confirmar que el cajero realmente imprimió (el
/// chooser es asíncrono y el sistema no informa el resultado). Devuelve true
/// cuando logró generar el PDF y abrir el chooser, que es lo máximo observable
/// desde aquí. Va último en la cadena precisamente por eso.
/// </summary>
public class PdfReceiptPrinter : IReceiptPrinter
{
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(1);

    public string Name => "Compartir PDF";

    /// <summary>Siempre disponible: es el fallback final.</summary>
    public bool IsAvailable => true;

    public async Task<bool> PrintAsync(StandaloneReceipt receipt)
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is null)
            {
                AppLogger.E("PdfReceiptPrinter", "CurrentActivity es null; no se puede compartir.");
                return false;
            }

            var lines = ReceiptTextBuilder.BuildLines(receipt);
            var pdf = ThermalPdfWriter.Build(lines);

            var cacheDir = activity.CacheDir;
            if (cacheDir is null)
            {
                AppLogger.E("PdfReceiptPrinter", "CacheDir es null; no se puede escribir el comprobante.");
                return false;
            }

            PurgeOldReceipts(cacheDir.AbsolutePath);

            var fileName = $"comprobante_abono_{receipt.PaymentNumber}_" +
                           $"{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(cacheDir.AbsolutePath, fileName);
            await File.WriteAllBytesAsync(filePath, pdf);

            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                activity,
                $"{activity.PackageName}.fileprovider",
                new Java.IO.File(filePath));

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("application/pdf");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.PutExtra(Intent.ExtraSubject, $"Comprobante de abono #{receipt.PaymentNumber}");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            var chooser = Intent.CreateChooser(intent, "Imprimir o compartir comprobante");
            chooser?.AddFlags(ActivityFlags.NewTask);
            chooser?.AddFlags(ActivityFlags.GrantReadUriPermission);

            activity.StartActivity(chooser);

            // Darle un instante al sistema para montar el chooser antes de que
            // la Activity siga su curso (QA: BugFix #3 original, se conserva).
            await Task.Delay(500);

            AppLogger.I("PdfReceiptPrinter", $"Comprobante PDF generado y compartido: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.E("PdfReceiptPrinter", "Error generando o compartiendo el comprobante", ex);
            return false;
        }
    }

    /// <summary>
    /// QA M-13: los comprobantes contienen datos del cliente. Se borran los de
    /// más de un día para no dejarlos acumulados en la caché de la app.
    /// Best-effort: si falla, no interrumpe la impresión.
    /// </summary>
    private static void PurgeOldReceipts(string cacheDir)
    {
        try
        {
            var cutoff = DateTime.Now - CacheRetention;
            foreach (var old in Directory.EnumerateFiles(cacheDir, "comprobante_abono_*"))
            {
                if (File.GetLastWriteTime(old) < cutoff)
                    File.Delete(old);
            }
        }
        catch (Exception ex)
        {
            AppLogger.W("PdfReceiptPrinter", $"No se pudo purgar comprobantes viejos: {ex.Message}");
        }
    }
}
