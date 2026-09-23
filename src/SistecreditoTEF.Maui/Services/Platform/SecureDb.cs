using System.Security.Cryptography;
using SQLite;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Abre conexiones SQLite CIFRADAS (SQLCipher). La llave se genera al azar en el
/// primer arranque y se guarda en [SecureStorage] (Android Keystore), nunca en el
/// codigo ni en disco en claro.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA A-6 — EL BUG QUE CORRIGE
/// ─────────────────────────────────────────────────────────────────────────────
/// La version anterior no distinguia "no habia llave" de "no pude LEER la
/// llave":
/// <code>
///   try { existing = await SecureStorage...GetAsync(...).WaitAsync(3s); }
///   catch { /* solo log */ }              // timeout -> existing queda null
///   if (string.IsNullOrEmpty(existing))
///       existing = ...GetBytes(32);        // GENERA UNA LLAVE NUEVA
/// </code>
/// Si SecureStorage existia pero tardaba mas de 3 s, se generaba una llave
/// nueva, la BD cifrada con la llave vieja ya no se podia abrir, y
/// <c>OpenAsync</c> la **borraba y la recreaba vacia**. Resultado: se perdian de
/// golpe el store de idempotencia (quedando solo la barrera remota) y TODA la
/// auditoria financiera local, sin alerta y sin recuperacion.
///
/// Ahora:
///   1. La lectura de SecureStorage se reintenta con backoff antes de rendirse.
///   2. Se distingue llave AUTORITATIVA (leida/escrita con exito) de llave
///      provisional. Con llave provisional NO se borra nada: se falla la
///      apertura y el llamador degrada.
///   3. El borrado de la BD solo ocurre con llave autoritativa y ante un error
///      compatible con "archivo no descifrable", no ante cualquier excepcion
///      (antes un disco lleno o una BD bloqueada tambien disparaba el borrado).
///   4. La recreacion queda registrada como ERROR, no como detalle.
/// </summary>
public static class SecureDb
{
    private const string KeyStorageName = "sc_db_key_v1";
    private const int KeyReadAttempts = 3;

    private static readonly SemaphoreSlim _keyGate = new(1, 1);
    private static DbKey? _cachedKey;

    /// <summary>Llave de cifrado y si su origen es confiable.</summary>
    private sealed record DbKey(string Value, bool IsAuthoritative);

    public static async Task<SQLiteAsyncConnection> OpenAsync<T>(string fileName) where T : new()
    {
        var key = await GetOrCreateKeyAsync();
        var conn = OpenRaw(fileName, key.Value);

        try
        {
            await conn.CreateTableAsync<T>();
            return conn;
        }
        catch (Exception ex)
        {
            try { await conn.CloseAsync(); } catch { /* ignore */ }

            if (!key.IsAuthoritative)
            {
                // No sabemos si la llave es la correcta: borrar seria destruir
                // datos validos por un problema transitorio del Keystore.
                AppLogger.E("SecureDb",
                    $"No se pudo abrir {fileName} y la llave NO es confiable " +
                    "(SecureStorage no respondio). NO se borra la BD; se reintentara " +
                    "en el proximo arranque.", ex);
                throw;
            }

            if (!LooksUndecryptable(ex))
            {
                // Disco lleno, BD bloqueada, permisos: no es un problema de llave.
                AppLogger.E("SecureDb",
                    $"No se pudo abrir {fileName} por un error que NO parece de cifrado. " +
                    "NO se borra la BD.", ex);
                throw;
            }

            AppLogger.E("SecureDb",
                $"La BD {fileName} no es descifrable con la llave actual; se RECREA VACIA. " +
                "Se pierden los datos locales previos (idempotencia/auditoria).", ex);

            DeleteFile(fileName);
            var fresh = OpenRaw(fileName, key.Value);
            await fresh.CreateTableAsync<T>();
            return fresh;
        }
    }

    /// <summary>Borra un archivo de BD en claro heredado de versiones sin cifrado.</summary>
    public static void DeleteLegacyPlaintext(string fileName) => DeleteFile(fileName);

    /// <summary>
    /// Heuristica: SQLCipher devuelve "file is not a database" / NOTADB cuando la
    /// llave no corresponde. Cualquier otra cosa es un problema distinto.
    /// </summary>
    private static bool LooksUndecryptable(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("not a database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("NOTADB", StringComparison.OrdinalIgnoreCase)
            || message.Contains("file is encrypted", StringComparison.OrdinalIgnoreCase)
            || message.Contains("malformed", StringComparison.OrdinalIgnoreCase);
    }

    private static SQLiteAsyncConnection OpenRaw(string fileName, string key)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        var options = new SQLiteConnectionString(path, storeDateTimeAsTicks: true, key: key);
        AppLogger.I("SecureDb", $"Abriendo BD cifrada {fileName}.");
        return new SQLiteAsyncConnection(options);
    }

    private static void DeleteFile(string fileName)
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLogger.W("SecureDb", $"No se pudo borrar {fileName}: {ex.Message}");
        }
    }

    private static async Task<DbKey> GetOrCreateKeyAsync()
    {
        if (_cachedKey is { } cached) return cached;

        await _keyGate.WaitAsync();
        try
        {
            if (_cachedKey is { } inner) return inner;

            // 1) Intentar LEER la llave existente, con reintentos. SecureStorage
            //    puede tardar en POS sin lock screen o recien arrancados.
            var (existing, readOk) = await TryReadKeyAsync();

            if (!string.IsNullOrEmpty(existing))
            {
                _cachedKey = new DbKey(existing, IsAuthoritative: true);
                return _cachedKey;
            }

            if (!readOk)
            {
                // No pudimos leer: puede haber una llave valida ahi. Usamos una
                // provisional SOLO para esta sesion y marcamos que no es
                // confiable, para que OpenAsync no borre nada.
                AppLogger.E("SecureDb",
                    "SecureStorage no respondio tras varios intentos. Se usa una llave " +
                    "PROVISIONAL: la BD local podria no abrirse en esta sesion, pero NO " +
                    "se destruira.");
                _cachedKey = new DbKey(GenerateKey(), IsAuthoritative: false);
                return _cachedKey;
            }

            // 2) Leimos bien y no habia llave: primer arranque. Generamos y
            //    persistimos.
            var fresh = GenerateKey();
            var writeOk = await TryWriteKeyAsync(fresh);
            if (!writeOk)
            {
                AppLogger.W("SecureDb",
                    "No se pudo persistir la llave en SecureStorage; la BD de esta sesion " +
                    "no sera legible en el proximo arranque.");
            }

            _cachedKey = new DbKey(fresh, IsAuthoritative: writeOk);
            return _cachedKey;
        }
        finally
        {
            _keyGate.Release();
        }
    }

    /// <summary>
    /// Devuelve (llave, lecturaExitosa). <c>lecturaExitosa=false</c> significa que
    /// SecureStorage no respondio: NO se puede concluir que no haya llave.
    /// </summary>
    private static async Task<(string? Key, bool ReadOk)> TryReadKeyAsync()
    {
        for (var attempt = 1; attempt <= KeyReadAttempts; attempt++)
        {
            try
            {
                var value = await SecureStorage.Default.GetAsync(KeyStorageName)
                    .WaitAsync(TimeSpan.FromSeconds(3));
                return (value, true);
            }
            catch (Exception ex)
            {
                AppLogger.W("SecureDb",
                    $"SecureStorage.Get intento {attempt}/{KeyReadAttempts} fallo: {ex.Message}");
                if (attempt < KeyReadAttempts)
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
            }
        }
        return (null, false);
    }

    private static async Task<bool> TryWriteKeyAsync(string key)
    {
        try
        {
            await SecureStorage.Default.SetAsync(KeyStorageName, key)
                .WaitAsync(TimeSpan.FromSeconds(3));
            AppLogger.I("SecureDb", "Llave de cifrado creada y persistida.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.W("SecureDb", $"SecureStorage.Set fallo: {ex.Message}");
            return false;
        }
    }

    private static string GenerateKey() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
