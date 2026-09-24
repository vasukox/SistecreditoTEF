namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Con que accion se abrio la app en este arranque, y si esa accion ya fue
/// atendida.
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
    /// <summary>
    /// Action del Intent con el que se abrio la app, o del ultimo que llego.
    ///
    /// Asignarlo marca la accion como NO atendida: cada intent nuevo vuelve a
    /// darle el mando a [MainActivity].
    /// </summary>
    string? Action { get; set; }

    /// <summary>
    /// true si la app la abrio HioPos (cualquier accion del contrato TEF) y no el
    /// icono del launcher. En ese caso la navegacion la maneja [MainActivity] y la
    /// pantalla raiz NO debe decidir nada.
    /// </summary>
    bool EsDeHiopos { get; }

    /// <summary>
    /// true cuando [MainActivity] ya termino de navegar por la accion actual.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE HACE FALTA, Y QUE PANTALLA NEGRA ARREGLA
    /// ─────────────────────────────────────────────────────────────────────────
    /// [EsDeHiopos] responde "quien abrio la app", y con eso solo la raiz no puede
    /// distinguir dos momentos MUY distintos que se ven igual:
    ///
    ///   a) Arranque: la raiz aparece ANTES de que MainActivity navegue. Aca la
    ///      raiz tiene que quedarse quieta, o pisa la navegacion de la venta y el
    ///      modulo termina pidiendo la clave del cajero en medio de una factura.
    ///
    ///   b) El cajero volvio ATRAS desde la primera pantalla del flujo. Como el
    ///      flujo se empuja sobre la raiz, "atras" cae aca — y aca no hay nada:
    ///      fondo #333333 y ningun contenido. En el terminal eso es, literalmente,
    ///      una pantalla negra de la que no se sale.
    ///
    /// La diferencia entre (a) y (b) es justamente si MainActivity ya navego. Esta
    /// bandera es ese dato, y nada mas que ese dato.
    ///
    /// Se apaga sola al asignar [Action]: un intent nuevo es una orden nueva.
    /// </summary>
    bool NavegacionConsumida { get; set; }
}

public sealed class LaunchContext : ILaunchContext
{
    private const string AccionLauncher = "android.intent.action.MAIN";

    private readonly object _gate = new();
    private string? _action;
    private bool _consumida;

    public string? Action
    {
        get { lock (_gate) return _action; }
        set
        {
            lock (_gate)
            {
                _action = value;

                // Un intent nuevo vuelve a poner a MainActivity al mando. Sin esto,
                // un relanzamiento caliente desde HioPos encontraria la bandera en
                // true de la venta anterior y la raiz decidiria por su cuenta,
                // pisando la venta nueva.
                _consumida = false;
            }
        }
    }

    public bool NavegacionConsumida
    {
        get { lock (_gate) return _consumida; }
        set { lock (_gate) _consumida = value; }
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
