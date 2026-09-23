namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Decide si un Intent entrante debe procesarse o descartarse.
///
/// Existe porque <c>launchMode=singleTask</c> hace que el ícono del launcher y
/// HioPos compartan la MISMA Activity: los dos modos (recaudo standalone y
/// facturación) entran por el mismo lugar y hay que evitar que uno pise al otro.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE CORRIGE
/// ─────────────────────────────────────────────────────────────────────────────
/// La versión anterior descartaba CUALQUIER intent mientras la bandera de venta
/// viva estuviera encendida:
///
///     if (state.HioposTransactionActive) return true;   // sin mirar qué llega
///
/// Consecuencia en el terminal: el cajero elige Sistecrédito, vuelve atrás en
/// HioPos (el módulo nunca devolvió resultado, así que la bandera sigue en true) y
/// vuelve a elegir Sistecrédito. Ese segundo <c>TRANSACTION</c> se DESCARTABA, y el
/// módulo quedaba mostrando los datos de la venta anterior — incluida la cédula de
/// otro cliente.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA REGLA CORRECTA
/// ─────────────────────────────────────────────────────────────────────────────
/// HioPos es la autoridad sobre la venta. Sus acciones se procesan SIEMPRE: si
/// manda un <c>TRANSACTION</c> nuevo, está diciendo "arrancá de nuevo con esta
/// venta", y hay que rehacer el estado desde su documento.
///
/// Lo único que se descarta es el intent del LAUNCHER mientras hay una venta de
/// HioPos esperando resultado: ahí sí el cajero tocó el ícono por error y abrir el
/// flujo de abonos rompería la factura en curso.
///
/// Es código puro: se testea sin Android.
/// </summary>
public static class HioposIntentGuard
{
    /// <summary>Action del intent del ícono del launcher.</summary>
    public const string LauncherAction = "android.intent.action.MAIN";

    /// <summary>Resultado de la decisión, con el motivo para el log.</summary>
    public sealed record Decision(bool Discard, string Reason);

    /// <summary>
    /// ¿Se descarta este intent?
    /// </summary>
    /// <param name="incomingAction">Action del intent que llegó.</param>
    /// <param name="hioposTransactionActive">
    /// true si hay una factura de HioPos entre el arranque de la TRANSACTION y la
    /// devolución del resultado.
    /// </param>
    /// <param name="isStandalone">true si la app está en modo recaudo standalone.</param>
    public static Decision Evaluate(
        string? incomingAction, bool hioposTransactionActive, bool isStandalone)
    {
        var action = incomingAction ?? string.Empty;
        var esLauncher = string.Equals(action, LauncherAction, StringComparison.Ordinal);

        // Las acciones de HioPos SIEMPRE se procesan: es la autoridad sobre la venta.
        if (!esLauncher)
        {
            return new Decision(false,
                hioposTransactionActive
                    ? "Accion de HioPos con una venta ya viva: se reinicia el flujo con la venta nueva."
                    : "Accion de HioPos.");
        }

        // Desde acá, es el intent del launcher.
        if (hioposTransactionActive)
        {
            return new Decision(true,
                "Intent del launcher descartado: hay una factura de HioPos esperando resultado.");
        }

        if (isStandalone)
        {
            return new Decision(true,
                "Intent del launcher descartado: ya se esta en el flujo de abonos standalone.");
        }

        return new Decision(false, "Intent del launcher: se abre el flujo de abonos.");
    }
}
