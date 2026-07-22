using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Dtos;
using SistecreditoTEF.Maui.Mappers;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Implementacion de ICredinetRepository.
/// 1:1 con CredinetRepositoryImpl.kt de Kotlin.
///
/// DRY:
///   - ProcessResponse: centraliza la decision Success vs BusinessError
///     y captura excepciones de red/HTTP del ApiClient.
///   - La conversion DTO -> Dominio se hace via mappers (CredinetMapper).
/// </summary>
public class CredinetRepository : ICredinetRepository
{
    private readonly ICredinetApi _api;
    private readonly ApiConfig _config;

    public CredinetRepository(ICredinetApi api, ApiConfig config)
    {
        _api = api;
        _config = config;
    }

    public async Task<ApiResult<Client>> GetCreditLimitClientAsync(
        string typeDocument, string idDocument)
    {
        try
        {
            var body = await _api.GetCreditLimitClientAsync(
                typeDocument, idDocument, _config.StoreId);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<Client>(ex);
        }
    }

    public async Task<ApiResult<CreditDetails>> GetCreditDetailsAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument)
    {
        try
        {
            var body = await _api.GetCreditDetailsAsync(
                creditValue, frequency, months,
                typeDocument, idDocument, _config.StoreId);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<CreditDetails>(ex);
        }
    }

    public async Task<ApiResult<CreditToken>> SolicitarClaveDinamicaAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument, int? destination = null)
    {
        try
        {
            var body = await _api.GetCreditTokenAsync(
                creditValue, months, frequency,
                typeDocument, idDocument, destination, _config.StoreId);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<CreditToken>(ex);
        }
    }

    public async Task<ApiResult<Credit>> CrearCreditoAsync(
        double creditValue, int frequency, int months,
        string typeDocument, string idDocument,
        string token, string source = "2", int authMethod = 1,
        string? invoice = null, string? seller = null, string? products = null)
    {
        try
        {
            var request = new CreateCreditRequest(
                TypeDocument: typeDocument,
                IdDocument: idDocument,
                CreditValue: creditValue,
                Frequency: frequency,
                Fees: months,
                Token: token,
                Source: source,
                AuthMethod: authMethod,
                Seller: seller,
                Products: products,
                Invoice: invoice,
                StoreId: _config.StoreId);
            var body = await _api.CreateAsync(request);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<Credit>(ex);
        }
    }

    public async Task<ApiResult<List<ActiveCredit>>> GetActiveCreditsAsync(
        string typeDocument, string idDocument)
    {
        try
        {
            var body = await _api.GetActiveCreditsAsync(
                typeDocument, idDocument, _config.StoreId);
            return ProcessResponse(body).Map(dtoList => dtoList.Select(d => d.ToDomain()).ToList());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<List<ActiveCredit>>(ex);
        }
    }

    public async Task<ApiResult<Payment>> PagarCreditoAsync(
        string creditId, double totalValuePaid, string userName)
    {
        try
        {
            var request = new PayCreditRequest(
                CreditId: creditId,
                TotalValuePaid: totalValuePaid,
                UserName: userName);
            var body = await _api.PayCreditAsync(request);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<Payment>(ex);
        }
    }

    public async Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(
        double creditValue)
    {
        try
        {
            var body = await _api.GetSimulatedMonthLimitAsync(creditValue, _config.StoreId);
            return ProcessResponse(body).Map(dto => dto.ToDomain());
        }
        catch (CredinetApiClient.ApiException ex)
        {
            return HandleApiException<SimulatedMonthLimit>(ex);
        }
    }

    /// <summary>
    /// Convierte ApiResponse de CREDINET a ApiResult del dominio.
    /// Reglas: errorCode != 0 o data == null => ApiError.Business; si no, Ok.
    /// </summary>
    private static ApiResult<T> ProcessResponse<T>(ApiResponse<T> body)
    {
        if (body.ErrorCode != 0 || body.Data is null)
        {
            return new ApiResult<T>.Failure<T>(
                new ApiError.Business(
                    Code: body.ErrorCode,
                    Message: body.Message ?? "Sin mensaje",
                    Function: body.Function));
        }
        return new ApiResult<T>.Ok<T>(body.Data);
    }

    /// <summary>
    /// Traduce una excepcion HTTP/red lanzada por el ApiClient al tipo
    /// Failure&lt;T&gt; correspondiente. DRY: usado por los 7 endpoints.
    /// </summary>
    private static ApiResult<T> HandleApiException<T>(CredinetApiClient.ApiException ex) => ex switch
    {
        CredinetApiClient.ApiException.NetworkException n
            => new ApiResult<T>.Failure<T>(new ApiError.Network(n.Cause)),
        CredinetApiClient.ApiException.HttpException h
            => new ApiResult<T>.Failure<T>(new ApiError.Http(h.Code, h.Message)),
        _ => new ApiResult<T>.Failure<T>(
            new ApiError.Http(500, ex.Message))
    };
}
