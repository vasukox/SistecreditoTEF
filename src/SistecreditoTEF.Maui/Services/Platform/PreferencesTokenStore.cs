using Microsoft.Maui.Storage;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion de [ITokenStore] sobre MAUI Preferences.
/// Singleton: una sola key, un solo valor durante la sesion de HioPosCloud.
/// </summary>
public class PreferencesTokenStore : ITokenStore
{
    private const string Key = "hiopos.token";

    public string? GetToken() =>
        Preferences.Default.Get<string?>(Key, null);

    public void SetToken(string token)
    {
        Preferences.Default.Set(Key, token);
        AppLogger.I("ITokenStore", "Token persistido.");
    }

    public void Clear()
    {
        Preferences.Default.Remove(Key);
    }
}
