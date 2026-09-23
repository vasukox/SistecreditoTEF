namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Con que accion se abrio la app en este arranque.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE NO ALCANZA CON ITransactionStateStore
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>HioposTransactionActive</c> se enciende en <c>HandleTransaction</c>, que corre
/// en <c>OnResume</c>. Pero la pantalla raiz del Shell se construye dentro de
/// <c>base.OnCreate</c>, ANTES. O sea que cuando [SplashViewModel] pregunta si hay
/// una operacion de HioPos en curso, la respuesta todavia es "no" aunque la app se
/// haya abierto justamente para una venta.
///
/// El sintoma fue concreto: al facturar, el modulo pedia la clave del cajero —que
/// solo corresponde a los abonos— en vez de ir directo a consultar el cliente.
///
/// Esto se setea en <c>OnCreate</c> antes de <c>base.OnCreate</c>, o sea antes de que
/// exista una pantalla que pueda leerlo mal. Es el unico dato de arranque disponible
/// a tiempo.
/// </summary>
public interface ILaunchContext
{
    /// <summary>Action del Intent con el que se abrio la app.</summary>
    string? Action { get; set; }

    /// <summary>
    /// true si la app la abrio HioPos (cualquier accion del contrato TEF) y no el
    /// icono del launcher. En ese caso la navegacion la maneja [MainActivity] y la
    /// pantalla raiz NO debe decidir nada.
    /// </summary>
    bool EsDeHiopos { get; }
}

public sealed class LaunchContext : ILaunchContext
{
    private const string AccionLauncher = "android.intent.action.MAIN";

    private readonly object _gate = new();
    private string? _action;

    public string? Action
    {
        get { lock (_gate) return _action; }
        set { lock (_gate) _action = value; }
    }

    public bool EsDeHiopos
    {
        get
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(_action)) return false;
                return !string.Equals(_action, AccionLauncher, StringComparison.Ordinal);
            }
        }
    }
}
