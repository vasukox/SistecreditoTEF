using System.Text;
using System.Text.Json;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// Los tres mensajes del dialogo, y el orden en que van.
///
/// <code>
///   caja nueva  →  se conecta
///   caja vieja  →  Saludo  { version, reto, tienda, cajeros }
///   caja nueva  →  Prueba  { prueba }
///   caja vieja  →  Entrega { sobre cifrado }  |  { error, intentosRestantes }
/// </code>
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL PUNTO CENTRAL: EL SOBRE ES LO ULTIMO
/// ─────────────────────────────────────────────────────────────────────────────
/// El padron cifrado se manda SOLO despues de que el otro lado demostro conocer el
/// codigo. Si viajara primero, cualquiera podria conectarse, guardarlo y probar el
/// millon de codigos en su casa: sin limite de intentos y sin que la tienda se
/// entere. Con este orden, quien cuenta los intentos es quien tiene el secreto.
///
/// El nombre de la tienda va en el saludo, ANTES de validar nada. No es secreto
/// —esta en la pantalla de configuracion— y sirve para que el operador confirme de
/// donde esta copiando: en un centro comercial puede haber otra sucursal de la
/// misma cadena en la misma red.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL SALUDO VA SIEMPRE, PASE LO QUE PASE
/// ─────────────────────────────────────────────────────────────────────────────
/// Aunque la ventana este vencida o los intentos agotados. Mandar el error sin el
/// saludo previo deja al cliente —que espera un saludo— leyendo un mensaje que no
/// entiende, y el operador ve "no se pudo interpretar la respuesta" cuando lo que
/// pasaba era, textualmente, que se le vencio la ventana.
/// </summary>
public static class PairingProtocol
{
    /// <summary>
    /// Puerto propio del emparejamiento. No es un puerto registrado: se eligio uno
    /// alto y fijo para que el instructivo de tienda pueda nombrarlo si hay que
    /// pedirle algo al area de redes.
    /// </summary>
    public const int Port = 47114;

    /// <summary>Puerto del descubrimiento por difusion (ver [PairingDiscovery]).</summary>
    public const int DiscoveryPort = 47115;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Sin esto, alguien que abra conexiones y las deje colgadas bloquea el
    /// emparejamiento de las cajas de verdad.
    /// </summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Lo que llega viene de la red: sin tope, un par malicioso manda bytes para
    /// siempre y tumba la caja por memoria.
    /// </summary>
    public const int MaxMessageBytes = 512 * 1024;

    public const string ErrorInvalidCode = "invalid_code";
    public const string ErrorNoAttemptsLeft = "no_attempts_left";
    public const string ErrorWindowClosed = "window_closed";

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// Los mensajes van como JSON, UNO POR LINEA, sobre el socket. No es la
    /// codificacion mas compacta, y da igual: esto ocurre una vez en la vida de
    /// cada caja, y poder leerlo desde una terminal cuando algo falle en una tienda
    /// vale mas que ahorrar cuatro bytes.
    /// </summary>
    public static async Task SendAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var line = JsonSerializer.Serialize(message, Json) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    /// <summary>
    /// Lee una linea y la interpreta. Devuelve <c>default</c> si la conexion se
    /// corto, si el mensaje supera el tope o si no se entiende: nunca lanza por
    /// contenido, solo propaga la cancelacion.
    /// </summary>
    public static async Task<T?> ReceiveAsync<T>(Stream stream, CancellationToken token)
    {
        var buffer = new List<byte>(256);
        var one = new byte[1];

        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(one, token);
            }
            catch (IOException)
            {
                return default;
            }

            if (read == 0) return default;                 // el otro lado cerro
            if (one[0] == (byte)'\n') break;
            if (one[0] == (byte)'\r') continue;

            buffer.Add(one[0]);
            if (buffer.Count > MaxMessageBytes) return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(buffer.ToArray()), Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>
/// Saludo de la caja que reparte. <c>Challenge</c> va en base64 porque el
/// transporte es JSON de texto.
/// </summary>
public sealed record PairingGreeting(
    int Version,
    string Challenge,
    string Tienda,
    int Cajeros);

/// <summary>Prueba de que la caja nueva conoce el codigo.</summary>
public sealed record PairingProof(string Proof);

/// <summary>
/// Entrega: o el sobre cifrado, o el motivo por el que no. Los intentos restantes
/// viajan SIEMPRE: el operador tiene que saber que le quedan dos antes de que el
/// codigo se queme, no descubrirlo cuando ya se quemo.
/// </summary>
public sealed record PairingDelivery(
    string? Payload,
    string? Error,
    int AttemptsRemaining);
