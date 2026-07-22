namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion in-memory de [IRequestLogCapture]. Max 50 entradas
/// (FIFO) para no saturar la UI del demo.
/// </summary>
public sealed class InMemoryRequestLogCapture : IRequestLogCapture
{
    private const int MaxEntries = 50;
    private readonly object _gate = new();
    private readonly List<HttpLogEntry> _entries = new();

    public IReadOnlyList<HttpLogEntry> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public void Append(HttpLogEntry entry)
    {
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