using SistecreditoTEF.Maui.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Auth;

/// <summary>
/// Ingreso de cajeros para el flujo de abonos.
///
/// Los abonos se hacen abriendo el APK desde el ícono, fuera de HioPos, así que no
/// hay nadie que haya validado quién está operando. Estas reglas son lo único que
/// separa "el cajero Juan cobró $100.000" de "alguien cobró $100.000".
///
/// ─────────────────────────────────────────────────────────────────────────────
/// NO HAY LÍMITE DE INTENTOS
/// ─────────────────────────────────────────────────────────────────────────────
/// Había un bloqueo de 2 minutos a los 5 fallos. Se quitó por pedido explícito: un
/// cajero que olvida la clave frenaba la caja, y en atención al público esperar dos
/// minutos es peor que el riesgo que el bloqueo evitaba. El costo de PBKDF2
/// (210.000 iteraciones por intento) sigue siendo un freno natural.
/// </summary>
public class AuthServiceTests
{
    // El doble en memoria se movio a [InMemoryAuthStore]: lo comparten estas
    // pruebas y las de replicacion entre cajas, que necesitan el mismo
    // almacenamiento con exportar/importar.
    private static AuthService Build(out InMemoryAuthStore store)
    {
        store = new InMemoryAuthStore();
        return new AuthService(store);
    }

    // ==================================================================
    // Configuración inicial
    // ==================================================================

    [Fact]
    public async Task Recien_instalada_pide_configuracion_inicial()
    {
        var auth = Build(out _);

        Assert.True(await auth.RequiereConfiguracionInicialAsync());
    }

    [Fact]
    public async Task Con_el_PIN_configurado_ya_no_pide_configuracion_inicial()
    {
        var auth = Build(out _);

        Assert.True(await auth.ConfigurarPinAdminAsync("2468"));
        Assert.False(await auth.RequiereConfiguracionInicialAsync());
    }

    [Fact]
    public async Task El_PIN_no_se_puede_reemplazar_desde_la_configuracion_inicial()
    {
        // Si se pudiera, cualquiera que llegue a esa pantalla se queda con el
        // terminal. Para cambiarlo hay que saber el anterior.
        var auth = Build(out _);
        await auth.ConfigurarPinAdminAsync("2468");

        Assert.False(await auth.ConfigurarPinAdminAsync("9999"));
        Assert.True(await auth.VerificarPinAdminAsync("2468"));
    }

    [Fact]
    public async Task Cambiar_el_PIN_exige_el_anterior()
    {
        var auth = Build(out _);
        await auth.ConfigurarPinAdminAsync("2468");

        Assert.False(await auth.CambiarPinAdminAsync("0000", "1357"));
        Assert.True(await auth.CambiarPinAdminAsync("2468", "1357"));
        Assert.True(await auth.VerificarPinAdminAsync("1357"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("123")]
    [InlineData("   ")]
    public async Task Un_PIN_demasiado_corto_se_rechaza(string pin)
    {
        var auth = Build(out _);

        Assert.False(await auth.ConfigurarPinAdminAsync(pin));
        Assert.True(await auth.RequiereConfiguracionInicialAsync());
    }

    // ==================================================================
    // Alta de cajeros
    // ==================================================================

    [Fact]
    public async Task Se_da_de_alta_un_cajero_y_puede_ingresar()
    {
        var auth = Build(out _);

        var cajero = await auth.AgregarCajeroAsync("jperez", "Juan Perez", "1234");

        Assert.NotNull(cajero);
        var ok = Assert.IsType<ResultadoIngreso.Ok>(
            await auth.IngresarAsync("jperez", "1234"));
        Assert.Equal("Juan Perez", ok.Cajero.Nombre);
    }

    [Fact]
    public async Task No_se_permiten_dos_usuarios_activos_iguales()
    {
        // El usuario es el identificador de ingreso: dos iguales harían el login
        // ambiguo y no se sabría cuál de los dos cobró.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("jperez", "Juan Perez", "1234");

        Assert.Null(await auth.AgregarCajeroAsync("JPEREZ", "Otro Juan", "5678"));
    }

    [Fact]
    public async Task El_nombre_SI_se_puede_repetir()
    {
        // Dos "Juan Perez" distintos existen en la vida real; lo que no puede
        // repetirse es el usuario.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("jperez", "Juan Perez", "1234");

        Assert.NotNull(await auth.AgregarCajeroAsync("jperez2", "Juan Perez", "5678"));
    }

    [Theory]
    [InlineData("ab")]        // muy corto
    [InlineData("j perez")]   // con espacio: un espacio invisible es indiagnosticable
    [InlineData("")]
    public async Task Un_usuario_invalido_se_rechaza(string usuario)
    {
        var auth = Build(out _);

        Assert.Null(await auth.AgregarCajeroAsync(usuario, "Juan", "1234"));
    }

    [Fact]
    public async Task Una_clave_corta_se_rechaza()
    {
        var auth = Build(out _);

        Assert.Null(await auth.AgregarCajeroAsync("jperez", "Juan", "12"));
    }

    [Fact]
    public async Task Sin_nombre_el_comprobante_usa_el_usuario()
    {
        // Preferible a un comprobante con el cajero en blanco.
        var auth = Build(out _);

        var cajero = await auth.AgregarCajeroAsync("jperez", "", "1234");

        Assert.NotNull(cajero);
        Assert.Equal("jperez", cajero!.NombreVisible);
    }

    // ==================================================================
    // Baja y reactivación
    // ==================================================================

    [Fact]
    public async Task Un_cajero_desactivado_no_puede_ingresar()
    {
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        await auth.DesactivarCajeroAsync(cajero!.Id);

        Assert.IsType<ResultadoIngreso.NoHabilitado>(
            await auth.IngresarAsync("juan", "1234"));
    }

    [Fact]
    public async Task Un_cajero_desactivado_sigue_existiendo()
    {
        // No se borra: los abonos que hizo lo referencian por nombre, y perder quién
        // cobró deja un hueco en la trazabilidad.
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "1234");
        await auth.DesactivarCajeroAsync(cajero!.Id);

        Assert.Empty(await auth.ObtenerCajerosActivosAsync());
        Assert.Single(await auth.ObtenerTodosLosCajerosAsync());
    }

    [Fact]
    public async Task Un_cajero_desactivado_se_puede_volver_a_activar()
    {
        // Antes la baja era IRREVERSIBLE: el cajero no podía ingresar y tampoco
        // recrearse, porque el alta rechaza usuarios repetidos. La única salida era
        // borrar los datos de la app y perder la lista entera más el historial.
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "1234");
        await auth.DesactivarCajeroAsync(cajero!.Id);

        Assert.True(await auth.ReactivarCajeroAsync(cajero.Id));

        Assert.Single(await auth.ObtenerCajerosActivosAsync());
        Assert.IsType<ResultadoIngreso.Ok>(await auth.IngresarAsync("juan", "1234"));
    }

    [Fact]
    public async Task Reactivar_conserva_la_clave_original()
    {
        // Reactivar no es recrear: vuelve con su misma clave, no en blanco ni con
        // una nueva que alguien tenga que comunicarle.
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "MiClave99");
        await auth.DesactivarCajeroAsync(cajero!.Id);
        await auth.ReactivarCajeroAsync(cajero.Id);

        Assert.IsType<ResultadoIngreso.Ok>(await auth.IngresarAsync("juan", "MiClave99"));
    }

    [Fact]
    public async Task No_se_puede_reactivar_si_el_usuario_ya_lo_tiene_otro_activo()
    {
        var auth = Build(out _);
        var viejo = await auth.AgregarCajeroAsync("juan", "Juan", "1234");
        await auth.DesactivarCajeroAsync(viejo!.Id);

        // Con el primero de baja el usuario queda libre y se puede reusar.
        Assert.NotNull(await auth.AgregarCajeroAsync("juan", "Juan Nuevo", "5678"));

        Assert.False(await auth.ReactivarCajeroAsync(viejo.Id));
        Assert.Single(await auth.ObtenerCajerosActivosAsync());
    }

    [Fact]
    public async Task Reactivar_uno_ya_activo_no_falla()
    {
        // Idempotente: un doble toque no puede convertirse en un error visible.
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        Assert.True(await auth.ReactivarCajeroAsync(cajero!.Id));
        Assert.Single(await auth.ObtenerCajerosActivosAsync());
    }

    [Fact]
    public async Task Reactivar_uno_inexistente_devuelve_false_sin_lanzar()
    {
        var auth = Build(out _);

        Assert.False(await auth.ReactivarCajeroAsync("no-existe"));
    }

    // ==================================================================
    // Ingreso: sin límite de intentos
    // ==================================================================

    [Fact]
    public async Task Con_la_clave_incorrecta_no_ingresa()
    {
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("juan", "0000"));
    }

    [Fact]
    public async Task Se_puede_reintentar_indefinidamente_sin_quedar_bloqueado()
    {
        // Es la regla que reemplazó al bloqueo: un cajero que olvidó la clave no
        // puede quedar fuera de la caja esperando dos minutos.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        for (var i = 0; i < 30; i++)
        {
            var r = await auth.IngresarAsync("juan", "clave-mala");
            Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(r);
        }

        // Y la clave correcta sigue entrando después de 30 fallos.
        Assert.IsType<ResultadoIngreso.Ok>(await auth.IngresarAsync("juan", "1234"));
    }

    [Fact]
    public async Task Los_intentos_restantes_se_informan_como_ilimitados()
    {
        // -1 y no 0: cero se leería como "no te quedan intentos", justo lo
        // contrario. La pantalla lo interpreta como ilimitados y no muestra cuenta.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        var r = await auth.IngresarAsync("juan", "0000");

        var mal = Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(r);
        Assert.Equal(AuthService.IntentosRestantesIlimitados, mal.IntentosRestantes);
    }

    // ==================================================================
    // Ingreso: reglas del usuario
    // ==================================================================

    [Fact]
    public async Task Un_usuario_inexistente_responde_igual_que_una_clave_incorrecta()
    {
        // Si dieran mensajes distintos, se podrían descubrir los usuarios válidos
        // del terminal probando nombres.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("juan", "Juan", "1234");

        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("noexiste", "1234"));
        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("juan", "0000"));
    }

    [Fact]
    public async Task El_usuario_no_distingue_mayusculas_ni_espacios_alrededor()
    {
        // Un cajero que escribe "JPerez" a las 7 de la mañana tiene que entrar.
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("jperez", "Juan Perez", "1234");

        Assert.IsType<ResultadoIngreso.Ok>(await auth.IngresarAsync("JPEREZ", "1234"));
        Assert.IsType<ResultadoIngreso.Ok>(await auth.IngresarAsync("  jPeReZ  ", "1234"));
    }

    [Fact]
    public async Task La_clave_SI_distingue_mayusculas()
    {
        var auth = Build(out _);
        await auth.AgregarCajeroAsync("jperez", "Juan Perez", "Clave1234");

        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("jperez", "clave1234"));
    }

    [Fact]
    public async Task Un_usuario_vacio_no_ingresa()
    {
        var auth = Build(out _);

        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("", "1234"));
    }

    // ==================================================================
    // La clave nunca queda recuperable
    // ==================================================================

    [Fact]
    public async Task La_clave_no_se_guarda_en_claro()
    {
        var auth = Build(out var store);
        await auth.AgregarCajeroAsync("juan", "Juan", "MiClaveSecreta");

        var guardado = Assert.Single(await store.GetCajerosAsync());

        Assert.DoesNotContain("MiClaveSecreta", guardado.ClaveHash);
    }

    [Fact]
    public async Task El_PIN_de_administrador_no_se_guarda_en_claro()
    {
        var auth = Build(out var store);
        await auth.ConfigurarPinAdminAsync("2468");

        var hash = await store.GetAdminPinHashAsync();

        Assert.NotNull(hash);
        Assert.DoesNotContain("2468", hash!);
    }

    [Fact]
    public async Task Cambiar_la_clave_invalida_la_anterior()
    {
        var auth = Build(out _);
        var cajero = await auth.AgregarCajeroAsync("juan", "Juan", "vieja1234");

        Assert.True(await auth.CambiarClaveCajeroAsync(cajero!.Id, "nueva5678"));

        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await auth.IngresarAsync("juan", "vieja1234"));
        Assert.IsType<ResultadoIngreso.Ok>(
            await auth.IngresarAsync("juan", "nueva5678"));
    }
}
