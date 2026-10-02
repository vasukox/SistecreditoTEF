using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Tiendas;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Tiendas;

/// <summary>
/// EL BOTON QUE PREGUNTA SI ESTA TIENDA EXISTE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL HUECO QUE VIENE A TAPAR
/// ─────────────────────────────────────────────────────────────────────────────
/// El semaforo de la cabecera ([IEstadoDeLaConexion]) es PASIVO: se entera del
/// estado de la tienda a partir de las llamadas que el modulo hace operando. Eso
/// deja sin cubrir justo el momento del montaje — el instalador elige la tienda,
/// no vende nada, y se va con el indicador en "sin verificar". El error aparecia
/// con la primera venta de la tienda, que es exactamente lo que se queria evitar.
///
/// Esta comprobacion es explicita y no escribe nada.
/// </summary>
public class VerificarLaTiendaTests
{
    private const string Store037 = "607af8e38c91f70001436058";

    /// <summary>Devuelve siempre la misma respuesta, para fijar cada rama.</summary>
    private sealed class CredinetQueResponde(ApiResult<SimulatedMonthLimit> respuesta)
        : ICredinetRepository
    {
        public int Llamadas { get; private set; }

        public Task<ApiResult<SimulatedMonthLimit>> GetSimulatedMonthLimitAsync(double creditValue)
        {
            Llamadas++;
            return Task.FromResult(respuesta);
        }

        // El resto no participa de la comprobacion.
        public Task<ApiResult<Client>> GetCreditLimitClientAsync(string t, string i) =>
            throw new NotSupportedException();
        public Task<ApiResult<CreditDetails>> GetCreditDetailsAsync(
            double c, int f, int m, string t, string i) => throw new NotSupportedException();
        public Task<ApiResult<CreditToken>> SolicitarClaveDinamicaAsync(
            double c, int f, int m, string t, string i, int? d = null) => throw new NotSupportedException();
        public Task<ApiResult<Credit>> CrearCreditoAsync(
            double c, int f, int m, string t, string i, string tok,
            string source = "2", int auth = 1,
            string? inv = null, string? sel = null, string? pro = null) => throw new NotSupportedException();
        public Task<ApiResult<List<ActiveCredit>>> GetActiveCreditsAsync(string t, string i) =>
            throw new NotSupportedException();
        public Task<ApiResult<Payment>> PagarCreditoAsync(string c, double v, string u) =>
            throw new NotSupportedException();
    }

    private static ApiConfig Config(bool produccion, string? storeId) =>
        ApiConfig.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Credinet:SubscriptionKey"] = "clave",
                ["Credinet:BaseUrl"] = "https://api.credinet.co/posprod/",
                ["Credinet:Environment"] = produccion ? "production" : "sandbox"
            }).Build(),
            new SinNube(),
            storeIdDeLaCaja: storeId);

    private sealed class SinNube : ICloudConfig
    {
        public string? Get(string key) => null;
    }

    private static VerificacionDeTienda Armar(
        ApiResult<SimulatedMonthLimit> respuesta,
        bool produccion = true,
        string? storeId = Store037) =>
        new(new CredinetQueResponde(respuesta),
            new StaticApiConfigSource(Config(produccion, storeId)));

    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Si_Sistecredito_contesta_la_tienda_queda_aceptada()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(new SimulatedMonthLimit(6)));

        Assert.Equal(ResultadoDeVerificacion.Aceptada, await verificador.VerificarAsync());
    }

    /// <summary>
    /// LA RAMA QUE IMPORTA. 225 StoreNotFound es el unico "no" sobre la tienda, y
    /// es el que tiene que dejar al instalador sin dudas antes de irse del local.
    /// </summary>
    [Fact]
    public async Task Si_Sistecredito_dice_que_la_tienda_no_existe_se_rechaza()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                new ApiError.Business(225, "StoreNotFound", "/getSimulatedMonthLimit")));

        Assert.Equal(ResultadoDeVerificacion.Rechazada, await verificador.VerificarAsync());
    }

    /// <summary>
    /// Otro error de negocio significa que contestaron y que la tienda les sirvio:
    /// lo que no les gusto fue otra cosa. Confundirlo con un rechazo de tienda
    /// dejaria cajas sanas sin poder operar.
    /// </summary>
    [Fact]
    public async Task Otro_error_de_negocio_no_es_un_rechazo_de_tienda()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                new ApiError.Business(223, "RequestValuesInvalid", "/getSimulatedMonthLimit")));

        Assert.Equal(ResultadoDeVerificacion.Aceptada, await verificador.VerificarAsync());
    }

    /// <summary>
    /// Sin red no se puede afirmar nada de la tienda. Decir "rechazada" ahi seria
    /// bloquear una caja sana por un corte de wifi.
    /// </summary>
    [Fact]
    public async Task Sin_poder_hablar_con_Sistecredito_no_se_concluye_nada()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit>(
                new ApiError.Network(new HttpRequestException("sin red"))));

        Assert.Equal(ResultadoDeVerificacion.SinRespuesta, await verificador.VerificarAsync());
    }

    /// <summary>
    /// En pruebas la tienda NO viaja a Credinet a proposito, asi que la llamada
    /// saldria sin tienda y volveria OK siempre. Dar verde ahi seria decirle al
    /// instalador que comprobo algo que no comprobo.
    /// </summary>
    [Fact]
    public async Task En_sandbox_se_dice_que_no_hay_nada_que_comprobar()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(new SimulatedMonthLimit(6)),
            produccion: false);

        Assert.Equal(ResultadoDeVerificacion.NoAplicaEnPruebas, await verificador.VerificarAsync());
    }

    [Fact]
    public async Task Sin_tienda_elegida_no_se_pregunta_nada()
    {
        var verificador = Armar(
            new ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit>(new SimulatedMonthLimit(6)),
            storeId: null);

        Assert.Equal(ResultadoDeVerificacion.NoAplicaEnPruebas, await verificador.VerificarAsync());
    }
}
