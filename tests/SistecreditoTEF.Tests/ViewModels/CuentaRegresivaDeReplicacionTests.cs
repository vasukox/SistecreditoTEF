using Microsoft.Extensions.Configuration;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// La pantalla de copia entre cajas: la cuenta regresiva del codigo y las puertas
/// del lado que recibe.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE SE PRUEBA UNA PANTALLA
/// ─────────────────────────────────────────────────────────────────────────────
/// El protocolo ya tiene sus pruebas ([ReplicacionEntreCajasTests]): cifrado,
/// intentos, ventana. Lo que no tenia ninguna es lo que el instalador VE, y ahi
/// estan las decisiones que lo hacen equivocarse:
///
///   • Un codigo que parece vivo cuando ya vencio hace teclear seis digitos que
///     no van a servir, y quemar un intento de los tres.
///   • Un reloj que escribe "5:7" en vez de "5:07" se lee mal justo cuando queda
///     poco tiempo.
///   • Un boton de "traer" que se deja apretar sin direccion manda al instalador
///     a un error de red en vez de decirle que le falta un dato.
///
/// Nada de esto abre un socket: la cuenta regresiva se probo separandola del
/// [PairingHost] a proposito (ver los tres "Pintar" del ViewModel).
/// </summary>
public class CuentaRegresivaDeReplicacionTests
{
    private sealed class NavSpy : INavigationService
    {
        public List<string> Visitadas { get; } = [];

        private Task Ir(string destino) { Visitadas.Add(destino); return Task.CompletedTask; }

        public Task GoToCapturaCedulaAsync()     => Ir(nameof(GoToCapturaCedulaAsync));
        public Task GoToValidacionClienteAsync() => Ir(nameof(GoToValidacionClienteAsync));
        public Task GoToSeleccionCuotasAsync()   => Ir(nameof(GoToSeleccionCuotasAsync));
        public Task GoToOtpAsync()               => Ir(nameof(GoToOtpAsync));
        public Task GoToConfirmacionAsync(Credit credit) => Ir(nameof(GoToConfirmacionAsync));
        public Task GoToPagoAsync(ActiveCredit credit)   => Ir(nameof(GoToPagoAsync));
        public Task GoToReciboPagoAsync(Payment payment) => Ir(nameof(GoToReciboPagoAsync));
        public Task GoToCreditosActivosAsync()   => Ir(nameof(GoToCreditosActivosAsync));
        public Task GoToHomeAsync()              => Ir(nameof(GoToHomeAsync));
        public Task GoToConfigurarAdminAsync()   => Ir(nameof(GoToConfigurarAdminAsync));
        public Task GoToIngresoCajeroAsync()     => Ir(nameof(GoToIngresoCajeroAsync));
        public Task GoToAdminCajerosAsync()      => Ir(nameof(GoToAdminCajerosAsync));
        public Task GoToReplicacionAsync()       => Ir(nameof(GoToReplicacionAsync));
        public Task GoToActualizarCajerosAsync() => Ir(nameof(GoToActualizarCajerosAsync));
        public Task GoToTiendaAsync()            => Ir(nameof(GoToTiendaAsync));
        public Task GoToElegirTiendaAsistenteAsync() => Ir(nameof(GoToElegirTiendaAsistenteAsync));
        public Task GoToConfiguracionInicioAsync()   => Ir(nameof(GoToConfiguracionInicioAsync));
    }

    private static (ReplicacionViewModel vm, InMemoryAuthStore store, NavSpy nav) Armar()
    {
        var store = new InMemoryAuthStore();
        var nav = new NavSpy();
        var config = new ApiConfig
        {
            SubscriptionKey = "no-se-usa-aca",
            BaseUrl = "https://api.credinet.co/pos/",
            StoreName = "Tienda de prueba",
        };

        // La tienda de la caja y el proveedor de configuracion solo intervienen al
        // MONTAR una caja (cuando se aplica la tienda que vino en el sobre). Estos
        // tests son de la cuenta regresiva, asi que alcanzan los dobles vacios.
        var tienda = new SistecreditoTEF.Maui.Services.Tiendas.TiendaDeLaCajaEnMemoria();
        var proveedor = new ApiConfigProvider(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Credinet:SubscriptionKey"] = "no-se-usa-aca",
                    ["Credinet:BaseUrl"] = "https://api.credinet.co/pos/"
                })
                .Build(),
            EmptyCloudConfig.Instance,
            tienda);

        return (new ReplicacionViewModel(store, config, new SesionCajero(), nav, tienda, proveedor),
                store, nav);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LA CUENTA REGRESIVA
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recien abierta: barra llena y ningun aviso. Si la barra arrancara vacia, la
    /// tarjeta aparece con la ventana aparentemente vencida y se llena un instante
    /// despues; se ve como un error.
    /// </summary>
    [Fact]
    public void Recien_abierta_la_barra_esta_llena_y_sin_avisos()
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromMinutes(10), cajas: 0, intentos: 3);

        Assert.True(vm.VentanaAbierta);
        Assert.Equal(1.0, vm.FraccionRestante, precision: 3);
        Assert.False(vm.Apurando);
        Assert.False(vm.PorVencer);
    }

    [Fact]
    public void A_la_mitad_de_la_ventana_la_barra_esta_a_la_mitad()
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromMinutes(5), cajas: 0, intentos: 3);

        Assert.Equal(0.5, vm.FraccionRestante, precision: 3);
    }

    /// <summary>Bajo los 3 minutos la barra pasa a ambar, pero todavia no late.</summary>
    [Fact]
    public void Bajo_tres_minutos_avisa_en_ambar()
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromMinutes(3), cajas: 0, intentos: 3);

        Assert.True(vm.Apurando);
        Assert.False(vm.PorVencer);
    }

    /// <summary>
    /// Bajo el ultimo minuto se pone roja y el codigo late. Desde el otro extremo
    /// de la tienda no se lee un reloj, pero un pulso si se ve.
    /// </summary>
    [Fact]
    public void Bajo_un_minuto_late()
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromSeconds(59), cajas: 0, intentos: 3);

        Assert.True(vm.PorVencer);
        Assert.True(vm.Apurando);   // el rojo no cancela el ambar: es el tramo siguiente
    }

    /// <summary>
    /// "5:07", no "5:7". Un reloj mal escrito se lee mal justo cuando queda poco.
    /// </summary>
    [Fact]
    public void Los_segundos_van_siempre_con_dos_digitos()
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(new TimeSpan(0, 5, 7), cajas: 0, intentos: 3);

        Assert.Equal("5:07", vm.TiempoRestante);
    }

    /// <summary>La barra no puede pasarse de los extremos ni con un reloj corrido.</summary>
    [Theory]
    [InlineData(-30, 0.0)]
    [InlineData(0, 0.0)]
    [InlineData(900, 1.0)]
    public void La_barra_se_queda_entre_cero_y_uno(int segundos, double esperado)
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromSeconds(segundos), cajas: 0, intentos: 3);

        Assert.Equal(esperado, vm.FraccionRestante, precision: 3);
    }

    /// <summary>
    /// El contador de cajas es la UNICA confirmacion que tiene el que reparte de
    /// que del otro lado paso algo: el que copia esta en otra caja, a veces en otro
    /// piso.
    /// </summary>
    [Theory]
    [InlineData(0, "Todavia no copio ninguna caja")]
    [InlineData(1, "1 caja ya copio")]
    [InlineData(2, "2 cajas ya copiaron")]
    public void El_contador_de_cajas_se_escribe_en_castellano(int cajas, string esperado)
    {
        var (vm, _, _) = Armar();

        vm.PintarVentanaViva(TimeSpan.FromMinutes(9), cajas, intentos: 3);

        Assert.Equal(esperado, vm.CajasCopiadas);
    }

    /// <summary>
    /// Vencer por tiempo y quemarse por intentos son dos cosas distintas para el
    /// que esta mirando: una es "genera otro", la otra es "alguien estuvo tecleando
    /// codigos que no eran".
    /// </summary>
    [Fact]
    public void Vencer_por_tiempo_no_es_lo_mismo_que_quemarse()
    {
        var (porTiempo, _, _) = Armar();
        porTiempo.PintarVentanaVencida(quemado: false);

        Assert.False(porTiempo.EmisorEnFalla);
        Assert.Contains("cerro", porTiempo.EstadoEmisor, StringComparison.OrdinalIgnoreCase);

        var (quemada, _, _) = Armar();
        quemada.PintarVentanaVencida(quemado: true);

        Assert.True(quemada.EmisorEnFalla);
        Assert.Contains("intentos", quemada.EstadoEmisor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Al cerrarse, el codigo se BORRA de la pantalla. Dejarlo a la vista hace que
    /// alguien lo teclee en la otra caja y queme uno de los tres intentos contra un
    /// codigo que ya no existe.
    /// </summary>
    [Fact]
    public void Al_cerrarse_la_ventana_el_codigo_desaparece()
    {
        var (vm, _, _) = Armar();
        vm.Codigo = "123456";
        Assert.True(vm.TieneCodigo);

        vm.PintarVentanaVencida(quemado: false);

        Assert.Null(vm.Codigo);
        Assert.False(vm.TieneCodigo);
        Assert.False(vm.VentanaAbierta);
        Assert.Equal(0, vm.FraccionRestante);
    }

    /// <summary>
    /// El codigo se dicta en voz alta de una caja a otra. Seis cifras pegadas se
    /// leen mal y se repiten peor, asi que van en casillas separadas.
    /// </summary>
    [Fact]
    public void El_codigo_se_parte_en_seis_casillas_en_orden()
    {
        var (vm, _, _) = Armar();

        vm.Codigo = "046913";

        Assert.Equal(["0", "4", "6", "9", "1", "3"], vm.CodigoDigitos);
    }

    [Fact]
    public void Sin_codigo_no_hay_casillas()
    {
        var (vm, _, _) = Armar();

        Assert.Empty(vm.CodigoDigitos);
        Assert.False(vm.TieneCodigo);
    }

    /// <summary>
    /// Una caja a medio configurar NO reparte. Repartir medio padron deja a la
    /// receptora creyendose configurada y fallando al primer ingreso.
    /// </summary>
    [Fact]
    public async Task Una_caja_incompleta_no_abre_la_ventana()
    {
        var (vm, _, _) = Armar();   // sin PIN y sin cajeros

        await vm.AbrirVentanaCommand.ExecuteAsync(null);

        Assert.False(vm.VentanaAbierta);
        Assert.Null(vm.Codigo);
        Assert.True(vm.EmisorEnFalla);
        Assert.Contains("completa", vm.EstadoEmisor, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EL LADO QUE RECIBE
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Sin_direccion_no_se_puede_traer_y_se_dice_por_que()
    {
        var (vm, _, _) = Armar();
        vm.CodigoIngresado = "123456";

        Assert.False(vm.PuedeTraer);
        Assert.Contains("caja", vm.MotivoTraer, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.TraerCommand.CanExecute(null));
    }

    [Fact]
    public void Con_direccion_pero_sin_codigo_completo_tampoco()
    {
        var (vm, _, _) = Armar();
        vm.DireccionOrigen = "192.168.1.20";
        vm.CodigoIngresado = "1234";

        Assert.False(vm.PuedeTraer);
        Assert.Contains("codigo", vm.MotivoTraer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Con_los_dos_datos_el_boton_se_habilita_y_no_queda_texto_de_bloqueo()
    {
        var (vm, _, _) = Armar();
        vm.DireccionOrigen = "192.168.1.20";
        vm.CodigoIngresado = "123456";

        Assert.True(vm.PuedeTraer);
        Assert.Equal(string.Empty, vm.MotivoTraer);
        Assert.True(vm.TraerCommand.CanExecute(null));
    }

    /// <summary>
    /// EL CanExecute NO PROTEGE EL COMANDO. Apaga el boton, pero invocarlo por
    /// codigo lo ejecuta igual. Es la misma leccion que dejo
    /// [ConfigurarAdminViewModel], donde la falta de esta comprobacion guardaba un
    /// PIN que el instalador no habia escrito.
    ///
    /// Aca, sin la comprobacion, se saldria a la red con una direccion vacia y el
    /// instalador veria un error de conexion en vez de "te falta un dato".
    /// </summary>
    [Fact]
    public async Task Traer_con_datos_incompletos_avisa_en_vez_de_salir_a_la_red()
    {
        var (vm, _, _) = Armar();
        vm.CodigoIngresado = "12";   // ni direccion ni codigo completo

        await vm.TraerCommand.ExecuteAsync(null);

        Assert.True(vm.ReceptorEnFalla);
        Assert.False(vm.HayAlgoPorConfirmar);
        Assert.Equal(vm.MotivoTraer, vm.EstadoReceptor);
    }

    /// <summary>
    /// Descartar es la segunda salida de la confirmacion: si el resumen muestra una
    /// tienda que no es, el unico camino no puede ser aceptar.
    /// </summary>
    [Fact]
    public void Descartar_no_deja_nada_por_confirmar_ni_navega()
    {
        var (vm, _, nav) = Armar();

        vm.DescartarRecibidoCommand.Execute(null);

        Assert.False(vm.HayAlgoPorConfirmar);
        Assert.Null(vm.ResumenRecibido);
        Assert.False(vm.ReceptorEnFalla);
        Assert.Empty(nav.Visitadas);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // QUE MITAD SE VE
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Arranca en RECIBIR: es el caso que llega desde la pantalla de primer uso,
    /// que es por donde entra casi todo el mundo.
    /// </summary>
    [Fact]
    public void Arranca_mostrando_el_lado_que_recibe()
    {
        var (vm, _, _) = Armar();

        Assert.True(vm.ModoRecibir);
        Assert.False(vm.ModoCompartir);
    }

    /// <summary>
    /// ACTUALIZAR VA EN LOS DOS SENTIDOS, Y ESO SE ELIGE.
    ///
    /// La primera version daba por hecho que quien abria "actualizar cajeros" venia a
    /// RECIBIR. Pero el padron al dia lo puede tener cualquiera de las tres cajas: el
    /// que acaba de dar de alta a un cajero esta parado frente a la suya, y desde ahi
    /// lo normal es querer enviarlo.
    ///
    /// Obligarlo a recibir lo mandaba a caminar hasta la otra caja — o, peor, a
    /// traerse el padron VIEJO encima del suyo, que es justo la baja silenciosa que
    /// el diferencial vino a evitar.
    /// </summary>
    [Fact]
    public void Al_actualizar_cajeros_tambien_se_puede_enviar()
    {
        var (vm, _, _) = Armar();
        vm.SoloCajeros = true;

        // Arranca en recibir, como antes...
        Assert.True(vm.ModoRecibir);

        // ...pero el sentido contrario esta disponible.
        vm.VerCompartirCommand.Execute(null);
        Assert.True(vm.ModoCompartir);
    }

    /// <summary>
    /// Al ENVIAR hay que decir que aca no cambia nada, o el operador teme estar
    /// pisandose su propio padron y no aprieta.
    /// </summary>
    [Fact]
    public void Enviar_avisa_que_en_esta_caja_no_cambia_nada()
    {
        var (vm, _, _) = Armar();
        vm.SoloCajeros = true;
        vm.VerCompartirCommand.Execute(null);

        Assert.Contains("no cambia nada", vm.Explicacion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Las etiquetas del selector nombran la operacion, no la generica.</summary>
    [Fact]
    public void El_selector_se_llama_distinto_en_cada_operacion()
    {
        var (copiar, _, _) = Armar();
        Assert.Equal("Traer a esta caja", copiar.TextoModoRecibir);
        Assert.Equal("Compartir desde aquí", copiar.TextoModoCompartir);

        var (actualizar, _, _) = Armar();
        actualizar.SoloCajeros = true;
        Assert.Equal("Traer cajeros", actualizar.TextoModoRecibir);
        Assert.Equal("Enviar mis cajeros", actualizar.TextoModoCompartir);
    }

    [Fact]
    public void Las_dos_mitades_nunca_se_ven_a_la_vez()
    {
        var (vm, _, _) = Armar();

        vm.VerCompartirCommand.Execute(null);
        Assert.True(vm.ModoCompartir);
        Assert.False(vm.ModoRecibir);

        vm.VerRecibirCommand.Execute(null);
        Assert.True(vm.ModoRecibir);
        Assert.False(vm.ModoCompartir);
    }

    /// <summary>
    /// El campo de la direccion a mano arranca plegado: es la salida para cuando la
    /// difusion esta bloqueada, no el camino normal. Mostrarlo siempre convertia el
    /// paso 1 en dos formularios.
    /// </summary>
    [Fact]
    public void La_direccion_a_mano_arranca_plegada()
    {
        var (vm, _, _) = Armar();

        Assert.False(vm.DireccionAMano);

        vm.AlternarDireccionAManoCommand.Execute(null);
        Assert.True(vm.DireccionAMano);
    }

    /// <summary>
    /// Si el escaneo no encuentra nada, el campo a mano se abre SOLO: es
    /// exactamente lo que toca hacer ahora, y buscarlo plegado seria un paso de mas
    /// justo cuando algo ya salio distinto de lo esperado.
    /// </summary>
    [Fact]
    public async Task Si_no_aparece_ninguna_caja_se_abre_la_direccion_a_mano()
    {
        var (vm, _, _) = Armar();

        // No hay ninguna caja repartiendo en el agente de pruebas: la busqueda
        // termina vacia sin necesidad de simular nada.
        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.False(vm.HayCajasEncontradas);
        Assert.True(vm.DireccionAMano);
        Assert.True(vm.ReceptorEnFalla);
        Assert.Contains("direccion", vm.EstadoReceptor, StringComparison.OrdinalIgnoreCase);
    }
}
