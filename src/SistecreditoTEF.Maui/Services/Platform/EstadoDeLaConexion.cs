using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// En que estado esta el modulo para operar, de verdad.
/// </summary>
public enum SaludDelModulo
{
    /// <summary>
    /// Todavia no se hablo con Credinet en esta sesion. NO es "en linea": es que
    /// no se sabe, y decirlo asi es la unica respuesta honesta.
    /// </summary>
    SinVerificar,

    /// <summary>La ultima llamada a Credinet respondio.</summary>
    EnLinea,

    /// <summary>El terminal no tiene salida a internet.</summary>
    SinRed,

    /// <summary>Hay red, pero Credinet no contesta (timeout, 5xx, DNS).</summary>
    SinRespuesta,

    /// <summary>
    /// Credinet respondio <c>errorCode 225 · StoreNotFound</c>: la tienda con la
    /// que esta caja esta operando NO existe para Sistecredito. Es el unico
    /// estado que BLOQUEA.
    /// </summary>
    TiendaRechazada
}

/// <summary>
/// EL SEMAFORO DEL MODULO, Y QUE DICE LA VERDAD.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTO REEMPLAZA AL INDICADOR ANTERIOR
/// ─────────────────────────────────────────────────────────────────────────────
/// El primero miraba <c>Connectivity.NetworkAccess</c>, o sea si el terminal tenia
/// wifi. En una caja eso esta en verde casi siempre: el POS esta conectado a la
/// red de la tienda aunque Credinet este caido, aunque el certificado falle o
/// aunque la tienda no exista. Se reporto desde la terminal con el problema
/// encima: "ahi aparece siempre el en linea y por eso uno no sabe si esa es la
/// verdad". Tenia razon — una luz verde que no puede ponerse en rojo no informa
/// nada, y es peor que no tener luz, porque la gente la cree.
///
/// Este estado sale de las llamadas REALES a Credinet: lo alimenta
/// [CredinetRepository] con el resultado de cada peticion que hace el modulo. No
/// genera trafico propio y no puede mentir, porque refleja lo ultimo que de verdad
/// paso.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO QUE ESTE SEMAFORO NO PUEDE VER
/// ─────────────────────────────────────────────────────────────────────────────
/// Detecta una tienda que Credinet RECHAZA (errorCode 225). NO puede detectar una
/// tienda que Credinet ACEPTA pero atribuye a otra: en ese caso la respuesta es un
/// 200 normal y no hay ningun campo que diga a nombre de quien quedo. Si eso pasa,
/// el unico lugar donde se ve es conciliando en la plataforma de Sistecredito, y
/// hay que resolverlo con ellos. Queda escrito aqui para que nadie suponga que
/// este semaforo cubre ese caso.
/// </summary>
public interface IEstadoDeLaConexion
{
    SaludDelModulo Estado { get; }

    /// <summary>Cuando respondio Credinet por ultima vez, o null si nunca.</summary>
    DateTime? UltimaRespuesta { get; }

    /// <summary>
    /// false SOLO cuando Credinet rechazo la tienda. Sin red o sin respuesta no
    /// bloquea: son transitorios y el cajero tiene que poder reintentar.
    /// </summary>
    bool PuedeOperar { get; }

    event EventHandler? Cambio;

    /// <summary>Credinet contesto (aunque sea un rechazo de negocio).</summary>
    void RegistrarRespuesta(int errorCode);

    /// <summary>No se pudo llegar a Credinet.</summary>
    void RegistrarFalloDeRed();
}

/// <summary>
/// Implementacion en memoria. Singleton: el estado es uno solo para la app.
///
/// NUNCA LANZA: es un indicador. Si falla al avisar a un suscriptor, lo anota y
/// sigue; una caja no se puede caer por pintar un chip.
/// </summary>
public sealed class EstadoDeLaConexion : IEstadoDeLaConexion
{
    /// <summary>Codigo de Credinet para "esa tienda no existe". Manual §8.</summary>
    public const int StoreNotFound = 225;

    private readonly IEstadoDeRed _red;

    public EstadoDeLaConexion(IEstadoDeRed red) => _red = red;

    private SaludDelModulo _estado = SaludDelModulo.SinVerificar;

    public SaludDelModulo Estado
    {
        get
        {
            // La tienda rechazada MANDA sobre todo lo demas: es un problema de
            // configuracion que no se arregla solo, y taparlo con un "sin red"
            // porque justo se cayo el wifi seria esconder el unico estado que
            // obliga a actuar.
            if (_estado == SaludDelModulo.TiendaRechazada) return _estado;

            // Y sin red no se puede afirmar "en linea" por mucho que la ultima
            // llamada haya salido bien hace diez minutos.
            if (!_red.HayConexion) return SaludDelModulo.SinRed;

            return _estado;
        }
    }

    public DateTime? UltimaRespuesta { get; private set; }

    public bool PuedeOperar => Estado != SaludDelModulo.TiendaRechazada;

    public event EventHandler? Cambio;

    public void RegistrarRespuesta(int errorCode)
    {
        UltimaRespuesta = DateTime.Now;

        // Que Credinet conteste 225 significa que la linea funciona Y que la
        // tienda no sirve. Las dos cosas a la vez, y la segunda es la que importa.
        Cambiar(errorCode == StoreNotFound
            ? SaludDelModulo.TiendaRechazada
            : SaludDelModulo.EnLinea);
    }

    public void RegistrarFalloDeRed()
    {
        // No se pisa un rechazo de tienda con un fallo de red: el rechazo sigue
        // siendo cierto y sigue teniendo que bloquear.
        if (_estado == SaludDelModulo.TiendaRechazada) return;

        Cambiar(_red.HayConexion ? SaludDelModulo.SinRespuesta : SaludDelModulo.SinRed);
    }

    /// <summary>
    /// Vuelve a dejar el semaforo sin verificar. Lo llama quien cambia la tienda:
    /// un rechazo de la anterior no dice nada de la nueva, y arrastrarlo dejaria
    /// la caja bloqueada despues de corregir el problema.
    /// </summary>
    public void Olvidar()
    {
        UltimaRespuesta = null;
        Cambiar(SaludDelModulo.SinVerificar);
    }

    private void Cambiar(SaludDelModulo nuevo)
    {
        var antes = Estado;
        _estado = nuevo;
        if (Estado == antes) return;

        AppLogger.I("EstadoDeLaConexion", $"Estado del modulo: {antes} -> {Estado}.");

        try
        {
            Cambio?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaConexion", $"Fallo avisando el cambio de estado: {ex.Message}");
        }
    }
}
