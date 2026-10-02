namespace SistecreditoTEF.Maui.Services.PantallaCliente;

/// <summary>
/// En que momento del tramite esta el cliente. Lo usa la pantalla para elegir
/// color y jerarquia, no para decidir que texto poner: los textos vienen ya
/// resueltos en [VitrinaCliente].
/// </summary>
public enum PasoDeLaVitrina
{
    /// <summary>Nada en curso: marca y bienvenida.</summary>
    Reposo,

    /// <summary>Se esta consultando a la persona. Todavia no sabemos quien es.</summary>
    Identificando,

    /// <summary>Ya sabemos quien es y se le saluda.</summary>
    Saludo,

    /// <summary>Se esta eligiendo el plan de cuotas.</summary>
    Plan,

    /// <summary>Se le pidio el codigo que llego por WhatsApp.</summary>
    Codigo,

    /// <summary>Cierre exitoso: credito aprobado o abono registrado.</summary>
    Cierre
}

/// <summary>
/// LO QUE VE EL CLIENTE EN LA PANTALLA DE ADELANTE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// ESTA PANTALLA ES PUBLICA
/// ─────────────────────────────────────────────────────────────────────────────
/// Son 11 pulgadas mirando al salon de ventas. No la ve solo el cliente: la ve
/// quien este haciendo fila detras, el de la caja de al lado y cualquiera que
/// pase. Por eso este registro es una LISTA CERRADA de campos de texto ya
/// resueltos, y no un espejo del estado de la app:
///
///   · NUNCA lleva la cedula, el telefono, el correo ni el nombre completo.
///     Del nombre va solo el de pila, que es lo que el asesor ya dice en voz alta.
///   · NUNCA lleva el codigo OTP. Es la clave con la que se firma un credito a
///     nombre de esa persona; mostrarlo en una pantalla publica lo regala.
///   · SI llevan los importes. Ese es justamente el motivo de la pantalla: que
///     el cliente pueda verificar cuanto se le esta cobrando sin tener que
///     pedirle el terminal al asesor.
///
/// Quien agregue un campo aca tiene que poder responder "¿y si esto lo lee un
/// desconocido a tres metros?".
/// </summary>
/// <param name="Paso">Momento del tramite; define el tratamiento visual.</param>
/// <param name="Titulo">Frase principal, en letra grande.</param>
/// <param name="Subtitulo">Aclaracion en segunda linea. Puede faltar.</param>
/// <param name="EtiquetaDelMonto">Que significa el importe ("Valor de la compra").</param>
/// <param name="Monto">Importe ya formateado en pesos. Puede faltar.</param>
/// <param name="Aviso">Nota al pie: plazo elegido, saldo nuevo, advertencia.</param>
public sealed record VitrinaCliente(
    PasoDeLaVitrina Paso,
    string Titulo,
    string? Subtitulo = null,
    string? EtiquetaDelMonto = null,
    string? Monto = null,
    string? Aviso = null)
{
    /// <summary>
    /// Pantalla neutra. Es el DESTINO POR DEFECTO de cualquier situacion que el
    /// guion no reconozca, y tambien lo que se muestra en las pantallas de
    /// administracion: el cliente no tiene por que ver el padron de cajeros ni el
    /// codigo de emparejamiento entre cajas.
    /// </summary>
    public static readonly VitrinaCliente Reposo = new(
        PasoDeLaVitrina.Reposo,
        "Sistecrédito",
        "Compre hoy y pague en cuotas");

    /// <summary>¿Hay un importe para destacar?</summary>
    public bool TieneMonto => !string.IsNullOrWhiteSpace(Monto);
}
