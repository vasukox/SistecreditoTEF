using Android.Content;
using Microsoft.Maui.ApplicationModel;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// Implementacion Android de [IAuditLogger] que envia un Broadcast
/// al HioPosCloud.
///
/// Doc §7.1: usa Context.SendBroadcast con el action "icg.actions.externalApi.AUDIT".
/// Doc §7.2: limite 50 chars para Action, 1050 para Comment.
/// </summary>
public class BroadcastAuditLogger : IAuditLogger
{
    private const int MaxActionLength  = 50;
    private const int MaxCommentLength = 1050;

    private readonly ITokenStore _tokenStore;
    private readonly IAuditLogCapture _capture;

    public BroadcastAuditLogger(ITokenStore tokenStore, IAuditLogCapture capture)
    {
        _tokenStore = tokenStore;
        _capture = capture;
    }

    public void Log(string action, string comment, string? documentId = null)
    {
        if (action.Length > MaxActionLength)
            action = action[..MaxActionLength];
        if (comment.Length > MaxCommentLength)
            comment = comment[..MaxCommentLength];

        // Siempre capturamos en memoria, asi el demo mode puede mostrarlas.
        _capture.Append(action, comment, documentId);

        var token = _tokenStore.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            // Sin token (INITIALIZE no llego todavia) - solo logueamos.
            AppLogger.W("IAuditLogger", $"AUDIT omitido (sin token): {action} | {comment}");
            return;
        }

        var ctx = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (ctx is null)
        {
            AppLogger.W("IAuditLogger", "Context null; AUDIT no enviado.");
            return;
        }

        var intent = new Intent(HioposActions.ExternalAudit)
            .PutExtra(HioposExtras.AuditAction,  action)
            .PutExtra(HioposExtras.AuditComment, comment)
            .PutExtra(HioposExtras.AuditToken,   token);

        if (!string.IsNullOrEmpty(documentId))
            intent.PutExtra(HioposExtras.AuditDocumentId, documentId);

        ctx.SendBroadcast(intent);
        AppLogger.I("IAuditLogger", $"AUDIT {action}: {comment}");
    }
}
