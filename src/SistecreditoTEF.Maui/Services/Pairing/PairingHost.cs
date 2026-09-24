using System.Net;
using System.Net.Sockets;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// La caja que reparte. Tiene las tres responsabilidades que no pueden vivir en
/// otro lado, porque es la unica que conoce el codigo: la ventana, el conteo de
/// intentos y la entrega del sobre.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA FABRICA DEL SOBRE ES ASINCRONA A PROPOSITO
/// ─────────────────────────────────────────────────────────────────────────────
/// Del otro lado hay lectura de la BD cifrada (SQLCipher, llave en el Keystore).
/// Resolverla con <c>GetAwaiter().GetResult()</c> bloquea un hilo del pool en una
/// app que en ese mismo momento puede estar cobrando.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL SOBRE SE RELEE EN CADA ENTREGA
/// ─────────────────────────────────────────────────────────────────────────────
/// No se arma una vez al abrir la ventana. Entre la primera caja y la tercera el
/// administrador pudo dar de alta otro cajero, y la tercera tiene que recibir el
/// padron de verdad y no una foto de hace ocho minutos.
/// </summary>
public sealed class PairingHost : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<CashierRosterEnvelope?>> _factory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();

    private Task? _loop;
    private int _attemptsRemaining = PairingSecret.MaxAttempts;
    private int _successfulTransfers;

    public string Code { get; }
    public DateTimeOffset ExpiresAt { get; }
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int AttemptsRemaining
    {
        get { lock (_gate) return _attemptsRemaining; }
    }

    public int SuccessfulTransfers
    {
        get { lock (_gate) return _successfulTransfers; }
    }

    public bool IsOpen => AttemptsRemaining > 0 && _clock() < ExpiresAt;

    public TimeSpan TimeLeft
    {
        get
        {
            var left = ExpiresAt - _clock();
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    /// <param name="factory">
    /// Lee el padron vigente. Devuelve <c>null</c> si no se pudo leer, y entonces
    /// no se entrega nada: es preferible que la caja nueva reporte un fallo a que
    /// se quede con medio padron.
    /// </param>
    /// <param name="tienda">
    /// Nombre de tienda que se muestra en el saludo para que el operador confirme
    /// de donde esta copiando. Sale de la configuracion de CloudLicense.
    /// </param>
    /// <param name="port">
    /// 0 en las pruebas, para que el sistema asigne uno libre y varias corran en
    /// paralelo sin pelearse el puerto.
    /// </param>
    /// <param name="storeId">
    /// Id de tienda de CloudLicense. Viaja en el saludo —no en el sobre— para que la
    /// caja receptora pueda rechazar un padron de OTRA tienda. Ver la nota en
    /// [PairingGreeting]: es opcional porque las cajas con el APK viejo no lo mandan
    /// y tienen que seguir sirviendo de emisoras.
    /// </param>
    public PairingHost(
        Func<CancellationToken, Task<CashierRosterEnvelope?>> factory,
        string tienda,
        Func<DateTimeOffset>? clock = null,
        int port = PairingProtocol.Port,
        string? storeId = null)
    {
        _factory = factory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Tienda = tienda;
        StoreId = storeId;

        Code = PairingSecret.GenerateCode();
        ExpiresAt = _clock() + PairingProtocol.Window;

        _listener = new TcpListener(IPAddress.Any, port);
    }

    public string Tienda { get; }

    /// <summary>Id de tienda que se anuncia en el saludo, o null si no se configuro.</summary>
    public string? StoreId { get; }

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }

            // Cada par en su propia tarea: uno que se conecta y no habla no puede
            // dejar al siguiente esperando los 20 segundos del timeout.
            _ = Task.Run(() => HandleAsync(client, token), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(PairingProtocol.ConnectionTimeout);

        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var challenge = PairingSecret.GenerateChallenge();

                // EL SALUDO VA SIEMPRE, aunque ya sepamos que no vamos a entregar
                // nada. Ver la nota en [PairingProtocol].
                await PairingProtocol.SendAsync(stream, new PairingGreeting(
                    CashierRosterEnvelope.CurrentVersion,
                    Convert.ToBase64String(challenge),
                    Tienda,
                    await ContarCajerosAsync(timeout.Token),
                    StoreId), timeout.Token);

                var proof = await PairingProtocol.ReceiveAsync<PairingProof>(stream, timeout.Token);
                if (proof is null) return;   // se corto o mando algo ininteligible

                var rechazo = MotivoDeRechazo();
                if (rechazo is not null)
                {
                    await PairingProtocol.SendAsync(stream,
                        new PairingDelivery(null, rechazo, AttemptsRemaining), timeout.Token);
                    return;
                }

                byte[] recibida;
                try
                {
                    recibida = Convert.FromBase64String(proof.Proof ?? string.Empty);
                }
                catch (FormatException)
                {
                    recibida = [];
                }

                if (!PairingSecret.VerifyProof(Code, challenge, recibida))
                {
                    var restantes = QuemarIntento();
                    AppLogger.W("PairingHost",
                        $"Codigo incorrecto desde la red. Intentos restantes: {restantes}.");

                    await PairingProtocol.SendAsync(stream, new PairingDelivery(
                        null,
                        restantes > 0
                            ? PairingProtocol.ErrorInvalidCode
                            : PairingProtocol.ErrorNoAttemptsLeft,
                        restantes), timeout.Token);
                    return;
                }

                // Acerto. Se RELEE el padron: puede haber cambiado desde que se
                // abrio la ventana.
                var envelope = await _factory(timeout.Token);
                if (envelope is null)
                {
                    AppLogger.E("PairingHost",
                        "No se pudo leer el padron para entregarlo; no se envia nada.");
                    await PairingProtocol.SendAsync(stream,
                        new PairingDelivery(null, PairingProtocol.ErrorWindowClosed,
                            AttemptsRemaining), timeout.Token);
                    return;
                }

                var payload = PairingSecret.Encrypt(Code, challenge, envelope.ToJson());

                await PairingProtocol.SendAsync(stream, new PairingDelivery(
                    Convert.ToBase64String(payload),
                    Error: null,
                    AttemptsRemaining: AttemptsRemaining), timeout.Token);

                lock (_gate) _successfulTransfers++;

                AppLogger.I("PairingHost",
                    $"Padron entregado a otra caja ({envelope.Cajeros.Count} cajeros). " +
                    $"Transferencias en esta ventana: {SuccessfulTransfers}.");
            }
        }
        catch (OperationCanceledException)
        {
            // El par no hablo a tiempo, o se cerro la pantalla. No es un error.
        }
        catch (Exception ex)
        {
            // Esta tarea corre suelta: una excepcion que escape se lleva el proceso
            // por delante, en una caja que puede estar cobrando.
            AppLogger.E("PairingHost", "Fallo atendiendo a una caja que pedia el padron.", ex);
        }
    }

    private async Task<int> ContarCajerosAsync(CancellationToken token)
    {
        try
        {
            var envelope = await _factory(token);
            return envelope?.CajerosActivos ?? 0;
        }
        catch (Exception ex)
        {
            AppLogger.W("PairingHost", $"No se pudo contar los cajeros para el saludo: {ex.Message}");
            return 0;
        }
    }

    private string? MotivoDeRechazo()
    {
        if (_clock() >= ExpiresAt) return PairingProtocol.ErrorWindowClosed;
        if (AttemptsRemaining <= 0) return PairingProtocol.ErrorNoAttemptsLeft;
        return null;
    }

    /// <summary>
    /// Un intento fallido quema uno de los tres, venga de donde venga. Los ACIERTOS
    /// no consumen: la decision de operacion es que un codigo sirva para varias
    /// cajas dentro de la ventana, para que quien instala genere uno y camine la
    /// tienda sin volver a la primera caja.
    /// </summary>
    private int QuemarIntento()
    {
        lock (_gate)
        {
            if (_attemptsRemaining > 0) _attemptsRemaining--;
            return _attemptsRemaining;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try { _listener.Stop(); } catch (SocketException) { }

        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }
}
