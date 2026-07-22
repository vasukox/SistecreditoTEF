namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Persiste el Token UUID que HioPosCloud envia en INITIALIZE.
/// Se reutiliza en cada Broadcast AUDIT (doc §8.3 y §7).
///
/// POR QUE EXISTE (V8): no se acopla a Microsoft.Maui.Storage.Preferences
/// directamente para poder testear con fakes.
/// </summary>
public interface ITokenStore
{
    string? GetToken();
    void SetToken(string token);
    void Clear();
}
