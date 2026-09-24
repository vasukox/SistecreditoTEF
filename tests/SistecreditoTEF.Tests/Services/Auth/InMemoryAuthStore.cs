using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Pairing;

namespace SistecreditoTEF.Tests.Services.Auth;

/// <summary>
/// Almacenamiento de cajeros en memoria.
///
/// El real es SQLCipher con la llave en el Keystore de Android, que no corre en un
/// agente de pruebas. Este doble existe para que la logica de ingreso
/// ([AuthService]) y la replicacion entre cajas ([Services.Pairing]) se puedan
/// probar de verdad, sin dispositivo.
///
/// Reproduce las dos reglas del real que importan para esas pruebas:
///   • el padron se escribe COMPLETO al importar, no se mezcla;
///   • exportar devuelve null si la caja no esta completa (sin PIN o sin cajeros).
/// </summary>
public sealed class InMemoryAuthStore : IAuthStore
{
    private string? _adminHash;
    private readonly Dictionary<string, Cajero> _cajeros = new();

    public Task<string?> GetAdminPinHashAsync() => Task.FromResult(_adminHash);

    public Task SetAdminPinHashAsync(string hash)
    {
        _adminHash = hash;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Cajero>> GetCajerosAsync() =>
        Task.FromResult<IReadOnlyList<Cajero>>(_cajeros.Values.ToList());

    public Task<Cajero?> GetCajeroAsync(string id) =>
        Task.FromResult(_cajeros.TryGetValue(id, out var c) ? c : null);

    public Task GuardarCajeroAsync(Cajero cajero)
    {
        _cajeros[cajero.Id] = cajero;
        return Task.CompletedTask;
    }

    public Task EstablecerActivoAsync(string id, bool activo)
    {
        if (_cajeros.TryGetValue(id, out var c)) _cajeros[id] = c with { Activo = activo };
        return Task.CompletedTask;
    }

    public Task<CashierRosterEnvelope?> ExportarPadronAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminHash) || _cajeros.Count == 0)
            return Task.FromResult<CashierRosterEnvelope?>(null);

        return Task.FromResult<CashierRosterEnvelope?>(new CashierRosterEnvelope(
            _adminHash,
            _cajeros.Values.Select(CashierRosterEnvelope.From).ToList()));
    }

    public Task<bool> ImportarPadronAsync(CashierRosterEnvelope sobre)
    {
        _adminHash = sobre.AdminPinHash;
        EscribirPadron(sobre);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Reproduce la tercera regla del real: el padron se reemplaza completo, pero el
    /// PIN de administrador NO se toca salvo que lo pidan.
    /// </summary>
    public Task<bool> ActualizarCajerosAsync(CashierRosterEnvelope sobre, bool incluirPinAdmin)
    {
        if (incluirPinAdmin && !string.IsNullOrWhiteSpace(sobre.AdminPinHash))
            _adminHash = sobre.AdminPinHash;

        EscribirPadron(sobre);
        return Task.FromResult(true);
    }

    private void EscribirPadron(CashierRosterEnvelope sobre)
    {
        _cajeros.Clear();
        foreach (var c in sobre.Cajeros)
            _cajeros[c.Id] = CashierRosterEnvelope.To(c);
    }
}
