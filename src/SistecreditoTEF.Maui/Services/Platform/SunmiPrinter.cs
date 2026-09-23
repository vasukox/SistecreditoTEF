using Android.Content;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Impresión nativa en POS Sunmi. **DESACTIVADO POR DEFECTO** — ver más abajo.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA: POR QUÉ ESTA CLASE ESTABA ROTA Y AHORA NO MIENTE
/// ─────────────────────────────────────────────────────────────────────────────
/// La implementación anterior no correspondía a ninguna API real de Sunmi:
///
///   • Componente: <c>com.sunmi.printerservice/.PrinterService</c>
///     El servicio real de Sunmi es <c>woyou.aidlservice.jiuv5.IWoyouService</c>,
///     en el paquete <c>woyou.aidlservice.jiuv5</c>.
///   • Acción: el código usaba <c>"sunmi.print"</c> mientras el comentario de la
///     propia clase documentaba <c>"sunmi printerprint"</c>. Ninguna de las dos
///     existe.
///   • Transporte: <c>StartService</c> hacia un servicio de OTRA app lanza
///     <c>IllegalStateException</c> en Android 8+ (API 26+) desde background, y
///     además requiere que el servicio esté exportado. Sunmi se consume por
///     <c>BindService</c> + AIDL, no por StartService.
///   • Marcado: el texto usaba tags inventados (<c>{center}</c>, <c>{b}</c>,
///     <c>{cut}</c>) que ninguna impresora interpreta.
///   • **Y lo más grave:** el método devolvía <c>true</c> incondicionalmente
///     después de un <c>Task.Delay(1500)</c>, así que la app reportaba
///     "comprobante impreso" cuando no se había impreso nada. Ese es el motivo
///     por el que "los abonos no imprimen" pasó desapercibido.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// ESTADO ACTUAL
/// ─────────────────────────────────────────────────────────────────────────────
/// Integrar Sunmi de verdad requiere el AIDL oficial del fabricante
/// (<c>IWoyouService.aidl</c> → binding generado) o su SDK, y validación en
/// hardware real. Nada de eso se puede inventar desde el código: los códigos de
/// transacción del Binder son ordinales del AIDL y adivinarlos produciría fallos
/// silenciosos, exactamente el problema que estamos corrigiendo.
///
/// Por eso esta clase:
///   1. Solo se declara disponible si el paquete AIDL real está instalado **Y**
///      la configuración <c>Printing:EnableSunmiNative</c> está en true.
///   2. Mientras no exista el binding oficial, <see cref="PrintAsync"/> devuelve
///      <c>false</c> — nunca un falso positivo — para que
///      [CompositeReceiptPrinter] caiga al siguiente medio (Android Print con
///      PDF real), que sí funciona.
///
/// PARA HABILITARLA: agregar el AIDL/SDK oficial de Sunmi, implementar el
/// binding en <see cref="PrintAsync"/>, poner <c>Printing:EnableSunmiNative</c>
/// en true y validar en una terminal Sunmi física.
/// </summary>
public class SunmiPrinter : IReceiptPrinter
{
    /// <summary>Paquete del servicio AIDL real de Sunmi (no el que se usaba antes).</summary>
    internal const string SunmiAidlPackage = "woyou.aidlservice.jiuv5";

    private readonly bool _nativeEnabled;

    public SunmiPrinter(bool nativeEnabled = false)
    {
        _nativeEnabled = nativeEnabled;
    }

    public string Name => "Sunmi (nativo)";

    public bool IsAvailable
    {
        get
        {
            // Sin binding oficial no hay impresión posible: declararse
            // disponible solo lograría que la cadena se detenga en un printer
            // que no puede imprimir.
            if (!_nativeEnabled) return false;

            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ApplicationContext;
                if (context?.PackageManager is null) return false;

                try
                {
                    context.PackageManager.GetPackageInfo(SunmiAidlPackage, 0);
                    return true;
                }
                catch (Android.Content.PM.PackageManager.NameNotFoundException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.W("SunmiPrinter", $"No se pudo consultar el servicio Sunmi: {ex.Message}");
                return false;
            }
        }
    }

    public Task<bool> PrintAsync(StandaloneReceipt receipt)
    {
        // Contrato explícito: false = "no imprimí", para que el compuesto siga
        // con el siguiente printer. NUNCA devolver true sin impresión real.
        AppLogger.W("SunmiPrinter",
            "Impresion nativa Sunmi no implementada (falta el AIDL oficial del fabricante). " +
            "Se delega al siguiente printer de la cadena.");
        return Task.FromResult(false);
    }
}
