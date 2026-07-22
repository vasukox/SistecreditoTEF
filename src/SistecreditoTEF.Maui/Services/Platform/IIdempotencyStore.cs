using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Cache local de transacciones ya creadas en Sistecredito, indexadas
/// por [SaleId] del documento de venta.
///
/// Doc §12 gotcha #7: "el mismo SaleId puede llegar dos veces: si HioPos
/// recibio UNKNOWN_RESULT, reintenta. Tu modulo debe detectar el duplicado
/// y responder ACCEPTED si ya creaste el credito en Sistecredito. Esto
/// evita el error 252 (DuplicatedCredit) en Credinet."
///
/// POR QUE EXISTE (B8): sin esto, ventas con timeout generan creditos
/// duplicados.
///
/// Interfaz async porque la implementacion SQLite requiere I/O async;
/// no bloquear la UI.
/// </summary>
public interface IIdempotencyStore
{
    Task<CachedTransaction?> FindBySaleIdAsync(string saleId);
    Task SaveAsync(CachedTransaction transaction);
}

/// <summary>
/// Snapshot minimo para responder ACCEPTED en reintentos sin volver
/// a pegarle a CREDINET.
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
    DateTime CreatedAt);
