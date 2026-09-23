using SQLite;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion SQLite (cifrada con SQLCipher) de [IIdempotencyStore].
///
/// Dos tablas:
///   - <c>cached_transactions</c> (PK = SaleId): creditos creados.
///   - <c>cached_payments</c> (PK = PaymentKey): intentos de abono (QA C-5).
///
/// QA M-11: la inicializacion ya NO usa un <c>Lazy&lt;Task&gt;</c> pelado. Un
/// <c>Lazy</c> cachea la task FALLIDA, asi que un bloqueo momentaneo de la BD al
/// arrancar dejaba al POS operando el dia entero sin barrera de idempotencia
/// local, con todas las llamadas siguientes fallando. Ahora se reintenta la
/// apertura en la proxima operacion.
/// </summary>
public class SqliteIdempotencyStore : IIdempotencyStore
{
    private const string DbName = "idempotency_sec.db3";
    private const string LegacyPlaintextDb = "idempotency.db3";

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    /// <summary>
    /// Conexion abierta, reintentando si un intento previo fallo (QA M-11).
    /// </summary>
    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        var existing = _connection;
        if (existing is not null) return existing;

        await _initGate.WaitAsync();
        try
        {
            if (_connection is not null) return _connection;

            SecureDb.DeleteLegacyPlaintext(LegacyPlaintextDb);
            var conn = await SecureDb.OpenAsync<CachedTransactionRow>(DbName);
            await conn.CreateTableAsync<CachedPaymentRow>();

            _connection = conn;
            AppLogger.I("IIdempotencyStore", "BD local cifrada inicializada (creditos + abonos).");
            return conn;
        }
        finally
        {
            _initGate.Release();
        }
    }

    // ------------------------------------------------------------------
    // Creditos
    // ------------------------------------------------------------------

    public async Task<CachedTransaction?> FindBySaleIdAsync(string saleId)
    {
        if (string.IsNullOrEmpty(saleId)) return null;

        var conn = await GetConnectionAsync();
        var row = await conn.Table<CachedTransactionRow>()
            .Where(r => r.SaleId == saleId)
            .FirstOrDefaultAsync();
        return row?.ToDomain();
    }

    public async Task SaveAsync(CachedTransaction transaction)
    {
        var conn = await GetConnectionAsync();
        await conn.InsertOrReplaceAsync(CachedTransactionRow.From(transaction));
        AppLogger.I("IIdempotencyStore",
            $"Credito cacheado: SaleId={transaction.SaleId} CreditId={transaction.CreditId} " +
            $"(voucher {(string.IsNullOrEmpty(transaction.CreditJson) ? "SIN" : "con")} datos financieros).");
    }

    // ------------------------------------------------------------------
    // Abonos (QA C-5)
    // ------------------------------------------------------------------

    public async Task<CachedPayment?> FindRecentPaymentAsync(
        string creditId, long amountCents, TimeSpan window)
    {
        if (string.IsNullOrEmpty(creditId)) return null;

        var conn = await GetConnectionAsync();
        var cutoff = DateTime.UtcNow - window;

        // Se compara credito + monto: un abono legitimo del mismo monto dias
        // despues queda fuera de la ventana y no se bloquea.
        var row = await conn.Table<CachedPaymentRow>()
            .Where(r => r.CreditId == creditId
                        && r.AmountCents == amountCents
                        && r.CreatedAt > cutoff)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        return row?.ToDomain();
    }

    public async Task SavePaymentAsync(CachedPayment payment)
    {
        var conn = await GetConnectionAsync();
        await conn.InsertOrReplaceAsync(CachedPaymentRow.From(payment));
        AppLogger.I("IIdempotencyStore",
            $"Intento de abono {payment.Status}: key={payment.PaymentKey} " +
            $"creditId={payment.CreditId} centavos={payment.AmountCents}");
    }

    // ------------------------------------------------------------------
    // Retencion (QA M-13)
    // ------------------------------------------------------------------

    public async Task PurgeOlderThanAsync(TimeSpan retention)
    {
        try
        {
            var conn = await GetConnectionAsync();
            var cutoff = DateTime.UtcNow - retention;

            // Un solo DELETE por tabla, no un SELECT + N DeleteAsync.
            //
            // Antes se cargaban en memoria TODAS las filas vencidas y se borraban
            // de a una. A 180 dias de retencion en una caja con movimiento eso son
            // decenas de miles de filas materializadas y otras tantas idas y
            // vueltas a la BD, sobre la MISMA conexion que usan los cobros. La
            // purga corre en segundo plano al arrancar, asi que no congelaba la
            // pantalla, pero si competia con la venta en curso justo cuando la
            // terminal recien abre. Con un DELETE es una sentencia y nada en
            // memoria.
            var credits = await conn.ExecuteAsync(
                "DELETE FROM cached_transactions WHERE CreatedAt < ?", cutoff);
            var payments = await conn.ExecuteAsync(
                "DELETE FROM cached_payments WHERE CreatedAt < ?", cutoff);

            if (credits + payments > 0)
                AppLogger.I("IIdempotencyStore",
                    $"Purga de idempotencia: {credits} creditos y {payments} abonos " +
                    $"con mas de {retention.TotalDays:0} dias.");
        }
        catch (Exception ex)
        {
            // La purga es mantenimiento: nunca debe impedir facturar.
            AppLogger.W("IIdempotencyStore", $"No se pudo purgar la BD local: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Filas
    // ------------------------------------------------------------------

    [Table("cached_transactions")]
    private sealed class CachedTransactionRow
    {
        [PrimaryKey]
        public string SaleId { get; set; } = string.Empty;
        public string CreditId { get; set; } = string.Empty;
        public int    CreditNumber { get; set; }
        public string TransactionData { get; set; } = string.Empty;
        public string AuthorizationId { get; set; } = string.Empty;
        public string CardHolder { get; set; } = string.Empty;
        public string CardNum { get; set; } = string.Empty;
        public string MerchantReceiptXml { get; set; } = string.Empty;
        public string CustomerReceiptXml { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        /// <summary>QA C-4: [Credit] serializado, para reimprimir el voucher real.</summary>
        public string CreditJson { get; set; } = string.Empty;

        public CachedTransaction ToDomain() => new(
            SaleId, CreditId, CreditNumber, TransactionData,
            AuthorizationId, CardHolder, CardNum,
            MerchantReceiptXml, CustomerReceiptXml, CreatedAt, CreditJson);

        public static CachedTransactionRow From(CachedTransaction t) => new()
        {
            SaleId = t.SaleId,
            CreditId = t.CreditId,
            CreditNumber = t.CreditNumber,
            TransactionData = t.TransactionData,
            AuthorizationId = t.AuthorizationId,
            CardHolder = t.CardHolder,
            CardNum = t.CardNum,
            MerchantReceiptXml = t.MerchantReceiptXml,
            CustomerReceiptXml = t.CustomerReceiptXml,
            CreatedAt = t.CreatedAt,
            CreditJson = t.CreditJson
        };
    }

    [Table("cached_payments")]
    private sealed class CachedPaymentRow
    {
        [PrimaryKey]
        public string PaymentKey { get; set; } = string.Empty;

        [Indexed]
        public string CreditId { get; set; } = string.Empty;
        public long   AmountCents { get; set; }
        public int    Status { get; set; }
        public string PaymentId { get; set; } = string.Empty;
        public int    PaymentNumber { get; set; }
        public string PaymentJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public CachedPayment ToDomain() => new(
            PaymentKey, CreditId, AmountCents, (PaymentAttemptStatus)Status,
            PaymentId, PaymentNumber, PaymentJson, CreatedAt, CompletedAt);

        public static CachedPaymentRow From(CachedPayment p) => new()
        {
            PaymentKey = p.PaymentKey,
            CreditId = p.CreditId,
            AmountCents = p.AmountCents,
            Status = (int)p.Status,
            PaymentId = p.PaymentId,
            PaymentNumber = p.PaymentNumber,
            PaymentJson = p.PaymentJson,
            CreatedAt = p.CreatedAt,
            CompletedAt = p.CompletedAt
        };
    }
}
