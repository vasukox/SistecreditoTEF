using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// Los dos modulos conviviendo: la venta que abre HioPos y los abonos que abre el
/// icono.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LA PANTALLA NEGRA QUE REPORTO LA TIENDA
/// ─────────────────────────────────────────────────────────────────────────────────
/// "Estoy facturando con Sistecredito en HioPos, selecciono el medio de pago y me
/// lleva bien a consultar cliente. Pero si el cajero se sale y deja el modulo ahi, y
/// despues selecciona el TEF a mano para entrar al otro modulo —ingresar y pagar
/// credito—, no se puede: aparece una pantalla negra."
///
/// Eran dos defectos encadenados, y los dos se reproducen aca sin terminal:
///
///   1. [SplashPage] es la RAIZ del Shell y el flujo de HioPos se empuja encima, asi
///      que el "atras" desde consultar cliente cae en ella. Esa pantalla no tiene
///      contenido —fondo #333333 y un aviso oculto mientras no hay error—, y ademas
///      decidia UNA sola vez: al volver no volvia a decidir. Pantalla negra sin
///      salida.
///
///   2. <c>HioposTransactionActive</c> solo se apaga al entregarle el resultado al
///      POS. Un cajero que se sale con "atras" nunca pasa por ahi, pero el proceso
///      —y con el los singletons— sigue vivo. La bandera quedaba pegada en true, y
///      entonces [HioposIntentGuard] descartaba el intent del icono y la raiz
///      tampoco decidia. El modulo de abonos se volvia inalcanzable hasta matar la
///      app.
///
/// El orden de las pruebas sigue el recorrido del cajero.
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
    }

    private sealed class Escenario
    {
        public InMemoryAuthStore Store { get; } = new();
        public SesionCajero Sesion { get; } = new();
        public StandaloneModeTracker Standalone { get; } = new();
        public TransactionStateStore Estado { get; } = new();
        public LaunchContext Arranque { get; } = new();
        public NavSpy Nav { get; } = new();

        public SplashViewModel Raiz() => new(
            new AuthService(Store), Sesion, Standalone, Estado, Arranque, Nav);

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

        /// <summary>HioPos lanza el modulo para cobrar una factura.</summary>
        public void HioposAbreUnaVenta()
        {
            Arranque.Action = HioposActions.Transaction;
            Estado.HioposTransactionActive = true;
            Standalone.Reset();
        }

        /// <summary>MainActivity ya navego a consultar cliente.</summary>
        public void MainActivityYaNavego() => Arranque.NavegacionConsumida = true;

        /// <summary>El cajero toca el icono del TEF.</summary>
        public void ElCajeroTocaElIcono() => Arranque.Action = HioposIntentGuard.LauncherAction;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL ARRANQUE DE UNA VENTA NO SE TOCA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La raiz aparece ANTES de que MainActivity navegue. Si decidiera ahi, el modulo
    /// pediria la clave del cajero en medio de una factura: eso ya paso una vez y es
    /// lo que esta rama protege.
    /// </summary>
    [Fact]
    public async Task Cuando_HioPos_abre_una_venta_la_raiz_no_navega()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.HioposAbreUnaVenta();   // MainActivity todavia no navego

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Empty(e.Nav.Visitadas);
        Assert.False(e.Standalone.IsStandalone);
        Assert.True(e.Estado.HioposTransactionActive);
    }

    /// <summary>
    /// Relanzamiento CALIENTE: HioPos manda otra venta mientras el modulo esta
    /// abierto, y MainActivity resetea el Shell a la raiz antes de empujar la
    /// pantalla nueva. Ese reseteo hace aparecer esta pantalla otra vez.
    ///
    /// Si la raiz decidiera ahi, le pisaria la navegacion a la venta NUEVA. El intent
    /// nuevo apaga [NavegacionConsumida] justamente para eso.
    /// </summary>
    [Fact]
    public async Task Un_relanzamiento_caliente_de_HioPos_no_deja_que_la_raiz_decida()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.HioposAbreUnaVenta();
        e.MainActivityYaNavego();

        // Llega la venta siguiente: al asignar la accion, la bandera se apaga sola.
        e.HioposAbreUnaVenta();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Empty(e.Nav.Visitadas);
        Assert.True(e.Estado.HioposTransactionActive);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL CAJERO SE SALE: ACA ESTABA LA PANTALLA NEGRA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El "atras" desde consultar cliente cae en la raiz. Antes esta pantalla se
    /// quedaba callada —ya habia decidido una vez— y el cajero se quedaba mirando el
    /// fondo #333333 sin un solo control.
    /// </summary>
    [Fact]
    public async Task Volver_atras_desde_la_venta_lleva_a_los_abonos_y_no_a_una_pantalla_negra()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.HioposAbreUnaVenta();
        e.MainActivityYaNavego();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Contains("GoToCreditosActivosAsync", e.Nav.Visitadas);
    }

    /// <summary>
    /// Salirse de la venta la ABANDONA, y abandonarla tiene que limpiar su estado.
    ///
    /// No es prolijidad: [ReciboPagoViewModel] elige como cerrar segun el modo —al POS
    /// o con FinishAffinity—. Con la venta mal soltada, un ABONO se le devolveria a
    /// HioPos como si fuera el cobro de la factura.
    /// </summary>
    [Fact]
    public async Task Salirse_de_la_venta_suelta_el_estado_y_pasa_a_modo_abonos()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.HioposAbreUnaVenta();
        e.Estado.SetActiveDocument(new SaleDocument());
        e.MainActivityYaNavego();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.False(e.Estado.HioposTransactionActive);
        Assert.Null(e.Estado.ActiveDocument);
        Assert.True(e.Standalone.IsStandalone);

        // El POS no espera un setResult de un abono hecho despues de abandonar.
        Assert.False(e.Standalone.IsRefundFromHioPos);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL ICONO DEL TEF DESPUES DE UNA VENTA ABANDONADA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EL SINTOMA REPORTADO, EN UNA LINEA.
    ///
    /// La bandera de venta viva quedaba pegada en true porque nadie le respondio al
    /// POS. Con ella encendida, la raiz se iba por la rama "no decido nada" aunque la
    /// app la hubiera abierto el ICONO —o sea, ya sin ninguna relacion con HioPos— y
    /// el cajero volvia a quedarse en la pantalla negra.
    /// </summary>
    [Fact]
    public async Task Una_venta_abandonada_no_bloquea_el_arranque_desde_el_icono()
    {
        var e = await new Escenario().ConCajaListaAsync();
        e.HioposAbreUnaVenta();

        // El cajero se salio; nadie le respondio al POS, asi que la bandera sigue
        // encendida. Despues toca el icono del TEF.
        e.ElCajeroTocaElIcono();

        await e.Raiz().DecidirYNavegarAsync();

        Assert.Contains("GoToCreditosActivosAsync", e.Nav.Visitadas);
        Assert.False(e.Estado.HioposTransactionActive);
        Assert.True(e.Standalone.IsStandalone);
    }

    /// <summary>
    /// El guard es la otra mitad del bloqueo: con la bandera pegada descartaba el
    /// intent del icono. Apagada, el icono abre los abonos como corresponde.
    /// </summary>
    [Fact]
    public void Con_la_venta_ya_soltada_el_guard_deja_pasar_el_icono()
    {
        var bloqueado = HioposIntentGuard.Evaluate(
            HioposIntentGuard.LauncherAction, hioposTransactionActive: true, isStandalone: false);
        Assert.True(bloqueado.Discard);

        var libre = HioposIntentGuard.Evaluate(
            HioposIntentGuard.LauncherAction, hioposTransactionActive: false, isStandalone: false);
        Assert.False(libre.Discard);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // EL ICONO EN UNA CAJA SIN CONFIGURAR
    // ══════════════════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════════════════
    // REENTRANCIA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La raiz puede aparecer varias veces —el cajero entra y sale del flujo— y tiene
    /// que decidir CADA vez. Lo que no puede es decidir dos veces a la vez.
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
}
