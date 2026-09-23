using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Almacena el estado del flujo en curso.
/// Singleton en DI.
/// </summary>
public interface ITransactionStateStore
{
    HioposTransaction? ActiveTransaction { get; }
    SaleDocument?      ActiveDocument    { get; }
    Client?            ValidatedClient   { get; }
    ActiveCredit?      SelectedCredit    { get; }
    Credit?            CreatedCredit     { get; }
    Payment?           LastPayment       { get; }
    decimal            CreditValue       { get; set; }
    int                Months            { get; set; }

    /// <summary>
    /// HU8-973: liveness de una factura de HI-POS. Es true SOLO entre el
    /// arranque de una TRANSACTION y la devolución del resultado a HioPos
    /// (FinishWithResult). El guard de MainActivity lo usa para saber si hay
    /// una venta genuinamente viva esperando resultado, en vez de inferirlo de
    /// datos residuales (ActiveTransaction/CreatedCredit/LastPayment) que
    /// podían quedar colgados si un flujo se abandonaba y bloquear el ícono de
    /// Abonos. En modo standalone (recaudo por launcher) permanece en false.
    /// </summary>
    bool               HioposTransactionActive { get; set; }

    /// <summary>
    /// HU8-973 BugFix: lock anti-doble-cobro en PagoPage.
    /// Guardamos el creditId y timestamp del ultimo intento de pago.
    /// Si el cajero vuelve a entrar a PagoPage antes de 60s con el mismo
    /// creditId, bloqueamos el reintento (porque podria haber sido ya
    /// procesado por Credinet pero la respuesta no llego). Sobrevive al
    /// back navigation porque vive en el Singleton store.
    /// </summary>
    string?  LastPaymentAttemptCreditId { get; set; }
    DateTime? LastPaymentAttemptAt      { get; set; }

    void SetActiveTransaction(HioposTransaction tx);
    void SetActiveDocument(SaleDocument doc);
    void SetValidatedClient(Client client);
    void SetSelectedCredit(ActiveCredit credit);
    void SetCreatedCredit(Credit credit);
    void SetLastPayment(Payment payment);
    void Clear();
}
