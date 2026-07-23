namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973: indica si la app se abrio en modo standalone (desde el icono
/// del launcher) o en modo kiosko (desde HI-POS Cloud).
///
/// En modo standalone:
///   - El cajero entro directo al APK sin venta abierta en el POS.
///   - Solo aplica para el flujo de PAGOS DE CREDITOS (abonos).
///   - Al finalizar un pago, la app cierra (no devuelve nada al POS).
///
/// En modo kiosko:
///   - HI-POS lanzo el APK como modulo TEF con un Intent explicito.
///   - Al finalizar, devuelve el resultado (SetResult+Finish) al POS.
///
/// Singleton en DI. Se setea en MainActivity.HandleIntent.
/// </summary>
public interface IStandaloneModeTracker
{
    bool IsStandalone { get; set; }
    void Reset();
}