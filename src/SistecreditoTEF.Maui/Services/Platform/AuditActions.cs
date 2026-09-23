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

    /// <summary>
    /// Nota de credito (TransactionType=REFUND) rechazada: Credinet no tiene
    /// operacion de reverso. Se audita porque para la caja es una venta que NO se
    /// pudo devolver por este medio, y alguien va a preguntar por que.
    /// </summary>
    public const string RefundRejected = "SC_REFUND_REJECTED";

    /// <summary>
    /// El POS solto la linea de pago de una venta cobrada con Sistecredito.
    ///
    /// Se audita SIEMPRE y con referencia e importe porque es una SIMULACION: se le
    /// contesta al POS que el abono quedo pagado, pero Credinet no tiene reverso y el
    /// credito de esa venta SIGUE VIVO. Esta traza es el unico rastro para cuadrarlo
    /// contra Sistecredito despues.
    /// </summary>
    public const string PaymentLineReleased = "SC_PAYMENT_LINE_RELEASED";
}
