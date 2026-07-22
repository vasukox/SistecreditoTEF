using SistecreditoTEF.Maui.Dtos;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Contrato del cliente HTTP de CREDINET.
///
/// Equivalente 1:1 a la interfaz CredinetApi.kt de Kotlin.
///
/// NOTA: aqui NO usamos Refit ni Decorators. La implementacion
/// (CredinetApiClient) hace los HttpRequestMessage manualmente para
/// tener control total sobre timeouts y headers (DRY).
/// </summary>
public interface ICredinetApi
{
    Task<ApiResponse<ClientDto>> GetCreditLimitClientAsync(
        string typeDocument, string idDocument, string? storeId);

    Task<ApiResponse<CreditDetailsDto>> GetCreditDetailsAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument, string? storeId);

    Task<ApiResponse<CreditTokenDto>> GetCreditTokenAsync(
        double creditValue, int months, int frequency,
        string typeDocument, string idDocument, int? destination,
        string? storeId);

    Task<ApiResponse<CreditDto>> CreateAsync(CreateCreditRequest request);

    Task<ApiResponse<List<ActiveCreditDto>>> GetActiveCreditsAsync(
        string typeDocument, string idDocument, string? storeId);

    Task<ApiResponse<PaymentDto>> PayCreditAsync(PayCreditRequest request);

    Task<ApiResponse<SimulatedMonthLimitDto>> GetSimulatedMonthLimitAsync(
        double creditValue, string? storeId);
}
