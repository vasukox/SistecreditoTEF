using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Estado del flujo en curso, compartido entre pantallas. Singleton en DI.
///
/// QA M-10: antes solo los campos de referencia estaban protegidos por el lock.
/// <c>CreditValue</c>, <c>Months</c>, <c>LastPaymentAttemptCreditId</c> y
/// <c>LastPaymentAttemptAt</c> eran auto-propiedades sin sincronizar, pese a que
/// <c>Clear()</c> sí las escribía dentro del lock. <c>decimal</c> y
/// <c>DateTime?</c> no son de escritura atómica, así que había riesgo real de
/// lectura desgarrada — y <c>LastPaymentAttemptAt</c> es justamente la barrera
/// anti-doble-cobro. Ahora TODO el estado pasa por el mismo lock.
/// </summary>
public sealed class TransactionStateStore : ITransactionStateStore
{
    private readonly object _gate = new();

    private HioposTransaction? _transaction;
    private SaleDocument?      _document;
    private Client?            _client;
    private ActiveCredit?      _selectedCredit;
    private Credit?            _createdCredit;
    private Payment?           _lastPayment;
    private decimal            _creditValue;
    private int                _months;
    private string?            _lastPaymentAttemptCreditId;
    private DateTime?          _lastPaymentAttemptAt;
    private bool               _hioposTransactionActive;

    public HioposTransaction? ActiveTransaction { get { lock (_gate) return _transaction; } }
    public SaleDocument?      ActiveDocument    { get { lock (_gate) return _document; } }
    public Client?            ValidatedClient   { get { lock (_gate) return _client; } }
    public ActiveCredit?      SelectedCredit    { get { lock (_gate) return _selectedCredit; } }
    public Credit?            CreatedCredit     { get { lock (_gate) return _createdCredit; } }
    public Payment?           LastPayment       { get { lock (_gate) return _lastPayment; } }

    public decimal CreditValue
    {
        get { lock (_gate) return _creditValue; }
        set { lock (_gate) _creditValue = value; }
    }

    public int Months
    {
        get { lock (_gate) return _months; }
        set { lock (_gate) _months = value; }
    }

    public string? LastPaymentAttemptCreditId
    {
        get { lock (_gate) return _lastPaymentAttemptCreditId; }
        set { lock (_gate) _lastPaymentAttemptCreditId = value; }
    }

    public DateTime? LastPaymentAttemptAt
    {
        get { lock (_gate) return _lastPaymentAttemptAt; }
        set { lock (_gate) _lastPaymentAttemptAt = value; }
    }

    public bool HioposTransactionActive
    {
        get { lock (_gate) return _hioposTransactionActive; }
        set { lock (_gate) _hioposTransactionActive = value; }
    }

    public void SetActiveTransaction(HioposTransaction tx) { lock (_gate) _transaction = tx; }
    public void SetActiveDocument(SaleDocument doc)        { lock (_gate) _document = doc; }
    public void SetValidatedClient(Client client)          { lock (_gate) _client = client; }
    public void SetSelectedCredit(ActiveCredit credit)     { lock (_gate) _selectedCredit = credit; }
    public void SetCreatedCredit(Credit credit)            { lock (_gate) _createdCredit = credit; }
    public void SetLastPayment(Payment payment)            { lock (_gate) _lastPayment = payment; }

    public void Clear()
    {
        lock (_gate)
        {
            _transaction    = null;
            _document       = null;
            _client         = null;
            _selectedCredit = null;
            _createdCredit  = null;
            _lastPayment    = null;
            _creditValue    = 0m;
            _months         = 0;
            _lastPaymentAttemptCreditId = null;
            _lastPaymentAttemptAt       = null;
            _hioposTransactionActive    = false;
        }
    }
}
