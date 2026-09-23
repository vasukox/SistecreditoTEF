using SQLite;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// HU8-973: auditoría PERSISTENTE en SQLite cifrado (reemplaza la in-memory que
/// se perdía al cerrar la app — mala trazabilidad financiera).
///
/// Mantiene un espejo en memoria (para el getter síncrono [Entries] que usa el
/// demo) y persiste cada entrada en disco cifrado. Al arrancar recarga las
/// últimas [MaxInMemory] para continuidad. La persistencia es best-effort: si
/// falla, NO tumba la operación (la fuente de verdad es CREDINET + el broadcast).
/// </summary>
public sealed class SqliteAuditLog : IAuditLogCapture
{
    private const string DbName = "audit_sec.db3";
    private const int MaxInMemory = 200;

    private readonly object _gate = new();
    private readonly List<AuditEntry> _entries = new();
    private readonly Lazy<Task<SQLiteAsyncConnection>> _db;

    public SqliteAuditLog()
    {
        _db = new Lazy<Task<SQLiteAsyncConnection>>(() => SecureDb.OpenAsync<AuditRow>(DbName));
        _ = LoadRecentAsync();
    }

    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public void Append(string action, string comment, string? documentId)
    {
        var entry = new AuditEntry(DateTime.Now, action, comment, documentId);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxInMemory)
                _entries.RemoveAt(0);
        }
        _ = PersistAsync(entry);
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
        _ = ClearAsync();
    }

    private async Task PersistAsync(AuditEntry e)
    {
        try
        {
            var conn = await _db.Value;
            await conn.InsertAsync(new AuditRow
            {
                Timestamp = e.Timestamp,
                Action = e.Action,
                Comment = e.Comment,
                DocumentId = e.DocumentId ?? string.Empty
            });
        }
        catch (Exception ex)
        {
            AppLogger.W("SqliteAuditLog", $"No se pudo persistir auditoría: {ex.Message}");
        }
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var conn = await _db.Value;
            var rows = await conn.Table<AuditRow>()
                .OrderByDescending(r => r.Id)
                .Take(MaxInMemory)
                .ToListAsync();
            rows.Reverse();
            lock (_gate)
            {
                if (_entries.Count == 0)
                    _entries.AddRange(rows.Select(r => new AuditEntry(
                        r.Timestamp, r.Action, r.Comment,
                        string.IsNullOrEmpty(r.DocumentId) ? null : r.DocumentId)));
            }
        }
        catch (Exception ex)
        {
            AppLogger.W("SqliteAuditLog", $"No se pudo cargar auditoría: {ex.Message}");
        }
    }

    private async Task ClearAsync()
    {
        try
        {
            var conn = await _db.Value;
            await conn.DeleteAllAsync<AuditRow>();
        }
        catch (Exception ex)
        {
            AppLogger.W("SqliteAuditLog", $"No se pudo limpiar auditoría: {ex.Message}");
        }
    }

    /// <summary>
    /// QA M-13: retención de la traza de auditoría. Se conserva el histórico
    /// reciente (por defecto 180 días, ver [MainActivity]) y se descarta el resto.
    /// </summary>
    public async Task PurgeOlderThanAsync(TimeSpan retention)
    {
        try
        {
            var conn = await _db.Value;
            var cutoff = DateTime.Now - retention;
            // Un solo DELETE, no un SELECT + N DeleteAsync: la auditoría es la
            // tabla que más crece (una fila por paso de cada transacción), así que
            // era la que más filas materializaba y más idas y vueltas hacía sobre
            // la conexión compartida con la operación.
            var deleted = await conn.ExecuteAsync(
                "DELETE FROM audit_log WHERE Timestamp < ?", cutoff);

            if (deleted > 0)
            {
                AppLogger.I("SqliteAuditLog",
                    $"Purga de auditoría: {deleted} entradas con más de {retention.TotalDays:0} días.");
                lock (_gate) _entries.RemoveAll(e => e.Timestamp < cutoff);
            }
        }
        catch (Exception ex)
        {
            // Mantenimiento: nunca debe impedir operar.
            AppLogger.W("SqliteAuditLog", $"No se pudo purgar la auditoría: {ex.Message}");
        }
    }

    [Table("audit_log")]
    private sealed class AuditRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
    }
}
