using System.Net.Sockets;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// Por que no se pudo copiar. Se modela explicito, como
/// <see cref="Auth.ResultadoIngreso"/>, para que la pantalla pueda decir QUE paso
/// sin inventarlo.
/// </summary>
public enum PairingOutcome
{
    Succeeded,

    /// <summary>No esta repartiendo, o la red no deja llegar.</summary>
    Unreachable,

    InvalidCode,
    NoAttemptsLeft,
    WindowClosed,

    /// <summary>Casi siempre: versiones distintas del APK entre las dos cajas.</summary>
    Unintelligible
}

public sealed record PairingResult(
    PairingOutcome Outcome,
    CashierRosterEnvelope? Envelope = null,
    int AttemptsRemaining = 0,
    string? Tienda = null,
    string? StoreId = null)
{
    public bool Succeeded => Outcome == PairingOutcome.Succeeded && Envelope is not null;
}

/// <summary>
/// La caja nueva. TODO problema sale como valor, NUNCA como excepcion.
///
/// Esto corre mientras alguien esta montando una tienda; una excepcion sin atrapar
/// ahi es una app que se cierra dejando la caja a medio configurar, y el operador
/// sin saber si el padron quedo escrito o no.
/// </summary>
public sealed class PairingClient
{
    /// <summary>
    /// Pide el padron a la caja que esta repartiendo. No escribe nada: quien decide
    /// guardar es la pantalla, DESPUES de que el operador confirme de que tienda es
    /// (ver [ReplicacionViewModel]).
    /// </summary>
    public async Task<PairingResult> FetchAsync(
        string host, string code, int port = PairingProtocol.Port,
        CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(PairingProtocol.ConnectionTimeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);

            await using var stream = client.GetStream();

            var greeting = await PairingProtocol.ReceiveAsync<PairingGreeting>(stream, timeout.Token);
            if (greeting is null || string.IsNullOrWhiteSpace(greeting.Challenge))
                return new PairingResult(PairingOutcome.Unintelligible);

            // Version distinta: el sobre no se interpreta a medias.
            if (greeting.Version != CashierRosterEnvelope.CurrentVersion)
                return new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda);

            byte[] challenge;
            try
            {
                challenge = Convert.FromBase64String(greeting.Challenge);
            }
            catch (FormatException)
            {
                return new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda);
            }

            var proof = PairingSecret.ComputeProof(code, challenge);
            await PairingProtocol.SendAsync(stream,
                new PairingProof(Convert.ToBase64String(proof)), timeout.Token);

            var delivery = await PairingProtocol.ReceiveAsync<PairingDelivery>(stream, timeout.Token);
            if (delivery is null)
                return new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda);

            if (delivery.Error is not null)
                return new PairingResult(
                    delivery.Error switch
                    {
                        PairingProtocol.ErrorInvalidCode => PairingOutcome.InvalidCode,
                        PairingProtocol.ErrorNoAttemptsLeft => PairingOutcome.NoAttemptsLeft,
                        PairingProtocol.ErrorWindowClosed => PairingOutcome.WindowClosed,
                        _ => PairingOutcome.Unintelligible
                    },
                    AttemptsRemaining: delivery.AttemptsRemaining,
                    Tienda: greeting.Tienda);

            if (string.IsNullOrWhiteSpace(delivery.Payload))
                return new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda);

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(delivery.Payload);
            }
            catch (FormatException)
            {
                return new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda);
            }

            var json = PairingSecret.Decrypt(code, challenge, payload);
            var envelope = CashierRosterEnvelope.FromJson(json);

            return envelope is null
                ? new PairingResult(PairingOutcome.Unintelligible, Tienda: greeting.Tienda)
                : new PairingResult(PairingOutcome.Succeeded, envelope,
                    delivery.AttemptsRemaining, greeting.Tienda, greeting.StoreId);
        }
        catch (OperationCanceledException)
        {
            return new PairingResult(PairingOutcome.Unreachable);
        }
        catch (SocketException)
        {
            return new PairingResult(PairingOutcome.Unreachable);
        }
        catch (IOException)
        {
            return new PairingResult(PairingOutcome.Unreachable);
        }
        catch (Exception ex)
        {
            // Red de seguridad: cualquier cosa inesperada tambien sale como valor.
            AppLogger.E("PairingClient", "Fallo inesperado copiando el padron.", ex);
            return new PairingResult(PairingOutcome.Unintelligible);
        }
    }

    /// <summary>
    /// Cada motivo traducido a los terminos del que esta instalando. Un
    /// "Unintelligible" en pantalla no le dice a nadie que hacer a continuacion.
    /// </summary>
    public static string Describir(PairingResult result) => result.Outcome switch
    {
        PairingOutcome.Succeeded =>
            "Configuracion recibida.",

        PairingOutcome.InvalidCode =>
            $"El codigo no es el que muestra la otra caja. Quedan {result.AttemptsRemaining} intentos.",

        PairingOutcome.NoAttemptsLeft =>
            "Se agotaron los intentos. En la otra caja, genera un codigo nuevo.",

        PairingOutcome.WindowClosed =>
            "La ventana de la otra caja se cerro. Genera un codigo nuevo alla.",

        PairingOutcome.Unreachable =>
            "No se pudo llegar a esa caja. Revisa la direccion y que siga mostrando el codigo.",

        _ =>
            "La otra caja respondio algo que esta no entiende. Suele ser que tienen versiones " +
            "distintas del modulo: actualiza las dos."
    };
}
