using Android.Content;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Imprime usando el SDK de Sunmi (AIDL preinstalado en POS Sunmi).
///
/// Sunmi expone un IntentService accesible via Intent:
///   ComponentName: "com.sunmi.printerservice/.PrinterService"
///   Action: "sunmi printerprint"
///   Extras: "data" = XML del recibo (formato propio de Sunmi)
///   Extras: "package" = nombre del package actual (requerido)
///
/// Si el POS no es Sunmi (o el servicio no esta instalado), IsAvailable
/// retorna false y se usa el fallback PDF.
///
/// NOTA: no usamos NuGet oficial de Sunmi para evitar agregar una
/// dependencia dura al proyecto. Usamos Intent directo + reflection.
/// Si en el futuro Sunmi cambia el Intent, hay que actualizar.
/// </summary>
public class SunmiPrinter : IReceiptPrinter
{
    public string Name => "Sunmi Printer";

    public bool IsAvailable
    {
        get
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ApplicationContext;
                if (context is null) return false;

                var pm = context.PackageManager;
                try
                {
                    pm.GetPackageInfo("com.sunmi.printerservice", 0);
                    return true;
                }
                catch (Android.Content.PM.PackageManager.NameNotFoundException)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<bool> PrintAsync(StandaloneReceipt r)
    {
        return Task.Run(() =>
        {
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity is null) return false;

                var text = BuildSunmiReceiptText(r);

                var intent = new Intent();
                intent.SetComponent(new ComponentName(
                    "com.sunmi.printerservice",
                    "com.sunmi.printerservice.PrinterService"));
                intent.SetAction("sunmi.print");
                intent.PutExtra("data", text);
                intent.PutExtra("package", activity.PackageName);

                activity.StartService(intent);
                Common.AppLogger.I("SunmiPrinter", $"Recibo enviado a Sunmi ({r.PaymentNumber})");
                return true;
            }
            catch (Exception ex)
            {
                Common.AppLogger.E("SunmiPrinter", "Error imprimiendo en Sunmi", ex);
                return false;
            }
        });
    }

    private static string BuildSunmiReceiptText(StandaloneReceipt r)
    {
        var sep = new string('=', 42);
        var lines = new System.Collections.Generic.List<string>
        {
            "{center}{b}COMPROBANTE DE PAGO{/b}{/center}",
            sep,
            $"Fecha: {r.Fecha:dd/MM/yyyy HH:mm}",
            $"Pago #: {r.PaymentNumber}",
            $"Credito: {r.CreditNumber}",
            $"Cliente: {r.Cliente}",
            $"C.C.: {r.ClienteDocumento}",
            sep,
            $"Capital pagado: $ {r.CapitalPagado:N0}",
            $"Saldo restante: $ {r.SaldoRestante:N0}",
            $"Proximo pago: {r.ProximoPago:yyyy-MM-dd}",
            $"Minimo proximo: $ {r.ProximoMinimo:N0}",
            sep,
            "{center}{b}GRACIAS POR SU PAGO{/b}{/center}",
            "{cut}"
        };
        return string.Join("\n", lines);
    }
}