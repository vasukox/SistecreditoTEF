using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Cache local de operaciones ya ejecutadas en Sistecredito, para no repetirlas
/// si HioPos reintenta o si el proceso muere en el momento equivocado.
///
/// Doc §12 gotcha #7: "el mismo SaleId puede llegar dos veces: si HioPos
/// recibio UNKNOWN_RESULT, reintenta. Tu modulo debe detectar el duplicado
/// y responder ACCEPTED si ya creaste el credito en Sistecredito. Esto
/// evita el error 252 (DuplicatedCredit) en Credinet."
///
/// QA C-5: ahora cubre TAMBIEN los abonos. El flujo de credito tenia doble
/// barrera (local por SaleId + <c>invoice</c> remoto) pero el de pago solo tenia
/// un guard EN MEMORIA de 60 segundos, y <c>payCredit</c> viaja sin ningun campo
/// de idempotencia, asi que Credinet no puede deduplicar del lado servidor. Si
/// el POS se reiniciaba entre el cobro exitoso y el recibo, el guard desaparecia
/// y el cajero podia cobrarle dos veces al cliente.
/// </summary>
public interface IIdempotencyStore
{
    // ---- Creditos (facturacion) ----
    Task<CachedTransaction?> FindBySaleIdAsync(string saleId);
    Task SaveAsync(CachedTransaction transaction);

    // ---- Abonos (recaudo) ----

    /// <summary>
    /// Busca un intento de pago para el mismo crédito y monto dentro de la
    /// ventana indicada. Sobrevive al reinicio del proceso.
    /// </summary>
    Task<CachedPayment?> FindRecentPaymentAsync(string creditId, long amountCents, TimeSpan window);

    /// <summary>Registra o actualiza un intento de pago (por <c>PaymentKey</c>).</summary>
    Task SavePaymentAsync(CachedPayment payment);

    /// <summary>
    /// QA M-13: purga registros más viejos que la retención configurada. Sin
    /// esto las tablas crecen indefinidamente en un POS que opera años.
    /// </summary>
    Task PurgeOlderThanAsync(TimeSpan retention);
}

/// <summary>
/// Snapshot de un crédito ya creado, suficiente para responder ACCEPTED y
/// **reimprimir el voucher correcto** en un reintento.
///
/// QA C-4: antes este record no guardaba los datos financieros, y el camino de
/// replay reconstruía el <c>Credit</c> con TODOS los importes en cero. El
/// cliente recibía un comprobante que decía cuota mensual $0, cuota inicial $0 y
/// Tasa E.A. 0,00% para un crédito real y vigente. Los campos
/// <c>MerchantReceiptXml</c>/<c>CustomerReceiptXml</c> existían justamente para
/// evitarlo… y se guardaban vacíos.
///
/// Ahora se persiste el <see cref="Credit"/> completo serializado
/// (<see cref="CreditJson"/>), que es lo que alimenta el voucher.
/// </summary>
public sealed record CachedTransaction(
    string SaleId,
    string CreditId,
    int CreditNumber,
    string TransactionData,
    string AuthorizationId,
    string CardHolder,
    string CardNum,
    string MerchantReceiptXml,
    string CustomerReceiptXml,
    DateTime CreatedAt,
    string CreditJson = "");

/// <summary>Estado de un intento de abono.</summary>
public enum PaymentAttemptStatus
{
    /// <summary>
    /// Se envió a Credinet y no se conoce el resultado (timeout / proceso muerto).
    /// Es el estado "en duda": NO se debe reintentar a ciegas.
    /// </summary>
    Pending = 0,

    /// <summary>Credinet confirmó el pago.</summary>
    Completed = 1,

    /// <summary>Credinet rechazó el pago por regla de negocio: se puede reintentar.</summary>
    Failed = 2
}

/// <summary>
/// Intento de abono persistido. La clave <c>PaymentKey</c> la genera el cliente
/// antes de llamar a Credinet, de modo que un reintento la reutilice.
/// </summary>
public sealed record CachedPayment(
    string PaymentKey,
    string CreditId,
    long AmountCents,
    PaymentAttemptStatus Status,
    string PaymentId,
    int PaymentNumber,
    string PaymentJson,
    DateTime CreatedAt,
    DateTime? CompletedAt);
