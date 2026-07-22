using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Contrato que la capa de dominio consume (DIP).
/// 1:1 con CredinetRepository.kt de Kotlin.
///
/// Equivalente a la interfaz Java: la UI depende de esta interfaz,
/// no de la implementacion. Esto permite inyectar fakes en tests.
/// </summary>
public interface ICredinetRepository
{
    Task<ApiResult<Client>> GetCreditLimitClientAsync(
        string typeDocument, string idDocument);

    Task<ApiResult<CreditDetails>> GetCreditDetailsAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument);

    Task<ApiResult<CreditToken>> SolicitarClaveDinamicaAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument, int? destination = null);

    Task<ApiResult<Credit>> CrearCreditoAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument,
        string token, string source = "2", int authMethod = 1,
        string? invoice = null, string? seller = null, string? products = null);

    Task<ApiResult<List<ActiveCredit>>> GetActiveCreditsAsync(
        string typeDocument, string idDocument);

    Task<ApiResult<Payment>> PagarCreditoAsync(
        string creditId, double totalValuePaid, string userName);

    Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(
        double creditValue);
}
