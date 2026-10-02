namespace SistecreditoTEF.Maui.Services.PantallaCliente;

/// <summary>
/// La segunda pantalla del terminal: la de 11 pulgadas que mira al cliente.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// REGLA DE ORO DE ESTA INTERFAZ
/// ─────────────────────────────────────────────────────────────────────────────
/// NINGUNA implementacion puede lanzar. Esto es un adorno colgado del camino por
/// el que pasa una venta a credito: si la pantalla del cliente no se puede abrir,
/// si el terminal no tiene segunda pantalla, si el fabricante la maneja con su
/// propio servicio y no nos deja pintar — todo eso se registra en el log y la
/// venta sigue exactamente igual.
///
/// Una excepcion aca seria un cobro perdido por un cartel.
/// </summary>
public interface IPantallaCliente
{
    /// <summary>
    /// ¿Se encontro una segunda pantalla donde pintar? Es informativo (sirve para
    /// el log y para el diagnostico en terminal); no hay que consultarlo antes de
    /// [Mostrar].
    /// </summary>
    bool Disponible { get; }

    /// <summary>
    /// Pinta este contenido. Idempotente: llamarlo dos veces con lo mismo no
    /// parpadea. Si no hay segunda pantalla, no hace nada.
    /// </summary>
    void Mostrar(VitrinaCliente vitrina);

    /// <summary>
    /// Suelta la segunda pantalla. Se llama cuando nuestra app deja de estar al
    /// frente: a partir de ahi la pantalla del cliente vuelve a ser de quien la
    /// tenia —normalmente HioPos—, que es lo correcto porque el cliente ya no esta
    /// tratando con nosotros.
    /// </summary>
    void Ocultar();
}

/// <summary>
/// Implementacion para cuando no hay plataforma (pruebas, y cualquier TFM que no
/// sea Android). Existe para que nadie tenga que comprobar null antes de mostrar.
/// </summary>
public sealed class PantallaClienteNula : IPantallaCliente
{
    public bool Disponible => false;

    public void Mostrar(VitrinaCliente vitrina) { /* no hay a donde */ }

    public void Ocultar() { /* no hay que soltar */ }
}
