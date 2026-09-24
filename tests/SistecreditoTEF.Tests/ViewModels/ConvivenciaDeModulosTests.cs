using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// Los dos modulos, separados: el que entra por la POS es de la POS, y el que se
/// abre a mano desde el icono es de abonos. No se juntan nunca.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// DONDE SE JUNTABAN
/// ─────────────────────────────────────────────────────────────────────────────────
/// [SplashPage] es la RAIZ del Shell y los dos flujos se empujan encima de ella, asi
/// que es el unico lugar comun — y por lo tanto el unico donde se pueden mezclar por
/// accidente.
///
/// Se probo una version que distinguia "MainActivity todavia no navego" de "el cajero
/// volvio atras" para poder decidir en el segundo caso. Salio mal: esa distincion
/// depende de CUANDO el framework entrega el evento de aparicion de la pagina, y
/// cuando llega tarde la raiz decide EN PLENA VENTA y le borra el estado.
///
/// El sintoma en caja: el cajero tocaba "Volver a la POS" y aterrizaba en pagar
/// credito. La causa: sin operacion viva, el boton hace el "atras" normal, y ese pop
/// cae en la raiz, que resuelve destino y manda a abonos.
///
/// La regla que quedo es dura y no depende de ningun orden de eventos: mientras la
/// POS este al mando, la raiz no decide NADA. Y para que no decidir no signifique
/// pantalla negra, la raiz muestra una salida a la POS.
/// </summary>
public class ConvivenciaDeModulosTests
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
    }

    /// <summary>
    /// Doble de la salida a la POS. Anota si se la llamo y devuelve lo mismo que la
    /// real: true solo si habia una operacion viva que devolver.
    /// </summary>
    private sealed class SalidaSpy(ITransactionStateStore state) : IHioposExit
    {
        public int Llamadas { get; private set; }

        public bool Disponible => state.HioposTransactionActive;

        public bool Volver(string origen)
        {
            Llamadas++;
            if (!state.HioposTransactionActive) return false;
            state.Clear();
            return true;
        }
    }

    private sealed class Escenario
    {
        public InMemoryAuthStore Store { get; } = new();
        public SesionCajero Sesion { get; } = new();
        public StandaloneModeTracker Standalone { get; } = new();
        public TransactionStateStore Estado { get; } = new();
        public LaunchContext Arranque { get; } = new();
        public NavSpy Nav { get; } = new();
        public SalidaSpy Salida { get; }

        public Escenario() => Salida = new SalidaSpy(Estado);

        public SplashViewModel Raiz() => new(
            new AuthService(Store), Sesion, Standalone, Estado, Arranque, Salida, Nav);

        /// <summary>Deja la caja configurada y con un cajero dado de alta.</summary>
        public async Task<Cajero> DarDeAltaUnCajeroAsync()
        {
            var auth = new AuthService(Store);
            await auth.ConfigurarPinAdminAsync("246810");
            await auth.AgregarCajeroAsync("ana", "Ana", "clave1234");
            return (await Store.GetCajerosAsync())[0];
        }

        /// <summary>Una caja ya configurada, con un cajero con la sesion abierta.</summary>
        public async Task<Escenario> ConCajaListaAsync()
        {
            Sesion.Iniciar(await DarDeAltaUnCajeroAsync());
            return this;
        }

        /// <summary>La POS lanza el modulo para cobrar una factura.</summary>
        public void LaPosAbreUnaVenta()
        {
            Arranque.Action = HioposActions.Transaction;
            Estado.HioposTransactionActive = true;
            Standalone.Reset();
        }

        /// <summary>El cajero toca el icono del TEF.</summary>
        public void ElCajeroTocaElIcono() => Arranque.Action = HioposIntentGuard.LauncherAction;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // MIENTRAS LA POS ESTE AL MANDO, LA RAIZ NO TOCA NADA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El arranque de una venta. La raiz aparece ANTES de que MainActivity navegue:
    /// si decidiera ahi, el modulo pediria la clave del cajero en medio de una
    /// factura. Ya paso una vez.
    /// </summary>
    [Fact]
    public async Task Cuando_la_POS_abre_una_venta_la_raiz_no_navega()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.LaPosAbreUnaVenta();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Empty(e.Nav.Visitadas);
        Assert.False(e.Standalone.IsStandalone);
    }

    /// <summary>
    /// LA REGRESION QUE ESTO FIJA.
    ///
    /// Aunque la pantalla aparezca de nuevo —un reseteo del Shell, un "atras", el
    /// evento de aparicion entregado tarde—, con una operacion viva la raiz no puede
    /// tocar el estado. Borrarlo dejaba al boton "Volver a la POS" haciendo un pop
    /// que aterrizaba en pagar credito.
    /// </summary>
    [Fact]
    public async Task Con_una_operacion_viva_la_raiz_nunca_borra_el_estado_de_la_venta()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.LaPosAbreUnaVenta();
        e.Estado.SetActiveDocument(new SaleDocument());

        var raiz = e.Raiz();
        await raiz.DecidirYNavegarAsync();
        await raiz.DecidirYNavegarAsync();   // la pagina volvio a aparecer
        await raiz.DecidirYNavegarAsync();

        Assert.True(e.Estado.HioposTransactionActive);
        Assert.NotNull(e.Estado.ActiveDocument);
        Assert.False(e.Standalone.IsStandalone);
        Assert.Empty(e.Nav.Visitadas);
    }

    /// <summary>
    /// Ni siquiera si el intent que llego fue el del ICONO: lo que manda es que la
    /// POS tenga una operacion viva. Cruzar ahi a abonos es exactamente juntar los
    /// dos modulos.
    /// </summary>
    [Fact]
    public async Task El_icono_no_se_mete_en_una_operacion_viva_de_la_POS()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.LaPosAbreUnaVenta();
        e.ElCajeroTocaElIcono();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Empty(e.Nav.Visitadas);
        Assert.True(e.Estado.HioposTransactionActive);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // NO DECIDIR NO PUEDE SIGNIFICAR PANTALLA NEGRA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Si el cajero termina en la raiz con la POS al mando, la salida es VOLVER A LA
    /// POS — no cruzar a abonos.
    /// </summary>
    [Fact]
    public async Task La_salida_de_la_raiz_devuelve_el_control_a_la_POS()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.LaPosAbreUnaVenta();

        var raiz = e.Raiz();
        await raiz.DecidirYNavegarAsync();
        await raiz.VolverALaPosCommand.ExecuteAsync(null);

        Assert.Equal(1, e.Salida.Llamadas);
        Assert.Empty(e.Nav.Visitadas);   // no se cruzo a abonos
    }

    /// <summary>
    /// Y si ya no hay nada que devolverle a la POS, entonces la POS dejo de estar al
    /// mando: recien ahi se resuelve el destino normal.
    /// </summary>
    [Fact]
    public async Task Si_la_POS_ya_no_espera_nada_la_salida_resuelve_el_destino()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.LaPosAbreUnaVenta();

        var raiz = e.Raiz();
        await raiz.DecidirYNavegarAsync();

        // La venta se cerro por otro lado mientras el cajero miraba esta pantalla.
        e.Estado.Clear();

        await raiz.VolverALaPosCommand.ExecuteAsync(null);

        Assert.Contains("GoToCreditosActivosAsync", e.Nav.Visitadas);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL MODULO MANUAL, POR SU LADO
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task El_icono_sin_operacion_de_la_POS_abre_abonos()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.ElCajeroTocaElIcono();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Contains("GoToCreditosActivosAsync", e.Nav.Visitadas);
        Assert.True(e.Standalone.IsStandalone);
    }

    [Fact]
    public async Task Sin_PIN_de_administrador_el_icono_lleva_a_la_configuracion_inicial()
    {
        var e = new Escenario();
        e.ElCajeroTocaElIcono();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Contains("GoToConfigurarAdminAsync", e.Nav.Visitadas);
    }

    [Fact]
    public async Task Sin_cajero_identificado_el_icono_lleva_al_ingreso()
    {
        var e = new Escenario();
        await e.DarDeAltaUnCajeroAsync();   // hay cajeros, pero nadie ingreso

        e.ElCajeroTocaElIcono();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Contains("GoToIngresoCajeroAsync", e.Nav.Visitadas);
    }

    /// <summary>
    /// La raiz decide CADA vez que aparece. Antes decidia una sola vez en la vida de
    /// la app: al volver no volvia a decidir y la pantalla se quedaba vacia.
    /// </summary>
    [Fact]
    public async Task La_raiz_decide_cada_vez_que_aparece()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.ElCajeroTocaElIcono();

        var raiz = e.Raiz();
        await raiz.DecidirYNavegarAsync();
        await raiz.DecidirYNavegarAsync();

        Assert.Equal(2, e.Nav.Visitadas.Count(d => d == "GoToCreditosActivosAsync"));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL GUARD DEL INTENT
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La otra mitad de la separacion: con una venta viva, el intent del icono se
    /// descarta. Sin venta viva, pasa.
    /// </summary>
    [Fact]
    public void El_guard_separa_el_icono_de_una_venta_viva()
    {
        var conVenta = HioposIntentGuard.Evaluate(
            HioposIntentGuard.LauncherAction, hioposTransactionActive: true, isStandalone: false);
        Assert.True(conVenta.Discard);

        var sinVenta = HioposIntentGuard.Evaluate(
            HioposIntentGuard.LauncherAction, hioposTransactionActive: false, isStandalone: false);
        Assert.False(sinVenta.Discard);
    }
}
