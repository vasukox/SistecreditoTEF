namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Captura en memoria de las acciones de auditoria. Permite mostrarlas
/// en el demo mode sin tener un POS escuchando los broadcasts.
///
/// Se registra como singleton y [BroadcastAuditLogger] escribe aqui
/// cada vez que emite un audit (doc §7.1), antes/despues del broadcast.
///
/// DRY: max 200 entradas (FIFO). Suficiente para una sesion de demo.
/// </summary>
public interface IAuditLogCapture
{
    IReadOnlyList<AuditEntry> Entries { get; }
    void Append(string action, string comment, string? documentId);
    void Clear();
}

public sealed record AuditEntry(
    DateTime Timestamp,
    string Action,
    string Comment,
    string? DocumentId);
