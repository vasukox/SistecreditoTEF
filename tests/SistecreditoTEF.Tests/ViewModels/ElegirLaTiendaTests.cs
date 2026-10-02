using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Tiendas;
using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// La pantalla con la que el instalador deja la caja operando como su tienda.
///
/// Lo que se ejercita acá es lo que no se puede comprobar mirando el terminal:
/// que elegir una tienda tenga efecto INMEDIATO sobre la configuración que usa el
/// módulo, y que un conflicto se vea nombrando las dos tiendas en disputa en vez
/// de resolverse solo.
/// </summary>
public class ElegirLaTiendaTests
{
    private const string Store037 = "607af8e38c91f70001436058";
    private const string StoreSuba = "607d8d208c91f70001439630";

    private sealed class CloudFalso(string? storeId) : ICloudConfig
    {
        public string? Get(string key) =>
            string.Equals(key, ICloudConfig.StoreId, StringComparison.OrdinalIgnoreCase)
                ? storeId
                : null;
    }

    private static TiendaViewModel Armar(
        string? deHiopos, string? yaElegida, out ITiendaDeLaCaja caja, out ApiConfigProvider cfg)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Credinet:SubscriptionKey"] = "clave",
                ["Credinet:BaseUrl"] = "https://api.credinet.co/posprod/",
                ["Credinet:Environment"] = "production",
                ["Credinet:StoreId"] = ""
            })
            .Build();

        var local = new TiendaDeLaCajaEnMemoria();
        if (yaElegida is not null)
            local.Fijar(CatalogoDeTiendas.PorStoreId(yaElegida)!);

        caja = local;
        cfg = new ApiConfigProvider(settings, new CloudFalso(deHiopos), local);

        var vm = new TiendaViewModel(local, cfg);
        vm.Cargar();
        return vm;
    }

    // ------------------------------------------------------------------

    [Fact]
    public void LaListaArranca_ConTodasLasTiendas()
    {
        var vm = Armar(null, null, out _, out _);

        Assert.Equal(CatalogoDeTiendas.Todas.Count, vm.Tiendas.Count);
    }

    [Fact]
    public void AlBuscar_SeFiltra()
    {
        var vm = Armar(null, null, out _, out _);

        vm.Busqueda = "037";

        Assert.Equal("037", vm.Tiendas[0].Codigo);
        Assert.True(vm.Tiendas.Count < CatalogoDeTiendas.Todas.Count);
    }

    /// <summary>
    /// LA PRUEBA QUE IMPORTA. Elegir tiene que surtir efecto en la configuración
    /// que el módulo usa de verdad, no solo en lo que se ve.
    ///
    /// Sin el <c>Reload</c>, la pantalla mostraría la tienda nueva y Credinet
    /// seguiría recibiendo la anterior hasta el próximo INITIALIZE — exactamente la
    /// clase de desfase entre lo mostrado y lo usado que originó todo esto.
    /// </summary>
    [Fact]
    public void AlElegirUnaTienda_LaConfiguracionLaTomaEnElActo()
    {
        var vm = Armar(deHiopos: null, yaElegida: null, out var caja, out var cfg);

        Assert.Null(cfg.Current.StoreId);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));

        Assert.Equal(Store037, caja.StoreId);
        Assert.Equal(Store037, cfg.Current.StoreId);
        Assert.Equal(EstadoDeLaTienda.Elegida, cfg.Current.Tienda.Estado);

        // Y con eso la caja ya puede cobrar en producción.
        Assert.Empty(cfg.Current.Validate());
    }

    /// <summary>
    /// Al entrar, el título es la tienda nombrada y el detalle trae el StoreId
    /// COMPLETO. Se reportó desde la terminal que al volver de elegir no se veía
    /// cuál había quedado; para cotejarlo contra la hoja hacen falta los 24
    /// caracteres, no un recorte.
    /// </summary>
    [Fact]
    public void ConTiendaElegida_SeVeElNombreYElStoreIdCompleto()
    {
        var vm = Armar(deHiopos: null, yaElegida: Store037, out _, out _);

        Assert.Contains("037", vm.Titulo, StringComparison.Ordinal);
        Assert.Contains(Store037, vm.Detalle, StringComparison.Ordinal);
    }

    [Fact]
    public void SinTiendaElegida_SeDiceQueFaltaElegirla()
    {
        var vm = Armar(deHiopos: Store037, yaElegida: null, out _, out _);

        Assert.Contains("elegir", vm.Titulo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlElegir_SeConfirmaConElNombreDeLaTienda()
    {
        var vm = Armar(null, null, out _, out _);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));

        Assert.True(vm.TieneMensajeOk);
        Assert.Contains("037", vm.MensajeOk, StringComparison.Ordinal);
    }

    /// <summary>
    /// Si HioPosCloud dice otra tienda se AVISA, nombrando la suya, pero la caja
    /// sigue operando con la elegida aquí. Dejarla sin vender porque ICG cree otra
    /// cosa sería peor que el aviso.
    /// </summary>
    [Fact]
    public void SiHioPosDiceOtra_SeAvisa_PeroNoFrena()
    {
        var vm = Armar(deHiopos: Store037, yaElegida: StoreSuba, out _, out var cfg);

        Assert.True(vm.HayAvisoDeHioPos);
        Assert.Contains("037", vm.AvisoDeHioPos, StringComparison.Ordinal);

        Assert.Equal(StoreSuba, cfg.Current.StoreId);
        Assert.Empty(cfg.Current.Validate());
    }

    /// <summary>
    /// Y el aviso desaparece eligiendo la que dice el POS, si esa era la correcta.
    /// </summary>
    [Fact]
    public void ElegirLaMismaQueHioPos_ApagaElAviso()
    {
        var vm = Armar(deHiopos: Store037, yaElegida: StoreSuba, out _, out var cfg);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));

        Assert.False(vm.HayAvisoDeHioPos);
        Assert.Equal(Store037, cfg.Current.StoreId);
        Assert.Empty(cfg.Current.Validate());
    }

    [Fact]
    public void SiCoinciden_NoSeAvisaNada()
    {
        var vm = Armar(deHiopos: Store037, yaElegida: Store037, out _, out _);

        Assert.False(vm.HayAvisoDeHioPos);
    }

    [Fact]
    public void ElegirNada_NoHaceNada()
    {
        var vm = Armar(null, null, out var caja, out _);

        vm.ElegirCommand.Execute(null);

        Assert.Null(caja.StoreId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EL ASISTENTE DE MONTAJE
    // ══════════════════════════════════════════════════════════════════════════
    //
    // En el montaje hay un paso de mas —confirmar— y no es un capricho: ahi no hay
    // vuelta atras facil (la correccion vive detras de un PIN que todavia no
    // existe) y varias de las 76 tiendas se llaman casi igual. Un toque de mas
    // cuesta un segundo; una tienda equivocada cuesta creditos a nombre de otra.

    private static TiendaViewModel ArmarAsistente(out ITiendaDeLaCaja caja, out ApiConfigProvider cfg)
    {
        var vm = Armar(deHiopos: null, yaElegida: null, out caja, out cfg);
        vm.ModoAsistente = true;
        return vm;
    }

    /// <summary>
    /// LA PRUEBA QUE IMPORTA DEL ASISTENTE: tocar una tienda NO la aplica. Si la
    /// aplicara, el paso de confirmacion seria decorativo — la caja ya habria
    /// quedado asociada al primer dedazo.
    /// </summary>
    [Fact]
    public void EnElAsistente_TocarUnaTiendaNoLaAplicaTodavia()
    {
        var vm = ArmarAsistente(out var caja, out var cfg);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));

        Assert.Equal("037", vm.Seleccionada?.Codigo);
        Assert.Null(caja.StoreId);
        Assert.Null(cfg.Current.StoreId);

        // Y la pantalla pasa a pedir la confirmacion en vez de seguir en la lista.
        Assert.True(vm.MostrandoConfirmacion);
        Assert.False(vm.MostrandoLista);
    }

    [Fact]
    public async Task EnElAsistente_AlAceptar_LaTiendaQuedaAplicada()
    {
        var vm = ArmarAsistente(out var caja, out var cfg);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));
        await vm.AceptarCommand.ExecuteAsync(null);

        Assert.Equal(Store037, caja.StoreId);
        Assert.Equal(Store037, cfg.Current.StoreId);
        Assert.True(vm.Confirmada);
        Assert.True(vm.MostrandoListo);
        Assert.Empty(cfg.Current.Validate());
    }

    [Fact]
    public void EnElAsistente_SePuedeVolverALaListaSinAplicarNada()
    {
        var vm = ArmarAsistente(out var caja, out _);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));
        vm.ElegirOtraCommand.Execute(null);

        Assert.Null(vm.Seleccionada);
        Assert.Null(caja.StoreId);
        Assert.True(vm.MostrandoLista);
    }

    /// <summary>
    /// Aceptar sin nada tocado no puede dar por confirmada una caja sin tienda.
    /// </summary>
    [Fact]
    public async Task EnElAsistente_AceptarSinSeleccion_NoHaceNada()
    {
        var vm = ArmarAsistente(out var caja, out _);

        await vm.AceptarCommand.ExecuteAsync(null);

        Assert.False(vm.Confirmada);
        Assert.Null(caja.StoreId);
    }

    /// <summary>
    /// En AJUSTES no hay paso intermedio: se toca y queda. Quien llega ahi viene
    /// desde administracion, detras del PIN, y ya sabe a que vino.
    /// </summary>
    [Fact]
    public void FueraDelAsistente_NoHayPasoDeConfirmacion()
    {
        var vm = Armar(null, null, out var caja, out _);

        vm.ElegirCommand.Execute(CatalogoDeTiendas.PorCodigo("037"));

        Assert.Equal(Store037, caja.StoreId);
        Assert.False(vm.MostrandoConfirmacion);
        Assert.True(vm.MostrandoLista);
    }
}
