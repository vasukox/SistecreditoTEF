namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Constantes de las acciones de auditoria para Sistecredito.
/// Doc §7.2. Centralizadas para evitar typos (DRY).
/// </summary>
public static class AuditActions
{
    public const string ValidateOk    = "SC_VALIDATE_OK";
    public const string ValidateFail  = "SC_VALIDATE_FAIL";
    public const string Simulate      = "SC_SIMULATE";
    public const string TokenRequest  = "SC_TOKEN_REQUEST";
    public const string CreditCreated = "SC_CREDIT_CREATED";
    public const string CreditFail    = "SC_CREDIT_FAIL";
    public const string Payment       = "SC_PAYMENT";
    public const string PaymentFail   = "SC_PAYMENT_FAIL";
}
