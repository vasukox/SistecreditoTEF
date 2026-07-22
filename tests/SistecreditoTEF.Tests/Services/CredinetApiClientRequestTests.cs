using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Tests de la CAPA HTTP de CREDINET.
///
/// Objetivo: descartar problemas en como CredinetApiClient genera el
/// request (URL, metodo, headers). Si el server responde 500 a pesar
/// de que estos tests pasan, el problema NO esta en la app (es 5xx
/// real del server, whitelist de IP, geo, etc.).
///
/// Capturamos el HttpRequestMessage real antes de que salga por la red
/// usando [CapturingHandler] (un HttpMessageHandler mock).
/// </summary>
public class CredinetApiClientRequestTests
{
    private static SistecreditoTEF.Maui.Services.Credinet.ApiConfig Cfg() =>
        new()
        {
            SubscriptionKey = "test-key-xyz",
            StoreId = null,
            BaseUrl  = "https://api.credinet.co/pos/"
        };

    private static (SistecreditoTEF.Maui.Services.Credinet.CredinetApiClient client,
                    CapturingHandler capture)
        Build()
    {
        var capture = new CapturingHandler();
        var http = new HttpClient(capture)
        {
            BaseAddress = new System.Uri("https://api.credinet.co/pos/")
        };
        var api = new SistecreditoTEF.Maui.Services.Credinet.CredinetApiClient(http, Cfg());
        return (api, capture);
    }

    // --- 1. getCreditLimitClient ---

    [Fact]
    public async Task GetCreditLimitClient_builds_GET_with_correct_query_string()
    {
        var (api, cap) = Build();

        await api.GetCreditLimitClientAsync("CC", "1026260942", storeId: null);

        Assert.NotNull(cap.LastRequest);
        var r = cap.LastRequest!;
        Assert.Equal(HttpMethod.Get, r.Method);
        Assert.Equal("/pos/getCreditLimitClient?typeDocument=CC&idDocument=1026260942",
                     r.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetCreditLimitClient_omits_storeId_when_null()
    {
        var (api, cap) = Build();

        await api.GetCreditLimitClientAsync("CC", "1026260942", storeId: null);

        Assert.NotNull(cap.LastRequest);
        Assert.DoesNotContain("storeId", cap.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetCreditLimitClient_includes_storeId_when_provided()
    {
        var (api, cap) = Build();

        await api.GetCreditLimitClientAsync("CC", "1026260942", storeId: "PERMODA-1");

        Assert.NotNull(cap.LastRequest);
        Assert.Contains("storeId=PERMODA-1", cap.LastRequest!.RequestUri!.Query);
    }

    // --- 2. getCreditDetails ---

    [Fact]
    public async Task GetCreditDetails_uses_months_lowercase_not_Months()
    {
        var (api, cap) = Build();

        await api.GetCreditDetailsAsync(
            creditValue: 500_000.0,
            frequency: 30,
            months: 3,
            typeDocument: "CC",
            idDocument: "1026260942",
            storeId: null);

        Assert.NotNull(cap.LastRequest);
        var qs = cap.LastRequest!.RequestUri!.Query;
        Assert.Contains("months=3", qs);
        Assert.DoesNotContain("Months=3", qs);
    }

    [Fact]
    public async Task GetCreditDetails_builds_GET_with_all_params()
    {
        var (api, cap) = Build();

        await api.GetCreditDetailsAsync(
            creditValue: 500_000.0,
            frequency: 30,
            months: 3,
            typeDocument: "CC",
            idDocument: "1026260942",
            storeId: null);

        var qs = cap.LastRequest!.RequestUri!.Query;
        Assert.Contains("creditValue=500000", qs);
        Assert.Contains("frequency=30", qs);
        Assert.Contains("months=3", qs);
        Assert.Contains("typeDocument=CC", qs);
        Assert.Contains("idDocument=1026260942", qs);
    }

    // --- 3. getCreditToken ---

    [Fact]
    public async Task GetCreditToken_builds_GET_with_destination_when_provided()
    {
        var (api, cap) = Build();

        await api.GetCreditTokenAsync(
            creditValue: 50_000.0,
            months: 1,
            frequency: 30,
            typeDocument: "CC",
            idDocument: "43057659",
            destination: 1,
            storeId: null);

        var qs = cap.LastRequest!.RequestUri!.Query;
        Assert.Contains("destination=1", qs);
        Assert.Contains("months=1", qs);
        Assert.Contains("creditValue=50000", qs);
    }

    [Fact]
    public async Task GetCreditToken_omits_destination_when_null()
    {
        var (api, cap) = Build();

        await api.GetCreditTokenAsync(
            creditValue: 50_000.0,
            months: 1,
            frequency: 30,
            typeDocument: "CC",
            idDocument: "43057659",
            destination: null,
            storeId: null);

        var qs = cap.LastRequest!.RequestUri!.Query;
        Assert.DoesNotContain("destination", qs);
    }

    // --- 4. create (POST) ---

    [Fact]
    public async Task Create_builds_POST_with_json_body()
    {
        var (api, cap) = Build();

        var req = new SistecreditoTEF.Maui.Dtos.CreateCreditRequest(
            TypeDocument: "CC",
            IdDocument: "43057659",
            CreditValue: 50_000.0,
            Frequency: 30,
            Fees: 1,
            Token: "123456");

        await api.CreateAsync(req);

        Assert.NotNull(cap.LastRequest);
        Assert.Equal(HttpMethod.Post, cap.LastRequest!.Method);
        Assert.Equal("/pos/create", cap.LastRequest.RequestUri!.AbsolutePath);
        Assert.NotNull(cap.LastRequestBody);
        Assert.Contains("\"idDocument\":\"43057659\"", cap.LastRequestBody!);
        Assert.Contains("\"creditValue\":50000", cap.LastRequestBody!);
        Assert.Contains("\"Token\":\"123456\"", cap.LastRequestBody!);
    }

    // --- 5. getactivecredits ---

    [Fact]
    public async Task GetActiveCredits_builds_GET_with_correct_path()
    {
        var (api, cap) = Build();

        await api.GetActiveCreditsAsync("CC", "43057659", storeId: null);

        var r = cap.LastRequest!;
        Assert.Equal(HttpMethod.Get, r.Method);
        Assert.Equal("/pos/getactivecredits", r.RequestUri!.AbsolutePath);
        var qs = r.RequestUri.Query;
        Assert.Contains("typeDocument=CC", qs);
        Assert.Contains("idDocument=43057659", qs);
    }

    // --- 6. payCredit (POST) ---

    [Fact]
    public async Task PayCredit_builds_POST_with_json_body()
    {
        var (api, cap) = Build();

        var req = new SistecreditoTEF.Maui.Dtos.PayCreditRequest(
            CreditId: "abc-123",
            TotalValuePaid: 20_000.0,
            UserName: "permoda");

        await api.PayCreditAsync(req);

        Assert.NotNull(cap.LastRequest);
        Assert.Equal(HttpMethod.Post, cap.LastRequest!.Method);
        Assert.Equal("/pos/payCredit", cap.LastRequest.RequestUri!.AbsolutePath);
        Assert.NotNull(cap.LastRequestBody);
        Assert.Contains("\"creditId\":\"abc-123\"", cap.LastRequestBody!);
        Assert.Contains("\"totalValuePaid\":20000", cap.LastRequestBody!);
        Assert.Contains("\"userName\":\"permoda\"", cap.LastRequestBody!);
    }

    // --- 7. getSimulatedMonthLimit ---

    [Fact]
    public async Task GetSimulatedMonthLimit_builds_GET_with_creditValue()
    {
        var (api, cap) = Build();

        await api.GetSimulatedMonthLimitAsync(500_000.0, storeId: null);

        var qs = cap.LastRequest!.RequestUri!.Query;
        Assert.Equal("/pos/getSimulatedMonthLimit", cap.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("creditValue=500000", qs);
    }
}
