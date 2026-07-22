using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Services.Platform;

public sealed class TransactionStateStore : ITransactionStateStore
{
    private readonly object _gate = new();
    private HioposTransaction? _transaction;
    private SaleDocument?      _document;
    private Client?            _client;
    private ActiveCredit?      _selectedCredit;
    private Credit?            _createdCredit;
    private Payment?           _lastPayment;

    public HioposTransaction? ActiveTransaction { get { lock (_gate) return _transaction; } }
    public SaleDocument?      ActiveDocument    { get { lock (_gate) return _document; } }
    public Client?            ValidatedClient   { get { lock (_gate) return _client; } }
    public ActiveCredit?      SelectedCredit    { get { lock (_gate) return _selectedCredit; } }
    public Credit?            CreatedCredit     { get { lock (_gate) return _createdCredit; } }
    public Payment?           LastPayment       { get { lock (_gate) return _lastPayment; } }
    public decimal            CreditValue       { get; set; }
    public int                Months            { get; set; }
    public string?            LastPaymentAttemptCreditId { get; set; }
    public DateTime?          LastPaymentAttemptAt      { get; set; }

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
            CreditValue     = 0m;
            Months          = 0;
            LastPaymentAttemptCreditId = null;
            LastPaymentAttemptAt       = null;
        }
    }
}
