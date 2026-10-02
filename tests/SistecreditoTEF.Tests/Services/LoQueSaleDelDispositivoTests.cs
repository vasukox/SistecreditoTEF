using System.Net;
using System.Text;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Tiendas;
using Xunit;
using Xunit.Abstractions;

namespace SistecreditoTEF.Tests.Services;

/// <summary>
/// QUE SALE DEL DISPOSITIVO, EXACTAMENTE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTA PRUEBA NO SE PARECE A LAS OTRAS
/// ─────────────────────────────────────────────────────────────────────────────
/// [LaTiendaViajaEnCadaLlamadaTests] comprueba que el repositorio le PASE la
/// tienda al cliente. Esta baja un escalon mas y mira el <c>HttpRequestMessage</c>
/// ya armado: la URL con su query string y el JSON del cuerpo, tal como viajan por
/// el cable.
///
/// Esa diferencia importa. Entre el repositorio y la red hay serializacion, y ahi
/// se pierden cosas en silencio: un <c>JsonIgnoreCondition.WhenWritingNull</c>, un
/// nombre de propiedad que no coincide, un parametro que no se agrega a la query
/// cuando viene vacio. Comprobar la intencion no demuestra el resultado.
///
/// Se recorren TRES tiendas reales de la hoja y las SIETE llamadas de cada una, de
/// punta a punta: del codigo de tienda ("012") al texto que sale del terminal.
/// </summary>
public class LoQueSaleDelDispositivoTests(ITestOutputHelper salida)
{
    /// <summary>Las tres tiendas del encargo, por codigo. El StoreId sale de la hoja.</summary>
    public static TheoryData<string> Tiendas => ["012", "037", "041"];

    private sealed record Peticion(string Metodo, string Url, string? Cuerpo)
    {
        /// <summary>Donde sea que viaje la tienda: query string o cuerpo JSON.</summary>
        public string Todo => Url + " " + (Cuerpo ?? string.Empty);
    }

    /// <summary>
    /// Anota cada peticion y responde un error de negocio valido. Responder error
    /// es deliberado: lo que se mira es lo que SALE.
    /// </summary>
    private sealed class Cable : HttpMessageHandler
    {
        public List<Peticion> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Peticiones.Add(new Peticion(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                cuerpo));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"function":"x","errorCode":999,"message":"no importa","country":"co","data":null}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private static (CredinetRepository repo, Cable cable) Armar(string? storeId)
    {
        var cable = new Cable();
        var config = new ApiConfig
        {
            SubscriptionKey = "clave-de-prueba",
            BaseUrl = "https://api.credinet.co/posprod/",
            StoreId = storeId
        };

        var http = new HttpClient(cable) { BaseAddress = new Uri(config.BaseUrl) };
        var api = new CredinetApiClient(http, config);

        return (new CredinetRepository(api, new StaticApiConfigSource(config)), cable);
    }

    /// <summary>Las siete llamadas del modulo, en el orden en que las usa una venta y un abono.</summary>
    private static async Task EjercitarTodasAsync(CredinetRepository repo)
    {
        await repo.GetCreditLimitClientAsync("CC", "1026260942");
        await repo.GetSimulatedMonthLimitAsync(500000);
        await repo.GetCreditDetailsAsync(500000, 30, 6, "CC", "1026260942");
        await repo.SolicitarClaveDinamicaAsync(500000, 30, 6, "CC", "1026260942");
        await repo.CrearCreditoAsync(500000, 30, 6, "CC", "1026260942", "123456");
        await repo.GetActiveCreditsAsync("CC", "1026260942");
        await repo.PagarCreditoAsync("f65521f5-ae10-ce18-c78f-08df046c4f85", 104926, "jperez");
    }

    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA PRUEBA DEL ENCARGO. Para cada una de las tres tiendas: las siete
    /// peticiones salen con el StoreId de ESA tienda, el de la hoja, y con ningun
    /// otro.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tiendas))]
    public async Task Las_siete_peticiones_salen_con_la_tienda_correcta(string codigo)
    {
        var tienda = CatalogoDeTiendas.PorCodigo(codigo);
        Assert.NotNull(tienda);

        var (repo, cable) = Armar(tienda.StoreId);
        await EjercitarTodasAsync(repo);

        salida.WriteLine($"=== TIENDA {tienda.Etiqueta} ===");
        salida.WriteLine($"StoreId de la hoja: {tienda.StoreId}");
        salida.WriteLine(string.Empty);
        foreach (var p in cable.Peticiones)
        {
            salida.WriteLine($"{p.Metodo} {p.Url}");
            if (p.Cuerpo is not null) salida.WriteLine($"     cuerpo: {p.Cuerpo}");
        }
        salida.WriteLine(string.Empty);

        Assert.Equal(7, cable.Peticiones.Count);

        foreach (var p in cable.Peticiones)
        {
            Assert.Contains(tienda.StoreId, p.Todo, StringComparison.Ordinal);

            // Y NINGUNA otra tienda de la hoja puede aparecer en la misma
            // peticion. Sin esto, un storeId pegado por error en otro parametro
            // pasaria desapercibido mientras el correcto tambien estuviera.
            foreach (var otra in CatalogoDeTiendas.Todas)
            {
                if (otra.StoreId == tienda.StoreId) continue;
                Assert.DoesNotContain(otra.StoreId, p.Todo, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// El detalle que se perdia en la serializacion: en los dos POST la tienda va
    /// en el CUERPO, no en la URL, y el nombre del campo tiene que ser
    /// exactamente <c>storeId</c>. Un nombre distinto viaja igual y el servidor lo
    /// ignora sin decir nada.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tiendas))]
    public async Task En_los_dos_POST_la_tienda_va_en_el_cuerpo_con_el_nombre_exacto(string codigo)
    {
        var tienda = CatalogoDeTiendas.PorCodigo(codigo)!;
        var (repo, cable) = Armar(tienda.StoreId);

        await repo.CrearCreditoAsync(500000, 30, 6, "CC", "1026260942", "123456");
        await repo.PagarCreditoAsync("un-credito", 104926, "jperez");

        var posts = cable.Peticiones.Where(p => p.Metodo == "POST").ToList();
        Assert.Equal(2, posts.Count);
        Assert.Contains(posts, p => p.Url.Contains("create", StringComparison.Ordinal));
        Assert.Contains(posts, p => p.Url.Contains("payCredit", StringComparison.Ordinal));

        foreach (var p in posts)
        {
            Assert.NotNull(p.Cuerpo);
            Assert.Contains($"\"storeId\":\"{tienda.StoreId}\"", p.Cuerpo, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Y en sandbox no viaja en ninguna, que tambien hay que demostrarlo: el
    /// ambiente de pruebas no conoce las tiendas de la hoja y responde
    /// StoreNotFound a cualquiera que lleve una. Ver [ApiConfig.StoreId].
    ///
    /// Con <c>WhenWritingNull</c> en el serializador, en los POST el campo ni
    /// siquiera aparece en el JSON — que es lo correcto, pero conviene tenerlo
    /// escrito y no deducido.
    /// </summary>
    [Fact]
    public async Task En_sandbox_ninguna_peticion_lleva_tienda()
    {
        var (repo, cable) = Armar(storeId: null);

        await EjercitarTodasAsync(repo);

        Assert.Equal(7, cable.Peticiones.Count);
        Assert.All(cable.Peticiones, p =>
            Assert.DoesNotContain("storeId", p.Todo, StringComparison.OrdinalIgnoreCase));
    }
}
