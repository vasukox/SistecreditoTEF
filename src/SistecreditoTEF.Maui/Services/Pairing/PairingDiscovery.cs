using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>Lo que responde una caja que esta repartiendo, al ser buscada.</summary>
public sealed record PairingBeacon(string Tienda, int Port);

/// <summary>Una caja encontrada en la red.</summary>
public sealed record CajaEncontrada(string Address, string Tienda, int Port);

/// <summary>
/// Difusion UDP para no tener que teclear la IP. ES UN ATAJO, NO UN REQUISITO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO MAS IMPORTANTE DE ESTE ARCHIVO
/// ─────────────────────────────────────────────────────────────────────────────
/// La difusion puede estar bloqueada aunque las cajas se vean entre si. Que dos
/// equipos se respondan el ping no dice NADA sobre si el switch o el punto de
/// acceso dejan pasar broadcast: son cosas distintas, que se configuran por
/// separado.
///
/// Por eso la pantalla emisora muestra SIEMPRE su IP, no solo cuando algo falla.
/// Un atajo que falla en silencio y deja al que instala sin salida es peor que no
/// tener atajo.
///
/// La respuesta lleva el nombre de la tienda y el puerto. Nada mas: contesta a
/// cualquiera que este en la red.
/// </summary>
public sealed class PairingDiscovery : IAsyncDisposable
{
    private const string Question = "SISTECREDITO-PAIRING?";

    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _responder;
    private Task? _loop;

    /// <summary>Empieza a responder "aca estoy". Lo llama la caja que reparte.</summary>
    public void StartResponding(string tienda, int port)
    {
        try
        {
            _responder = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            _responder.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _responder.Client.Bind(new IPEndPoint(IPAddress.Any, PairingProtocol.DiscoveryPort));

            _loop = Task.Run(() => ResponderLoopAsync(tienda, port, _cts.Token));
        }
        catch (SocketException ex)
        {
            // El descubrimiento es un atajo: si el puerto esta ocupado o la red no
            // deja, el emparejamiento sigue funcionando tecleando la IP.
            AppLogger.W("PairingDiscovery",
                $"No se pudo abrir el descubrimiento ({ex.SocketErrorCode}); se usara la IP a mano.");
        }
    }

    private async Task ResponderLoopAsync(string tienda, int port, CancellationToken token)
    {
        var respuesta = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new PairingBeacon(tienda, port)));

        while (!token.IsCancellationRequested)
        {
            try
            {
                var recibido = await _responder!.ReceiveAsync(token);
                if (Encoding.UTF8.GetString(recibido.Buffer) != Question) continue;

                await _responder.SendAsync(respuesta, respuesta.Length, recibido.RemoteEndPoint);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { /* un datagrama perdido no cierra el servicio */ }
        }
    }

    /// <summary>
    /// Busca cajas repartiendo. Devuelve lista vacia si no hay ninguna o si la
    /// difusion esta bloqueada: las dos cosas se ven igual desde aca, y las dos se
    /// resuelven tecleando la IP.
    /// </summary>
    public static async Task<IReadOnlyList<CajaEncontrada>> BuscarAsync(
        TimeSpan espera, CancellationToken token = default)
    {
        var encontradas = new Dictionary<string, CajaEncontrada>();

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var pregunta = Encoding.UTF8.GetBytes(Question);

            await udp.SendAsync(pregunta, pregunta.Length,
                new IPEndPoint(IPAddress.Broadcast, PairingProtocol.DiscoveryPort));

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(espera);

            while (!deadline.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(deadline.Token);
                var beacon = JsonSerializer.Deserialize<PairingBeacon>(
                    Encoding.UTF8.GetString(r.Buffer));

                if (beacon is null) continue;

                var ip = r.RemoteEndPoint.Address.ToString();
                encontradas[ip] = new CajaEncontrada(ip, beacon.Tienda, beacon.Port);
            }
        }
        catch (OperationCanceledException) { /* se agoto la espera: normal */ }
        catch (SocketException ex)
        {
            AppLogger.W("PairingDiscovery", $"Busqueda fallida ({ex.SocketErrorCode}).");
        }
        catch (JsonException) { /* alguien mas contesta en ese puerto */ }

        return encontradas.Values.ToList();
    }

    /// <summary>
    /// IP propia, la que las otras cajas van a poder alcanzar.
    ///
    /// "Conecta" un socket UDP a una direccion externa: no manda un solo byte ni
    /// necesita internet, solo hace que el sistema elija por cual interfaz saldria.
    /// Recorrer las interfaces a mano devuelve varias (Wi-Fi, datos, virtuales) y
    /// no dice cual sirve.
    /// </summary>
    public static string? LocalAddress()
    {
        try
        {
            using var probe = new UdpClient();
            probe.Connect("10.255.255.255", 65530);
            return (probe.Client.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _responder?.Dispose();

        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }
}
