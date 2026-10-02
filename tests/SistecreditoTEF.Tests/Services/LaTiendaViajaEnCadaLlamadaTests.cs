using SistecreditoTEF.Maui.Dtos;
using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Tests.Services;

/// <summary>
/// LA TIENDA TIENE QUE VIAJAR EN TODAS LAS LLAMADAS, NO EN CASI TODAS.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTO FIJA
/// ─────────────────────────────────────────────────────────────────────────────
/// De las siete llamadas a Credinet, seis mandaban el <c>storeId</c> y una no:
/// <c>payCredit</c>. El campo existia en [PayCreditRequest] desde el principio,
/// con default null, y el repositorio simplemente no se lo pasaba.
///
/// Resultado en produccion: TODOS los recaudos salian sin tienda y Credinet los
/// atribuia donde tuviera por defecto, no donde se habian cobrado. Una caja de la
/// 012 cobraba un abono y en la plataforma de Sistecredito aparecia a nombre de
/// otra tienda.
///
/// Por que no se vio antes:
///
///   · Desde la caja no hay ninguna senal. El abono responde OK, el comprobante
///     sale por la termica y el saldo del cliente baja. Todo correcto salvo a
///     nombre de quien quedo.
///   · El unico lugar donde se nota es conciliando en la plataforma, semanas
///     despues.
///   · En sandbox es INVISIBLE por construccion: ahi el storeId va en null a
///     proposito en las siete llamadas, asi que payCredit se comportaba igual que
///     las demas y ninguna prueba manual podia distinguirlas.
///
/// Por eso la prueba recorre la lista COMPLETA en vez de probar payCredit: el
/// defecto no fue que esa llamada estuviera mal escrita, sino que nadie
/// comprobaba que la lista estuviera entera. Una llamada nueva que se olvide del
/// storeId tiene que romper aqui.
/// </summary>
public class LaTiendaViajaEnCadaLlamadaTests
{
    private const string Store012 = "5e87f83eee08ad0001b1356e";

    /// <summary>
    /// Anota lo que recibio cada llamada y responde SIEMPRE un error de negocio.
    ///
    /// Responder error es deliberado: lo que se verifica es lo que SALE, y asi no
    /// hay que fabricar un DTO valido por endpoint —trabajo que no aporta nada a
    /// esta prueba y que la ataria a la forma de cada respuesta—.
    /// </summary>
    private sealed class ApiQueAnota : ICredinetApi
    {
        public List<string?> StoreIdsRecibidos { get; } = [];

        private static ApiResponse<T> Error<T>() =>
            new(Function: null, ErrorCode: 999, Message: "no importa", Country: "co", Data: default);

        private ApiResponse<T> Anotar<T>(string? storeId)
        {
            StoreIdsRecibidos.Add(storeId);
            return Error<T>();
        }

        public Task<ApiResponse<ClientDto>> GetCreditLimitClientAsync(string t, string i, string? s) =>
            Task.FromResult(Anotar<ClientDto>(s));

        public Task<ApiResponse<CreditDetailsDto>> GetCreditDetailsAsync(
            double c, int f, int m, string t, string i, string? s) =>
            Task.FromResult(Anotar<CreditDetailsDto>(s));

        public Task<ApiResponse<CreditTokenDto>> GetCreditTokenAsync(
            double c, int m, int f, string t, string i, int? d, string? s) =>
            Task.FromResult(Anotar<CreditTokenDto>(s));

        public Task<ApiResponse<CreditDto>> CreateAsync(CreateCreditRequest r) =>
            Task.FromResult(Anotar<CreditDto>(r.StoreId));

        public Task<ApiResponse<List<ActiveCreditDto>>> GetActiveCreditsAsync(string t, string i, string? s) =>
            Task.FromResult(Anotar<List<ActiveCreditDto>>(s));

        public Task<ApiResponse<PaymentDto>> PayCreditAsync(PayCreditRequest r) =>
            Task.FromResult(Anotar<PaymentDto>(r.StoreId));

        public Task<ApiResponse<SimulatedMonthLimitDto>> GetSimulatedMonthLimitAsync(double c, string? s) =>
            Task.FromResult(Anotar<SimulatedMonthLimitDto>(s));
    }

    private static (CredinetRepository repo, ApiQueAnota api) Armar(string? storeId)
    {
        var api = new ApiQueAnota();
        var config = new ApiConfig
        {
            SubscriptionKey = "clave",
            BaseUrl = "https://api.credinet.co/posprod/",
            StoreId = storeId
        };
        return (new CredinetRepository(api, new StaticApiConfigSource(config)), api);
    }

    /// <summary>Las siete llamadas, una detras de otra, con la misma tienda.</summary>
    private static async Task EjercitarTodasAsync(CredinetRepository repo)
    {
        await repo.GetCreditLimitClientAsync("CC", "1026260942");
        await repo.GetCreditDetailsAsync(500000, 30, 6, "CC", "1026260942");
        await repo.SolicitarClaveDinamicaAsync(500000, 30, 6, "CC", "1026260942");
        await repo.CrearCreditoAsync(500000, 30, 6, "CC", "1026260942", "123456");
        await repo.GetActiveCreditsAsync("CC", "1026260942");
        await repo.PagarCreditoAsync("un-credito", 104926, "jperez");
        await repo.GetSimulatedMonthLimitAsync(500000);
    }

    /// <summary>
    /// LA PRUEBA QUE IMPORTA. Si una sola llamada se olvida de la tienda, el
    /// credito o el abono que pase por ahi queda a nombre de otra.
    /// </summary>
    [Fact]
    public async Task En_produccion_la_tienda_viaja_en_las_siete_llamadas()
    {
        var (repo, api) = Armar(Store012);

        await EjercitarTodasAsync(repo);

        Assert.Equal(7, api.StoreIdsRecibidos.Count);
        Assert.All(api.StoreIdsRecibidos, s => Assert.Equal(Store012, s));
    }

    /// <summary>
    /// El abono por separado, con nombre propio: es el que faltaba, y es el que
    /// mas caro sale porque mueve plata que ya se cobro.
    /// </summary>
    [Fact]
    public async Task El_abono_lleva_la_tienda_donde_se_cobro()
    {
        var (repo, api) = Armar(Store012);

        await repo.PagarCreditoAsync("un-credito", 104926, "jperez");

        Assert.Equal(Store012, Assert.Single(api.StoreIdsRecibidos));
    }

    /// <summary>
    /// Y en sandbox NINGUNA la lleva, que tambien es a proposito: el ambiente de
    /// pruebas no conoce las tiendas de la hoja y responde StoreNotFound a
    /// cualquier llamada que lleve una. Ver [ApiConfig.StoreId].
    ///
    /// Esta mitad importa tanto como la otra: sin ella, "arreglar" el abono
    /// mandando siempre la tienda dejaria el sandbox sin poder cobrar — que es
    /// exactamente lo que ya paso una vez.
    /// </summary>
    [Fact]
    public async Task En_sandbox_no_viaja_en_ninguna()
    {
        var (repo, api) = Armar(null);

        await EjercitarTodasAsync(repo);

        Assert.Equal(7, api.StoreIdsRecibidos.Count);
        Assert.All(api.StoreIdsRecibidos, Assert.Null);
    }
}
