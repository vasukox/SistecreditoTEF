namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973: indica como se abrio la app y que flujo espera HioPos al terminar.
///
/// Tres modos:
///   1. Kiosko (SALE): HI-POS lanzo el APK como modulo TEF con un Intent
///      TRANSACTION (TransactionType=SALE). Al terminar, devuelve el
///      resultado (SetResult+Finish) y HioPos imprime el comprobante.
///
///   2. Standalone (launcher): el cajero abrio el APK desde el icono sin
///      venta abierta en el POS. Solo aplica para PAGOS DE CREDITOS
///      (abonos). Al finalizar, la app cierra (FinishAffinity) y no
///      devuelve nada.
///
///   3. Entrada de caja desde HI-POS: el cajero entro por Caja > Entradas de
///      caja y eligio el TEF. HioPos manda TRANSACTION con
///      TransactionType=SALE pero SIN documento de venta. No hay factura
///      abierta, asi que el comprobante lo imprime la app, pero igual hay
///      que devolverle el resultado al POS para que registre el movimiento
///      de caja. Es un hibrido: IsStandalone=true (imprime local) +
///      IsRefundFromHioPos=true (devuelve setResult).
///
/// OJO con el nombre [IsRefundFromHioPos]: quedo de cuando se creia que las
/// entradas de caja llegaban con TransactionType=REFUND. NO es asi —llegan como
/// SALE sin documento— y hoy un REFUND es una NOTA DE CREDITO que se rechaza en
/// [MainActivity.HandleTransaction] antes de llegar aca. La bandera sigue
/// significando "el POS espera un setResult al terminar", que es lo que consulta
/// [ReciboPagoViewModel]; el nombre es historico.
///
/// Singleton en DI. Se setea en MainActivity.HandleIntent.
/// </summary>
public interface IStandaloneModeTracker
{
    bool IsStandalone { get; set; }

    /// <summary>
    /// True cuando la operacion la origino HI-POS y por lo tanto el POS ESPERA un
    /// setResult al terminar (en vez de cerrar con FinishAffinity, como hace el
    /// flujo abierto desde el icono del launcher).
    ///
    /// El nombre habla de REFUND por razones historicas; ver la nota en la
    /// documentacion de la interfaz.
    /// </summary>
    bool IsRefundFromHioPos { get; set; }

    void Reset();
}