using Android.App;
using Android.Content;
using Android.OS;
using Android.Print;
using Microsoft.Maui.ApplicationModel;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Imprime el comprobante de abono usando el Android Print Framework (API 19+).
/// Es el camino PRINCIPAL de impresión local: funciona con cualquier impresora
/// registrada en el sistema (Bluetooth térmica, USB, red, o un Print Service
/// como Mopria / el del fabricante).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA: DOS BUGS CORREGIDOS AQUÍ
/// ─────────────────────────────────────────────────────────────────────────────
/// 1. **Se enviaba texto plano donde el framework espera un PDF.** El
///    <c>OnWrite</c> anterior hacía
///    <c>stream.Write(Encoding.UTF8.GetBytes(texto))</c> mientras
///    <c>OnLayout</c> declaraba <c>PrintContentType.Document</c>. El
///    ParcelFileDescriptor de un trabajo de impresión debe recibir un PDF
///    válido; con texto crudo el print service produce un trabajo inválido o
///    una hoja en blanco. **Esta era la causa real de que el abono no saliera.**
///    Ahora se escribe un PDF real generado por [ThermalPdfWriter].
///
/// 2. **Reportaba éxito siempre.** <c>PrintAsync</c> devolvía <c>true</c> justo
///    después de llamar a <c>printManager.Print(...)</c>, sin mirar el resultado.
///    Ahora se consulta el [PrintJob] devuelto y se reporta <c>false</c> si el
///    trabajo quedó fallido, bloqueado o cancelado, para que
///    [CompositeReceiptPrinter] pueda caer al fallback.
///
/// También se fija un tamaño de papel de rollo de 80 mm y márgenes mínimos: sin
/// eso el framework asume A4 y el comprobante sale en una hoja enorme con el
/// texto arriba a la izquierda.
/// </summary>
public class AndroidPrintPrinter : IReceiptPrinter
{
    /// <summary>80 mm expresados en mils (milésimas de pulgada), como pide PrintAttributes.</summary>
    private const int RollWidthMils = 3150;

    /// <summary>Cuánto se espera confirmación del trabajo antes de darlo por encolado.</summary>
    private static readonly TimeSpan JobSettleTimeout = TimeSpan.FromSeconds(3);

    public string Name => "Android Print";

    /// <summary>
    /// Disponible solo si el sistema tiene al menos un SERVICIO DE IMPRESIÓN
    /// habilitado.
    ///
    /// Antes se conformaba con que existiera el <c>PrintManager</c>, que existe en
    /// TODO dispositivo Android. Eso hacía que este printer se declarara siempre
    /// disponible y, peor, que reportara éxito al "encolar" un trabajo que nunca
    /// tenía impresora a la que ir:
    ///
    ///   I/AndroidPrintPrinter: Trabajo #N encolado en el sistema de impresion.
    ///   I/IReceiptPrinter:     Comprobante impreso con Android Print.   ← falso
    ///
    /// En los POS revisados <c>enabled_print_services</c> está vacío (la impresora
    /// es USB y no se expone por el print framework), así que este camino no puede
    /// imprimir y debe declararse NO disponible para que la cadena siga al
    /// siguiente medio.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ApplicationContext;
                if (context is null) return false;
                if (context.GetSystemService(Context.PrintService) is not PrintManager) return false;

                var habilitados = Android.Provider.Settings.Secure.GetString(
                    context.ContentResolver, "enabled_print_services");

                if (string.IsNullOrWhiteSpace(habilitados))
                {
                    AppLogger.I("AndroidPrintPrinter",
                        "No hay ningun servicio de impresion habilitado en Android; " +
                        "este camino no puede imprimir.");
                    return false;
                }

                AppLogger.I("AndroidPrintPrinter", "Servicios de impresion habilitados presentes.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.W("AndroidPrintPrinter", $"PrintManager no disponible: {ex.Message}");
                return false;
            }
        }
    }

    public async Task<bool> PrintAsync(StandaloneReceipt receipt)
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is null)
            {
                AppLogger.E("AndroidPrintPrinter", "CurrentActivity es null; no se puede imprimir.");
                return false;
            }

            if (activity.GetSystemService(Context.PrintService) is not PrintManager printManager)
            {
                AppLogger.E("AndroidPrintPrinter", "PrintManager no disponible.");
                return false;
            }

            var lines = ReceiptTextBuilder.BuildLines(receipt);
            var pdf = ThermalPdfWriter.Build(lines);
            AppLogger.I("AndroidPrintPrinter",
                $"PDF del comprobante generado ({pdf.Length} bytes, {lines.Count} lineas).");

            var jobName = $"Sistecredito_Abono_{receipt.PaymentNumber}";
            var attributes = BuildRollAttributes(lines.Count);

            PrintJob? job = null;
            // El PrintManager exige hilo de UI.
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                job = printManager.Print(jobName, new PdfPrintAdapter(pdf, jobName), attributes);
            });

            if (job is null)
            {
                AppLogger.E("AndroidPrintPrinter", "PrintManager.Print no devolvio trabajo.");
                return false;
            }

            return await WaitForJobAsync(job, receipt.PaymentNumber);
        }
        catch (Exception ex)
        {
            AppLogger.E("AndroidPrintPrinter", "Error lanzando el trabajo de impresion", ex);
            return false;
        }
    }

    /// <summary>
    /// Papel de rollo de 80 mm de ancho y alto proporcional al contenido, con
    /// márgenes mínimos. Sin esto el framework asume A4.
    /// </summary>
    private static PrintAttributes BuildRollAttributes(int lineCount)
    {
        // Alto en mils a partir de la geometría del PDF (1 pt = 1/72 in).
        var heightPoints = 20.0 + (Math.Max(lineCount, 1) * 10.5);
        var heightMils = (int)Math.Ceiling(heightPoints / 72.0 * 1000.0);

        var media = new PrintAttributes.MediaSize(
            "sistecredito_roll_80mm", "Rollo 80 mm", RollWidthMils, Math.Max(heightMils, 500));

        return new PrintAttributes.Builder()
            .SetMediaSize(media)!
            .SetResolution(new PrintAttributes.Resolution("default", "Default", 203, 203))!
            .SetMinMargins(PrintAttributes.Margins.NoMargins!)!
            .Build();
    }

    /// <summary>
    /// Espera brevemente a que el trabajo se resuelva. Si termina fallido,
    /// bloqueado o cancelado devolvemos false para que la cadena use el
    /// fallback. Si sigue encolado/iniciado lo damos por bueno: ya está en manos
    /// del sistema y el cajero ve el diálogo de impresión.
    /// </summary>
    private static async Task<bool> WaitForJobAsync(PrintJob job, string paymentNumber)
    {
        var deadline = DateTime.UtcNow + JobSettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (job.IsCompleted)
            {
                AppLogger.I("AndroidPrintPrinter", $"Trabajo #{paymentNumber} completado.");
                return true;
            }
            if (job.IsFailed)
            {
                AppLogger.E("AndroidPrintPrinter", $"Trabajo #{paymentNumber} FALLIDO.");
                return false;
            }
            if (job.IsCancelled)
            {
                AppLogger.W("AndroidPrintPrinter", $"Trabajo #{paymentNumber} cancelado.");
                return false;
            }
            if (job.IsBlocked)
            {
                AppLogger.E("AndroidPrintPrinter",
                    $"Trabajo #{paymentNumber} bloqueado (sin papel / impresora offline).");
                return false;
            }
            await Task.Delay(200);
        }

        AppLogger.I("AndroidPrintPrinter",
            $"Trabajo #{paymentNumber} encolado en el sistema de impresion.");
        return true;
    }

    /// <summary>
    /// Adapter que entrega el PDF ya generado al framework de impresión.
    /// Se limita a copiar bytes: la maquetación ocurrió en [ThermalPdfWriter].
    /// </summary>
    private sealed class PdfPrintAdapter : PrintDocumentAdapter
    {
        private readonly byte[] _pdf;
        private readonly string _name;

        public PdfPrintAdapter(byte[] pdf, string name)
        {
            _pdf = pdf;
            _name = name;
        }

        public override void OnLayout(
            PrintAttributes? oldAttributes,
            PrintAttributes? newAttributes,
            CancellationSignal? cancellationSignal,
            LayoutResultCallback? callback,
            Bundle? metadata)
        {
            if (callback is null) return;

            if (cancellationSignal?.IsCanceled == true)
            {
                callback.OnLayoutCancelled();
                return;
            }

            var info = new PrintDocumentInfo.Builder(_name + ".pdf")
                .SetContentType(PrintContentType.Document)
                .SetPageCount(1)
                .Build();

            // changed:true → el framework siempre pide el contenido de nuevo.
            callback.OnLayoutFinished(info, true);
        }

        public override void OnWrite(
            PageRange[]? pages,
            ParcelFileDescriptor? destination,
            CancellationSignal? cancellationSignal,
            WriteResultCallback? callback)
        {
            if (callback is null) return;

            try
            {
                if (cancellationSignal?.IsCanceled == true)
                {
                    callback.OnWriteCancelled();
                    return;
                }

                var fd = destination?.FileDescriptor;
                if (fd is null)
                {
                    callback.OnWriteFailed("FileDescriptor nulo");
                    return;
                }

                using var stream = new Java.IO.FileOutputStream(fd);
                stream.Write(_pdf, 0, _pdf.Length);
                stream.Flush();

                callback.OnWriteFinished([PageRange.AllPages!]);
            }
            catch (Exception ex)
            {
                AppLogger.E("AndroidPrintPrinter", "Error escribiendo el PDF del comprobante", ex);
                callback.OnWriteFailed(ex.Message ?? "Error");
            }
        }
    }
}
