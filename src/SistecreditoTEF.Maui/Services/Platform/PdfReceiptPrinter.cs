using Android.Content;
using AndroidX.Core.Content;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Fallback para impresion standalone: cuando el POS no tiene SDK de
/// Sunmi (o PAX, o cualquier otro), generamos un archivo de texto plano
/// con el comprobante y abrimos el share intent del sistema. El cajero
/// puede elegir: guardar, enviar por WhatsApp, abrir en otra app, etc.
///
/// No imprime directamente, pero cubre el caso donde el POS no es Sunmi.
/// </summary>
public class PdfReceiptPrinter : IReceiptPrinter
{
    public string Name => "Share Intent";

    public bool IsAvailable => true;  // Siempre disponible (es el fallback)

    public Task<bool> PrintAsync(StandaloneReceipt r)
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is null) return Task.FromResult(false);

            var text = BuildTextReceipt(r);

            // Generar nombre de archivo unico.
            var fileName = $"comprobante_abono_{r.PaymentNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var cacheDir = activity.CacheDir;
            var filePath = System.IO.Path.Combine(cacheDir.AbsolutePath, fileName);
            System.IO.File.WriteAllText(filePath, text);

            // Compartir via FileProvider de AndroidX.
            var file = new Java.IO.File(filePath);
            var authority = $"{activity.PackageName}.fileprovider";
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, authority, file);

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.PutExtra(Intent.ExtraText, $"Comprobante de pago #{r.PaymentNumber}");
            intent.AddFlags(ActivityFlags.NewTask);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            var chooser = Intent.CreateChooser(intent, "Compartir comprobante");
            chooser.AddFlags(ActivityFlags.NewTask);
            activity.StartActivity(chooser);

            AppLogger.I("PdfReceiptPrinter", $"Comprobante generado: {fileName}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            AppLogger.E("PdfReceiptPrinter", "Error generando comprobante", ex);
            return Task.FromResult(false);
        }
    }

    private static string BuildTextReceipt(StandaloneReceipt r)
    {
        var sep = new string('=', 42);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("COMPROBANTE DE PAGO - SISTECREDITO");
        sb.AppendLine(sep);
        sb.AppendLine($"Tienda:         {r.Tienda}");
        sb.AppendLine($"Fecha:          {r.Fecha:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Cajero:         {r.Cajero}");
        sb.AppendLine($"Pago #:         {r.PaymentNumber}");
        sb.AppendLine($"Credito:        {r.CreditNumber}");
        sb.AppendLine($"Cliente:        {r.Cliente}");
        sb.AppendLine($"C.C.:           {r.ClienteDocumento}");
        sb.AppendLine(sep);
        sb.AppendLine($"Capital pagado: $ {r.CapitalPagado:N0}");
        sb.AppendLine($"Saldo restante: $ {r.SaldoRestante:N0}");
        sb.AppendLine($"Proximo pago:   {r.ProximoPago:yyyy-MM-dd}");
        sb.AppendLine($"Minimo proximo: $ {r.ProximoMinimo:N0}");
        sb.AppendLine(sep);
        sb.AppendLine("GRACIAS POR SU PAGO");
        return sb.ToString();
    }
}