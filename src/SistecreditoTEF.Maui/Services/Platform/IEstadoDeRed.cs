namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// SI LA CAJA TIENE RED, PARA MOSTRARLO EN PANTALLA.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QUE SIGNIFICA Y QUE NO
/// ─────────────────────────────────────────────────────────────────────────────
/// Esto responde "el terminal tiene salida a internet", que es lo que el sistema
/// operativo sabe. NO responde "Credinet esta respondiendo": para eso habria que
/// llamar a la API, y una pantalla no puede estar consultando un servicio de pagos
/// cada vez que se dibuja.
///
/// Es suficiente para lo que se pide: el caso que se ve en tienda es el WiFi
/// caido, y ahi el cajero necesita saber por que no avanza ANTES de empezar un
/// cobro, no en medio. Por eso el indicador dice "En linea" / "Sin conexion" y no
/// promete nada sobre el estado de Credinet.
///
/// NUNCA LANZA. Es un adorno de la cabecera: si no se puede leer el estado de la
/// red, la caja tiene que seguir operando igual.
/// </summary>
public interface IEstadoDeRed
{
    /// <summary>
    /// true si el sistema dice que hay salida a internet. Ante cualquier duda
    /// devuelve true: un "sin conexion" falso en la cabecera haria que el cajero
    /// deje de cobrar teniendo red.
    /// </summary>
    bool HayConexion { get; }

    /// <summary>
    /// Avisa cuando el estado cambio, para que la cabecera se repinte sin tener
    /// que sondear.
    /// </summary>
    event EventHandler? Cambio;
}

/// <summary>
/// Implementacion de reserva: siempre en linea. La usan las pantallas cuando el
/// contenedor todavia no resolvio el servicio real —pasa durante el arranque— asi
/// que el control nunca tiene que comprobar null.
/// </summary>
public sealed class EstadoDeRedDesconocido : IEstadoDeRed
{
    public bool HayConexion => true;

    public event EventHandler? Cambio
    {
        add { }        // nunca cambia: no hay a quien avisar
        remove { }
    }
}
