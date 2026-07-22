using System.Security.Cryptography;
using SQLite;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973: abre conexiones SQLite CIFRADAS (SQLCipher). La llave de cifrado
/// se genera al azar en el primer arranque y se guarda en [SecureStorage]
/// (respaldado por Android Keystore / EncryptedSharedPreferences), nunca en
/// el código ni en disco en claro.
///
/// Resiliencia: si la BD no se puede abrir con la llave (p. ej. la llave se
/// perdió, o quedó un archivo en claro de una versión anterior), se elimina y
/// se recrea vacía en vez de crashear (los datos locales son cache/auditoría,
/// la fuente de verdad es CREDINET).
/// </summary>
public static class SecureDb
{
    private const string KeyStorageName = "sc_db_key_v1";
    private static readonly SemaphoreSlim _keyGate = new(1, 1);
    private static string? _cachedKey;

    public static async Task<SQLiteAsyncConnection> OpenAsync<T>(string fileName) where T : new()
    {
        var conn = await OpenRawAsync(fileName);
        try
        {
            await conn.CreateTableAsync<T>();
            return conn;
        }
        catch (Exception ex)
        {
            // BD ilegible (llave distinta o archivo en claro previo): recrear.
            AppLogger.E("SecureDb", $"No se pudo abrir {fileName} cifrada, se recrea: {ex.Message}", ex);
            try { await conn.CloseAsync(); } catch { /* ignore */ }
            DeleteFile(fileName);
            conn = await OpenRawAsync(fileName);
            await conn.CreateTableAsync<T>();
            return conn;
        }
    }

    /// <summary>Borra un archivo de BD en claro heredado de versiones sin cifrado.</summary>
    public static void DeleteLegacyPlaintext(string fileName) => DeleteFile(fileName);

    private static async Task<SQLiteAsyncConnection> OpenRawAsync(string fileName)
    {
        AppLogger.I("SecureDb", $"OpenRaw start {fileName}");
        var key = await GetOrCreateKeyAsync();
        AppLogger.I("SecureDb", $"key listo (len={key.Length})");
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        var options = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
        var conn = new SQLiteAsyncConnection(options);
        AppLogger.I("SecureDb", $"conexion creada {path}");
        return conn;
    }

    private static void DeleteFile(string fileName)
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }

    private static async Task<string> GetOrCreateKeyAsync()
    {
        if (_cachedKey is not null) return _cachedKey;
        await _keyGate.WaitAsync();
        try
        {
            if (_cachedKey is not null) return _cachedKey;

            string? existing = null;
            // Timeout: SecureStorage puede colgarse en algunos emuladores/POS sin
            // lock screen. Si no responde en 3s, seguimos con llave generada.
            try
            {
                AppLogger.I("SecureDb", "SecureStorage.Get...");
                existing = await SecureStorage.Default.GetAsync(KeyStorageName).WaitAsync(TimeSpan.FromSeconds(3));
                AppLogger.I("SecureDb", $"SecureStorage.Get ok (vacio={string.IsNullOrEmpty(existing)})");
            }
            catch (Exception ex) { AppLogger.W("SecureDb", $"SecureStorage.Get falló/timeout: {ex.Message}"); }

            if (string.IsNullOrEmpty(existing))
            {
                existing = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                try
                {
                    await SecureStorage.Default.SetAsync(KeyStorageName, existing).WaitAsync(TimeSpan.FromSeconds(3));
                    AppLogger.I("SecureDb", "SecureStorage.Set ok");
                }
                catch (Exception ex) { AppLogger.W("SecureDb", $"SecureStorage.Set falló/timeout: {ex.Message}"); }
            }

            _cachedKey = existing;
            return existing;
        }
        finally { _keyGate.Release(); }
    }
}
