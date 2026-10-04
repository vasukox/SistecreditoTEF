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

            // ─────────────────────────────────────────────────────────────────
            // VIAJAN TODOS: ACTIVOS Y DE BAJA, CON SU CLAVE
            // ─────────────────────────────────────────────────────────────────
            // [GetCajerosAsync] devuelve el padron ENTERO y aqui no se filtra
            // nada. Es deliberado y conviene que quede escrito, porque "mandar
            // solo los que pueden entrar" suena razonable y romperia dos cosas:
            //
            //   · Al que esta de baja no se lo podria REACTIVAR en la caja nueva
            //     —no estaria—, y tampoco recrearlo con su mismo usuario sin
            //     chocar contra el historico de la otra caja.
            //
            //   · Los abonos viejos quedan referenciados por el nombre de quien
            //     cobro. Si esa fila no viaja, la caja nueva no puede nombrar a
            //     quien hizo un recaudo que si esta en Sistecredito.
            //
            // La clave viaja como DERIVACION, nunca en claro: el salt y las
            // iteraciones van dentro de la cadena ([PasswordHasher]), asi que la
            // misma clave de siempre funciona en la caja que recibe sin que nadie
            // la vuelva a teclear.
            var cajeros = await GetCajerosAsync();
            if (cajeros.Count == 0) return null;

            var sobre = new CashierRosterEnvelope(
                pin,
                cajeros.Select(CashierRosterEnvelope.From).ToList());

            AppLogger.I("IAuthStore",
                $"Padron listo para replicar: {sobre.Cajeros.Count} cajeros " +
                $"({sobre.CajerosActivos} activos, " +
                $"{sobre.Cajeros.Count - sobre.CajerosActivos} de baja).");

            return sobre;
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

            // TODO O NADA. Ver [EscribirPadronAsync]: antes esto borraba y despues
            // insertaba en llamadas sueltas, asi que un fallo a la mitad dejaba la
            // caja con medio padron —o sin ninguno— mientras la pantalla decia que
            // habia quedado como estaba.
            await EscribirPadronAsync(conn, sobre.Cajeros, sobre.AdminPinHash);

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

    /// <summary>
    /// Reemplaza el padron entero —y opcionalmente el PIN— en UNA transaccion.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE TIENE QUE SER TODO O NADA
    /// ─────────────────────────────────────────────────────────────────────────────
    /// La version anterior hacia <c>DeleteAllAsync</c> y despues un
    /// <c>InsertOrReplaceAsync</c> por cajero, cada uno en su propia transaccion
    /// implicita. Entre el borrado y el ultimo insert hay una ventana real: en un
    /// POS la BD esta cifrada y compartida con la idempotencia, y cualquier cosa
    /// que interrumpa ahi —un bloqueo, un cierre de la app, una fila mala— deja la
    /// caja con PARTE del padron. O con ninguno.
    ///
    /// Y lo peor no era perderlo, era lo que se le decia al operador: el catch
    /// devolvia false y la pantalla mostraba "no se pudo actualizar el padron, la
    /// caja quedo como estaba". No habia quedado como estaba. Se habia quedado sin
    /// cajeros, y nadie iba a buscar ahi.
    ///
    /// Dentro de la transaccion se usa la conexion SINCRONA que expone sqlite-net:
    /// es la unica forma de que el borrado y los inserts compartan transaccion.
    /// </summary>
    private static async Task EscribirPadronAsync(
        SQLiteAsyncConnection conn,
        IReadOnlyList<ReplicatedCajero> cajeros,
        string? adminPinHash)
    {
        await conn.RunInTransactionAsync(tx =>
        {
            if (!string.IsNullOrWhiteSpace(adminPinHash))
            {
                tx.InsertOrReplace(new AuthSettingRow
                {
                    Key = AdminPinKey,
                    Value = adminPinHash
                });
            }

            // Completo, no mezclado. Ver la nota en [IAuthStore.ImportarPadronAsync].
            tx.DeleteAll<CajeroRow>();

            foreach (var c in cajeros)
            {
                tx.InsertOrReplace(new CajeroRow
                {
                    Id = c.Id,
                    Usuario = c.Usuario,
                    Nombre = c.Nombre,
                    ClaveHash = c.ClaveHash,
                    Activo = c.Activo,
                    CreadoEn = c.CreadoEn
                });
            }
        });

        // ─────────────────────────────────────────────────────────────────────
        // Y SE COMPRUEBA QUE QUEDO ESCRITO
        // ─────────────────────────────────────────────────────────────────────
        // La transaccion garantiza que no quede a medias, no que haya entrado lo
        // que se esperaba: dos cajeros con el mismo Id en el sobre se colapsan en
        // uno solo —InsertOrReplace— y la caja terminaria con menos gente de la
        // que el operador acaba de ver en el resumen, sin un solo error.
        //
        // Es una consulta de conteo sobre una tabla de decenas de filas. El costo
        // es irrelevante al lado de descubrirlo con un cajero que no puede entrar.
        var escritos = await conn.Table<CajeroRow>().CountAsync();
        if (escritos != cajeros.Count)
        {
            throw new InvalidOperationException(
                $"El padron quedo con {escritos} cajeros y el sobre traia {cajeros.Count}. " +
                "Probablemente hay Ids repetidos en el origen.");
        }
    }

    public async Task<bool> ActualizarCajerosAsync(CashierRosterEnvelope sobre, bool incluirPinAdmin)
    {
        ArgumentNullException.ThrowIfNull(sobre);

        try
        {
            var conn = await GetConnectionAsync();

            // Completo, igual que en la importacion: el padron de la otra caja es el
            // padron de la tienda. La diferencia con importar es lo que NO se toca
            // —el PIN de administrador solo si lo pidieron, porque en el camino
            // rutinario pisarlo no puede ser el default— y que esto tambien va en
            // una sola transaccion. Ver [EscribirPadronAsync].
            await EscribirPadronAsync(
                conn,
                sobre.Cajeros,
                incluirPinAdmin ? sobre.AdminPinHash : null);

            AppLogger.I("IAuthStore",
                $"Cajeros actualizados desde otra caja: {sobre.Cajeros.Count} escritos. " +
                $"PIN de administrador: {(incluirPinAdmin ? "tambien actualizado" : "sin tocar")}.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.E("IAuthStore", "No se pudo actualizar el padron de cajeros.", ex);
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
