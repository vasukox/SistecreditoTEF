using SQLite;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion SQLite de [IIdempotencyStore].
///
/// Tabla unica [cached_transactions] con PK = SaleId.
/// BD local en el app data dir del Android (sandbox).
///
/// DRY: usa SQLite-net-pcl (estandar de facto en MAUI), singleton.
/// </summary>
public class SqliteIdempotencyStore : IIdempotencyStore
{
    // HU8-973: BD cifrada (SQLCipher). Nombre nuevo + se elimina la BD en claro
    // de versiones anteriores para no chocar con el formato cifrado.
    private const string DbName = "idempotency_sec.db3";
    private const string LegacyPlaintextDb = "idempotency.db3";
    private readonly Lazy<Task<SQLiteAsyncConnection>> _db;

    public SqliteIdempotencyStore()
    {
        _db = new Lazy<Task<SQLiteAsyncConnection>>(async () =>
        {
            SecureDb.DeleteLegacyPlaintext(LegacyPlaintextDb);
            var conn = await SecureDb.OpenAsync<CachedTransactionRow>(DbName);
            AppLogger.I("IIdempotencyStore", "BD local cifrada inicializada.");
            return conn;
        });
    }

    public async Task<CachedTransaction?> FindBySaleIdAsync(string saleId)
    {
        var conn = await _db.Value;
        var row = await conn.Table<CachedTransactionRow>()
            .Where(r => r.SaleId == saleId)
            .FirstOrDefaultAsync();
        return row?.ToDomain();
    }

    public async Task SaveAsync(CachedTransaction tx)
    {
        var conn = await _db.Value;
        var row = CachedTransactionRow.From(tx);
        await conn.InsertOrReplaceAsync(row);
        AppLogger.I("IIdempotencyStore", $"Cacheado SaleId={tx.SaleId} CreditId={tx.CreditId}.");
    }

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

        public CachedTransaction ToDomain() => new(
            SaleId, CreditId, CreditNumber, TransactionData,
            AuthorizationId, CardHolder, CardNum,
            MerchantReceiptXml, CustomerReceiptXml, CreatedAt);

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
            CreatedAt = t.CreatedAt
        };
    }
}
