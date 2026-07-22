using System.Collections.Concurrent;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion in-memory de [IAuditLogCapture]. Thread-safe via
/// ConcurrentQueue + lock para snapshot. Max 200 entradas FIFO.
/// </summary>
public sealed class InMemoryAuditLog : IAuditLogCapture
{
    private const int MaxEntries = 200;
    private readonly object _gate = new();
    private readonly List<AuditEntry> _entries = new();

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
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
