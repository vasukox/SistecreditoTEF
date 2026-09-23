using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Maui.Services.Platform;
using SQLite;

namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Persistencia del PIN de administrador y de los cajeros, en la BD CIFRADA del
/// terminal (SQLCipher, llave en el Android Keystore).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ACA Y NO EN Preferences
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>Preferences</c> es SharedPreferences: texto plano en el sandbox de la app.
/// En un terminal rooteado o con una copia del /data, la lista de cajeros y sus
/// derivaciones quedarian legibles. Los hashes no son reversibles, pero saber que
/// cajeros existen y poder atacar sus derivaciones offline sin limite de intentos
/// es exactamente lo que la BD cifrada evita.
///
/// Se usa el MISMO archivo que la idempotencia: una sola llave, una sola BD que
/// mantener, y ya esta resuelto el caso de la llave ilegible.
///
/// La inicializacion sigue el patron de [SqliteIdempotencyStore]: un
/// <c>SemaphoreSlim</c> y no un <c>Lazy&lt;Task&gt;</c>, porque un Lazy cachea la
/// task FALLIDA y un bloqueo momentaneo de la BD al arrancar dejaria al terminal
/// sin poder validar cajeros por el resto del dia.
/// </summary>
public class SqliteAuthStore : IAuthStore
{
    private const string DbName = "idempotency_sec.db3";
    private const string AdminPinKey = "admin_pin";

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        var existing = _connection;
        if (existing is not null) return existing;

        await _initGate.WaitAsync();
        try
        {
            if (_connection is not null) return _connection;

            var conn = await SecureDb.OpenAsync<AuthSettingRow>(DbName);
            await conn.CreateTableAsync<CajeroRow>();

            _connection = conn;
            AppLogger.I("IAuthStore", "Tablas de cajeros inicializadas en la BD cifrada.");
            return conn;
        }
        finally
        {
            _initGate.Release();
        }
    }

    // ------------------------------------------------------------------
    // PIN de administrador
    // ------------------------------------------------------------------

    public async Task<string?> GetAdminPinHashAsync()
    {
        try
        {
            var conn = await GetConnectionAsync();
            var row = await conn.FindAsync<AuthSettingRow>(AdminPinKey);
            return row?.Value;
        }
        catch (Exception ex)
        {
            // Falla cerrado: si no se puede leer, se responde "no hay PIN" y la app
            // abre la configuracion inicial. Es preferible a dejar pasar a alguien
            // porque la verificacion no se pudo hacer.
            AppLogger.E("IAuthStore", "No se pudo leer el PIN de administrador.", ex);
            return null;
        }
    }

    public async Task SetAdminPinHashAsync(string hash)
    {
        var conn = await GetConnectionAsync();
        await conn.InsertOrReplaceAsync(new AuthSettingRow { Key = AdminPinKey, Value = hash });
    }

    // ------------------------------------------------------------------
    // Cajeros
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<Cajero>> GetCajerosAsync()
    {
        try
        {
            var conn = await GetConnectionAsync();
            var rows = await conn.Table<CajeroRow>().ToListAsync();
            return rows.Select(ToDomain).ToList();
        }
        catch (Exception ex)
        {
            AppLogger.E("IAuthStore", "No se pudo leer la lista de cajeros.", ex);
            return [];
        }
    }

    public async Task<Cajero?> GetCajeroAsync(string id)
    {
        try
        {
            var conn = await GetConnectionAsync();
            var row = await conn.FindAsync<CajeroRow>(id);
            return row is null ? null : ToDomain(row);
        }
        catch (Exception ex)
        {
            AppLogger.E("IAuthStore", "No se pudo leer el cajero.", ex);
            return null;
        }
    }

    public async Task GuardarCajeroAsync(Cajero cajero)
    {
        var conn = await GetConnectionAsync();
        await conn.InsertOrReplaceAsync(new CajeroRow
        {
            Id = cajero.Id,
            Usuario = cajero.Usuario,
            Nombre = cajero.Nombre,
            ClaveHash = cajero.ClaveHash,
            Activo = cajero.Activo,
            CreadoEn = cajero.CreadoEn
        });
    }

    public async Task EstablecerActivoAsync(string id, bool activo)
    {
        var conn = await GetConnectionAsync();
        var row = await conn.FindAsync<CajeroRow>(id);
        if (row is null) return;

        // Se marca, no se borra: los abonos que hizo quedan referenciados por su
        // nombre y perder quien cobro deja un hueco en la trazabilidad.
        row.Activo = activo;
        await conn.UpdateAsync(row);
    }

    // ------------------------------------------------------------------
    // Replicacion entre cajas
    // ------------------------------------------------------------------

    public async Task<CashierRosterEnvelope?> ExportarPadronAsync()
    {
        try
        {
            var pin = await GetAdminPinHashAsync();
            if (string.IsNullOrWhiteSpace(pin)) return null;

            var cajeros = await GetCajerosAsync();
            if (cajeros.Count == 0) return null;

            return new CashierRosterEnvelope(
                pin,
                cajeros.Select(CashierRosterEnvelope.From).ToList());
        }
        catch (Exception ex)
        {
            AppLogger.E("IAuthStore", "No se pudo armar el padron para replicar.", ex);
            return null;
        }
    }

    public async Task<bool> ImportarPadronAsync(CashierRosterEnvelope sobre)
    {
        ArgumentNullException.ThrowIfNull(sobre);

        try
        {
            var conn = await GetConnectionAsync();

            // El PIN primero: si algo falla despues, la caja queda sin cajeros pero
            // con administrador, y se puede terminar a mano. Al reves —cajeros sin
            // PIN— la pantalla de administracion no se puede abrir para arreglarlo.
            await conn.InsertOrReplaceAsync(new AuthSettingRow
            {
                Key = AdminPinKey,
                Value = sobre.AdminPinHash ?? string.Empty
            });

            // Completo, no mezclado. Ver la nota en [IAuthStore.ImportarPadronAsync].
            await conn.DeleteAllAsync<CajeroRow>();

            foreach (var c in sobre.Cajeros)
            {
                await conn.InsertOrReplaceAsync(new CajeroRow
                {
                    Id = c.Id,
                    Usuario = c.Usuario,
                    Nombre = c.Nombre,
                    ClaveHash = c.ClaveHash,
                    Activo = c.Activo,
                    CreadoEn = c.CreadoEn
                });
            }

            AppLogger.I("IAuthStore",
                $"Padron recibido de otra caja: {sobre.Cajeros.Count} cajeros escritos.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.E("IAuthStore", "No se pudo escribir el padron recibido.", ex);
            return false;
        }
    }

    private static Cajero ToDomain(CajeroRow r) =>
        new(r.Id, r.Usuario, r.Nombre, r.ClaveHash, r.Activo, r.CreadoEn);

    // ------------------------------------------------------------------
    // Filas
    // ------------------------------------------------------------------

    /// <summary>Clave/valor para ajustes de autenticacion (hoy solo el PIN admin).</summary>
    [Table("auth_settings")]
    private sealed class AuthSettingRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    [Table("cajeros")]
    private sealed class CajeroRow
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Usuario de ingreso. Indexado porque el login busca por este campo.
        ///
        /// Los registros creados antes de que existiera este campo quedan con cadena
        /// vacia: SQLite-net agrega la columna pero no puede inventar valores.
        /// [AuthService] tiene un respaldo que los deja ingresar por su nombre, para
        /// que una actualizacion no deje cajeros sin poder entrar.
        /// </summary>
        [Indexed]
        public string Usuario { get; set; } = string.Empty;

        /// <summary>Nombre para mostrar y para el comprobante.</summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Derivacion PBKDF2, nunca la clave. Ver [PasswordHasher].</summary>
        public string ClaveHash { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime CreadoEn { get; set; }
    }
}
