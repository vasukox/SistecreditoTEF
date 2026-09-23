using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// Los tres ViewModels de la puerta de entrada: crear el PIN, ingresar y
/// administrar cajeros.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE IMPORTAN MAS QUE OTRAS PANTALLAS
/// ─────────────────────────────────────────────────────────────────────────────
/// Un abono se hace fuera de HioPos, abriendo el APK desde el icono: no hay nadie
/// que haya validado quien opera. Estas reglas son lo unico que separa "el cajero
/// Juan cobro $100.000" de "alguien cobro $100.000", y el nombre del cajero viaja a
/// Credinet en cada recaudo.
///
/// Hasta ahora no tenian ni una prueba.
/// </summary>
public class PuertaDeEntradaTests
{
    /// <summary>
    /// Doble de navegacion que solo anota a donde se fue. Basta: lo que hay que
    /// verificar es la DECISION de navegar, no la navegacion en si.
    /// </summary>
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

    private static (AuthService auth, InMemoryAuthStore store, NavSpy nav) Armar()
    {
        var store = new InMemoryAuthStore();
        return (new AuthService(store), store, new NavSpy());
    }

    // ==================================================================
    // Crear el PIN
    // ==================================================================

    [Fact]
    public async Task El_PIN_no_se_crea_si_los_dos_no_coinciden()
    {
        var (auth, store, nav) = Armar();
        var vm = new ConfigurarAdminViewModel(auth, nav)
        {
            Pin = "1234", PinConfirmacion = "4321"
        };

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Contains("no coinciden", vm.MotivoBloqueo, StringComparison.OrdinalIgnoreCase);

        await vm.GuardarCommand.ExecuteAsync(null);
        Assert.Null(await store.GetAdminPinHashAsync());
    }

    [Fact]
    public async Task Crear_el_PIN_lleva_al_alta_de_cajeros()
    {
        var (auth, store, nav) = Armar();
        var vm = new ConfigurarAdminViewModel(auth, nav)
        {
            Pin = "246810", PinConfirmacion = "246810"
        };

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(await store.GetAdminPinHashAsync());
        Assert.Contains("GoToAdminCajerosAsync", nav.Visitadas);
    }

    /// <summary>
    /// El PIN se guarda derivado, nunca en claro: un volcado de la BD del terminal
    /// no puede devolver el PIN de administrador de la tienda.
    /// </summary>
    [Fact]
    public async Task El_PIN_se_guarda_derivado_y_no_en_claro()
    {
        var (auth, store, nav) = Armar();
        var vm = new ConfigurarAdminViewModel(auth, nav)
        {
            Pin = "998877", PinConfirmacion = "998877"
        };

        await vm.GuardarCommand.ExecuteAsync(null);

        var guardado = await store.GetAdminPinHashAsync();
        Assert.NotNull(guardado);
        Assert.DoesNotContain("998877", guardado, StringComparison.Ordinal);
    }

    /// <summary>
    /// El atajo que rompe el circulo de la caja nueva: para copiar el PIN de otra
    /// caja no hace falta inventar uno antes.
    /// </summary>
    [Fact]
    public async Task Desde_el_primer_uso_se_puede_ir_a_copiar_de_otra_caja()
    {
        var (auth, _, nav) = Armar();
        var vm = new ConfigurarAdminViewModel(auth, nav);

        await vm.CopiarDeOtraCajaCommand.ExecuteAsync(null);

        Assert.Contains("GoToReplicacionAsync", nav.Visitadas);
    }

    // ==================================================================
    // Ingresar
    // ==================================================================

    [Fact]
    public async Task Con_usuario_y_clave_correctos_se_inicia_sesion_y_se_navega()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("123456");
        await auth.AgregarCajeroAsync("jperez", string.Empty, "clave123");

        var sesion = new SesionCajero();
        var vm = new IngresoCajeroViewModel(auth, sesion, nav)
        {
            Usuario = "jperez", Clave = "clave123"
        };

        await vm.IngresarCommand.ExecuteAsync(null);

        Assert.NotNull(sesion.Actual);
        Assert.Equal("jperez", sesion.Actual!.Usuario);
        Assert.Contains("GoToCreditosActivosAsync", nav.Visitadas);
        Assert.Null(vm.ErrorMessage);
    }

    /// <summary>
    /// El mensaje NO distingue "no existe el usuario" de "la clave esta mal": si lo
    /// hiciera, se podrian descubrir los usuarios validos del terminal probando
    /// nombres. Es la misma regla para las dos ramas y por eso se prueban juntas.
    /// </summary>
    [Theory]
    [InlineData("jperez", "otra-clave")]
    [InlineData("noexiste", "clave123")]
    public async Task Un_ingreso_fallido_no_revela_si_el_usuario_existe(string usuario, string clave)
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("123456");
        await auth.AgregarCajeroAsync("jperez", string.Empty, "clave123");

        var sesion = new SesionCajero();
        var vm = new IngresoCajeroViewModel(auth, sesion, nav) { Usuario = usuario, Clave = clave };

        await vm.IngresarCommand.ExecuteAsync(null);

        Assert.Equal("Usuario o clave incorrectos. Verifica e intenta de nuevo.", vm.ErrorMessage);
        Assert.Null(sesion.Actual);
        Assert.Empty(nav.Visitadas);
    }

    /// <summary>
    /// Que la clave no quede escrita en el campo tras un fallo: el siguiente que
    /// agarre el terminal no puede ver el intento anterior.
    /// </summary>
    [Fact]
    public async Task La_clave_se_borra_del_campo_despues_de_fallar()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("123456");
        await auth.AgregarCajeroAsync("jperez", string.Empty, "clave123");

        var vm = new IngresoCajeroViewModel(auth, new SesionCajero(), nav)
        {
            Usuario = "jperez", Clave = "equivocada"
        };

        await vm.IngresarCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.Clave);
    }

    [Fact]
    public async Task Un_cajero_desactivado_no_entra_y_se_le_dice_por_que()
    {
        var (auth, store, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("123456");
        var cajero = await auth.AgregarCajeroAsync("jperez", string.Empty, "clave123");
        await store.EstablecerActivoAsync(cajero!.Id, false);

        var sesion = new SesionCajero();
        var vm = new IngresoCajeroViewModel(auth, sesion, nav)
        {
            Usuario = "jperez", Clave = "clave123"
        };

        await vm.IngresarCommand.ExecuteAsync(null);

        Assert.Contains("desactivado", vm.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sesion.Actual);
    }

    [Fact]
    public void Sin_usuario_o_sin_clave_el_boton_dice_que_falta()
    {
        var (auth, _, nav) = Armar();
        var vm = new IngresoCajeroViewModel(auth, new SesionCajero(), nav);

        Assert.False(vm.IngresarCommand.CanExecute(null));
        Assert.Contains("usuario", vm.MotivoBloqueo, StringComparison.OrdinalIgnoreCase);

        vm.Usuario = "jperez";
        Assert.Contains("clave", vm.MotivoBloqueo, StringComparison.OrdinalIgnoreCase);

        vm.Clave = "clave123";
        Assert.True(vm.IngresarCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.MotivoBloqueo);
    }

    // ==================================================================
    // Administrar cajeros
    // ==================================================================

    [Fact]
    public async Task La_administracion_no_se_abre_con_el_PIN_equivocado()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("246810");

        var vm = new AdminCajerosViewModel(auth, nav) { Pin = "111111" };
        await vm.DesbloquearCommand.ExecuteAsync(null);

        Assert.False(vm.Desbloqueado);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Con_el_PIN_correcto_se_abre_la_administracion()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("246810");

        var vm = new AdminCajerosViewModel(auth, nav) { Pin = "246810" };
        await vm.DesbloquearCommand.ExecuteAsync(null);

        Assert.True(vm.Desbloqueado);
    }

    /// <summary>
    /// El alta pide DOS datos, no tres: se quito el nombre por pedido de operacion.
    /// El comprobante cae al usuario cuando no hay nombre.
    /// </summary>
    [Fact]
    public async Task El_alta_solo_necesita_usuario_y_clave()
    {
        var (auth, store, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("246810");

        var vm = new AdminCajerosViewModel(auth, nav) { Pin = "246810" };
        await vm.DesbloquearCommand.ExecuteAsync(null);

        vm.NuevoUsuario = "mlopez";
        vm.NuevaClave = "clave123";
        Assert.True(vm.AgregarCommand.CanExecute(null));

        await vm.AgregarCommand.ExecuteAsync(null);

        var cajeros = await store.GetCajerosAsync();
        var creado = Assert.Single(cajeros);
        Assert.Equal("mlopez", creado.Usuario);
        Assert.Equal("mlopez", creado.NombreVisible);
    }

    /// <summary>
    /// Mandar a elegir de una lista vacia es un callejon: el instalador queda sin
    /// camino hacia adelante.
    /// </summary>
    [Fact]
    public async Task No_se_puede_continuar_sin_al_menos_un_cajero_activo()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("246810");

        var vm = new AdminCajerosViewModel(auth, nav) { Pin = "246810" };
        await vm.DesbloquearCommand.ExecuteAsync(null);

        Assert.False(vm.SiguienteCommand.CanExecute(null));

        vm.NuevoUsuario = "mlopez";
        vm.NuevaClave = "clave123";
        await vm.AgregarCommand.ExecuteAsync(null);

        Assert.True(vm.SiguienteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Un_usuario_con_espacios_o_muy_corto_no_se_da_de_alta()
    {
        var (auth, _, nav) = Armar();
        await auth.ConfigurarPinAdminAsync("246810");

        var vm = new AdminCajerosViewModel(auth, nav) { Pin = "246810" };
        await vm.DesbloquearCommand.ExecuteAsync(null);

        vm.NuevaClave = "clave123";

        vm.NuevoUsuario = "m lopez";
        Assert.False(vm.AgregarCommand.CanExecute(null));

        vm.NuevoUsuario = "m";
        Assert.False(vm.AgregarCommand.CanExecute(null));
    }
}
