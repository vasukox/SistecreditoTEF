namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Auditoria via Broadcast hacia HioPosCloud (doc §7).
///
/// POR QUE EXISTE (B9): sin esto, Permoda pierde trazabilidad de los
/// cobros Sistecredito que pasan por el POS. Tambien sirve como log
/// de debugging en campo.
///
/// Contrato del broadcast:
///   Action  = icg.actions.externalApi.AUDIT
///   Extras  = Action (50 chars), Comment (1050 chars), Token, DocumentId
/// </summary>
public interface IAuditLogger
{
    void Log(string action, string comment, string? documentId = null);
}
