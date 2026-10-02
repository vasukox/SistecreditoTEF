using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Tiendas;
using Xunit;

namespace SistecreditoTEF.Tests.Services;

/// <summary>
/// EL AMBIENTE LO DECIDE EL PAQUETE, Y CLOUDLICENSE NO PUEDE DEGRADARLO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTO FIJA
/// ─────────────────────────────────────────────────────────────────────────────
/// El ambiente se leia con la precedencia normal —CloudLicense gana sobre
/// appsettings— y el ambiente decide si el StoreId viaja:
///
///     StoreId = esProduccion ? tienda.StoreId : null
///
/// Ademas se comparaba por IGUALDAD contra "production" o "prod". Juntando las
/// dos cosas, un APK de PRODUCCION al que ICG le mandara un ENVIRONMENT escrito
/// de otra forma —"produccion" en español, "PRODUCTIVO", o un valor que quedara
/// de una homologacion— dejaba de enviar la tienda. Sin ninguna señal:
///
///   · la pantalla seguia mostrando la tienda correcta, porque muestra la
///     ELEGIDA y no la que viaja;
///   · [Validate] no se quejaba, porque sus comprobaciones de produccion estan
///     dentro de `if (IsProduction)`, que era false;
///   · y las peticiones salian a /posprod/ con la credencial real pero SIN
///     tienda, asi que se atribuian al valor por defecto del otro lado.
///
/// CloudLicense se provisiona TERMINAL POR TERMINAL. Por eso el sintoma era el
/// mas desconcertante posible: una tienda factura bien y la de al lado no, con el
/// mismo APK y las dos bien configuradas en pantalla.
/// </summary>
public class ElAmbienteLoDecideElPaqueteTests
{
    private const string Store012 = "5e87f83eee08ad0001b1356e";

    private sealed class Nube(string? ambiente) : ICloudConfig
    {
        public string? Get(string key) =>
            string.Equals(key, ICloudConfig.Environment, StringComparison.OrdinalIgnoreCase)
                ? ambiente
                : null;
    }

    private static ApiConfig Config(string ambienteDelPaquete, string? ambienteDeIcg) =>
        ApiConfig.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Credinet:SubscriptionKey"] = "clave-real",
                ["Credinet:BaseUrl"] = "https://api.credinet.co/posprod/",
                ["Credinet:Environment"] = ambienteDelPaquete
            }).Build(),
            new Nube(ambienteDeIcg),
            storeIdDeLaCaja: Store012);

    // ══════════════════════════════════════════════════════════════════════════
    // LA REGRESION
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA PRUEBA QUE IMPORTA. Un APK de produccion manda la tienda, diga lo que
    /// diga CloudLicense. Estos cinco valores son los que un humano escribe en un
    /// panel de provisionamiento; ninguno puede apagar el envio de la tienda.
    /// </summary>
    [Theory]
    [InlineData("produccion")]      // español, el caso mas probable
    [InlineData("PRODUCCIÓN")]
    [InlineData("PRODUCTIVO")]
    [InlineData("sandbox")]         // un valor de homologacion que quedo puesto
    [InlineData("")]
    [InlineData(null)]
    public void Un_APK_de_produccion_manda_la_tienda_diga_lo_que_diga_CloudLicense(string? deIcg)
    {
        var config = Config(ambienteDelPaquete: "production", ambienteDeIcg: deIcg);

        Assert.Equal(Store012, config.StoreId);
        Assert.True(config.IsProduction);
        Assert.True(config.PaqueteDeProduccion);
        Assert.Empty(config.Validate());
    }

    /// <summary>
    /// Y la barrera, por si algun dia se vuelve a colar una forma de degradar el
    /// ambiente: con la tienda elegida y el StoreId sin viajar, un paquete de
    /// produccion NO opera. Vender asi es peor que no vender — la venta sale bien
    /// y el credito queda a nombre de otro.
    /// </summary>
    [Fact]
    public void Si_la_tienda_esta_elegida_pero_no_viaja_el_paquete_de_produccion_no_opera()
    {
        var config = Config("production", null) with
        {
            StoreId = null   // como si algo hubiera degradado el ambiente
        };

        var problemas = config.Validate();

        Assert.Contains(problemas, p =>
            p.Contains("LA TIENDA NO VIAJA", StringComparison.Ordinal));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LO QUE SIGUE FUNCIONANDO IGUAL
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ICG puede PROMOVER un paquete de pruebas a produccion: eso permite
    /// homologar sin recompilar y nunca fue el problema. Lo que no puede es lo
    /// contrario.
    /// </summary>
    [Theory]
    [InlineData("production")]
    [InlineData("produccion")]
    [InlineData("prod")]
    public void CloudLicense_si_puede_promover_un_paquete_de_pruebas(string deIcg)
    {
        var config = Config(ambienteDelPaquete: "sandbox", ambienteDeIcg: deIcg);

        Assert.True(config.IsProduction);
        Assert.False(config.PaqueteDeProduccion);
        Assert.Equal(Store012, config.StoreId);
    }

    /// <summary>
    /// Un paquete de pruebas sin nada de ICG sigue sin mandar la tienda: el
    /// sandbox de Credinet no conoce las tiendas de la hoja y responde
    /// StoreNotFound a todo lo que la lleve. Ver [ApiConfig.StoreId].
    /// </summary>
    [Fact]
    public void Un_paquete_de_pruebas_sigue_sin_mandar_la_tienda()
    {
        var config = Config(ambienteDelPaquete: "sandbox", ambienteDeIcg: null);

        Assert.False(config.IsProduction);
        Assert.Null(config.StoreId);

        // Y no se queja por "la tienda no viaja": en pruebas es lo correcto.
        Assert.DoesNotContain(config.Validate(), p =>
            p.Contains("LA TIENDA NO VIAJA", StringComparison.Ordinal));
    }

    /// <summary>
    /// La comparacion por prefijo, aislada. Se cambio igualdad por prefijo porque
    /// el valor lo teclea otra empresa, en otro idioma: exigir "production" exacto
    /// no es validar, es poner una trampa.
    /// </summary>
    [Theory]
    [InlineData("production", true)]
    [InlineData("prod", true)]
    [InlineData("produccion", true)]
    [InlineData("PRODUCCIÓN", true)]
    [InlineData("  Prod  ", true)]
    [InlineData("sandbox", false)]
    [InlineData("test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Como_se_reconoce_el_ambiente_de_produccion(string? valor, bool esperado) =>
        Assert.Equal(esperado, ApiConfig.EsProduccion(valor));
}
