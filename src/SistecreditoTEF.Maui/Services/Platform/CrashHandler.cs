using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Que se hace cuando una excepcion llega a la red de seguridad: registrarla,
/// guardarla, y no dejar al POS esperando.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL ORDEN IMPORTA Y ES ESTE
/// ─────────────────────────────────────────────────────────────────────────────
///   1. Log del sistema  — es lo unico que existe si todo lo demas fallo.
///   2. Guardar el fallo — para poder leerlo dias despues, en la tienda.
///   3. Avisarle al POS  — solo si hay una operacion viva.
///
/// Cada paso va en su propio try/catch y nunca interrumpe al siguiente: este
/// codigo corre cuando algo YA se rompio, y un fallo aca deja al terminal sin
/// ningun rastro de lo que paso.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE AVISARLE AL POS ES LA PARTE QUE MAS VALE
/// ─────────────────────────────────────────────────────────────────────────────
/// HioPos lanza el modulo y se queda esperando el resultado. Si el modulo muere
/// sin contestar, el POS agota su tiempo y —segun lo verificado en terminal—
/// termina la venta SIN COBRAR. O sea que una excepcion de UI, que no tiene nada
/// que ver con el cobro, puede costar una factura.
///
/// Se responde con el mismo mensaje que el fail-safe de siempre
/// ([HioposResultBuilder.BuildUnexpectedFailure]): NO promete que no se cobro,
/// porque la excepcion pudo ocurrir en cualquier punto, incluso despues de crear
/// el credito. Manda a verificar, que es lo unico honesto.
/// </summary>
public sealed class CrashHandler(ICrashStore store)
{
    /// <summary>Que pantalla estaba abierta. Aporta el 80% del diagnostico.</summary>
    public Func<string?>? PantallaActual { get; init; }

    /// <summary>Identificador del APK, para saber que build tiene esa caja.</summary>
    public Func<string?>? Build { get; init; }

    /// <summary>
    /// Le contesta a HioPos si hay una operacion viva. Lo arma quien instala el
    /// guard, porque depende de Android.
    /// </summary>
    public Action? AvisarAlPos { get; init; }

    /// <summary>Cuantos fallos vio este proceso. Sirve para no entrar en bucle.</summary>
    public int Contador { get; private set; }

    public CrashRecord Registrar(Exception ex, CrashOrigin origen)
    {
        Contador++;

        var fallo = CrashRecord.De(
            ex, origen,
            pantalla: Seguro(PantallaActual),
            build: Seguro(Build));

        try
        {
            AppLogger.E("CrashGuard", fallo.ToSummary(), ex);
        }
        catch { /* si ni el log anda, quedan los dos pasos que siguen */ }

        try
        {
            store.Guardar(fallo);
        }
        catch { /* [FileCrashStore] ya atrapa lo suyo; esto es el segundo cinturon */ }

        try
        {
            AvisarAlPos?.Invoke();
        }
        catch (Exception avisoEx)
        {
            try
            {
                AppLogger.E("CrashGuard",
                    "No se pudo avisarle al POS despues del fallo; la venta puede quedar sin cobrar.",
                    avisoEx);
            }
            catch { /* nada mas que hacer */ }
        }

        return fallo;
    }

    private static string? Seguro(Func<string?>? f)
    {
        if (f is null) return null;
        try { return f(); } catch { return null; }
    }
}
