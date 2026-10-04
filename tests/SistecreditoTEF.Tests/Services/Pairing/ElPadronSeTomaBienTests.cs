using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Pairing;

/// <summary>
/// QUE EL PADRON QUE LLEGA SE TOME BIEN, NO SOLO QUE LLEGUE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO QUE SE REPORTO
/// ─────────────────────────────────────────────────────────────────────────────
/// "Si se replican pero no los estamos tomando bien". Las pruebas que ya existian
/// cubren el TRANSPORTE —que el sobre viaje, se cifre y llegue entero— y ahi no
/// habia nada roto. El problema estaba despues: en como la caja receptora usa lo
/// que recibio.
///
/// Estas pruebas cubren ese tramo.
/// </summary>
public class ElPadronSeTomaBienTests
{
    private static ReplicatedCajero Cajero(
        string id, string usuario, bool activo = true, string hash = "v1$1$c2FsdA==$aGFzaA==") =>
        new(id, usuario, string.Empty, hash, activo, DateTime.UtcNow);

    // ══════════════════════════════════════════════════════════════════════════
    // EL DEFECTO QUE EXPLICA EL SINTOMA
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA PRUEBA QUE IMPORTA.
    ///
    /// El alta solo exige que el usuario sea unico entre los ACTIVOS, asi que dar
    /// de baja a "jperez" y volver a crearlo deja DOS filas con ese usuario: la
    /// vieja inactiva y la nueva activa. Es deliberado — los abonos que hizo el
    /// primero siguen teniendo a quien referirse.
    ///
    /// El ingreso elegia con un FirstOrDefault a secas sobre un orden de filas que
    /// nadie define. Si salia primero la INACTIVA, la respuesta era "cajero
    /// desactivado" y ahi se cortaba, sin llegar a mirar la activa. El cajero
    /// existe, la clave es correcta, y no puede entrar.
    ///
    /// Y se dispara justo al replicar: el padron se escribe borrando e
    /// insertando, asi que el orden de las filas cambia. La misma persona entraba
    /// en la caja de origen y dejaba de entrar en la que acababa de recibir el
    /// padron.
    /// </summary>
    [Fact]
    public async Task Un_cajero_recreado_entra_aunque_quede_primero_el_registro_viejo()
    {
        var store = new InMemoryAuthStore();
        var auth = new AuthService(store);
        await auth.ConfigurarPinAdminAsync("246810");

        // Primero la fila VIEJA e inactiva, como queda tras una replicacion.
        await store.GuardarCajeroAsync(new Cajero(
            Id: "viejo", Usuario: "jperez", Nombre: string.Empty,
            ClaveHash: await PasswordHasher.HashAsync("clave-vieja"),
            Activo: false, CreadoEn: DateTime.UtcNow.AddYears(-1)));

        await store.GuardarCajeroAsync(new Cajero(
            Id: "nuevo", Usuario: "jperez", Nombre: string.Empty,
            ClaveHash: await PasswordHasher.HashAsync("clave-nueva"),
            Activo: true, CreadoEn: DateTime.UtcNow));

        var resultado = await auth.IngresarAsync("jperez", "clave-nueva");

        var ok = Assert.IsType<ResultadoIngreso.Ok>(resultado);
        Assert.Equal("nuevo", ok.Cajero.Id);
    }

    /// <summary>
    /// Y si TODOS los que tienen ese usuario estan de baja, se sigue diciendo que
    /// esta desactivado. Preferir al activo no puede convertirse en "siempre hay
    /// alguien": el que fue dado de baja no entra.
    /// </summary>
    [Fact]
    public async Task Si_el_unico_con_ese_usuario_esta_de_baja_no_entra()
    {
        var store = new InMemoryAuthStore();
        var auth = new AuthService(store);
        await auth.ConfigurarPinAdminAsync("246810");

        await store.GuardarCajeroAsync(new Cajero(
            Id: "viejo", Usuario: "jperez", Nombre: string.Empty,
            ClaveHash: await PasswordHasher.HashAsync("clave"),
            Activo: false, CreadoEn: DateTime.UtcNow));

        Assert.IsType<ResultadoIngreso.NoHabilitado>(
            await auth.IngresarAsync("jperez", "clave"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LO QUE EL SOBRE NO PUEDE TRAER
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ids repetidos. La caja receptora guarda con InsertOrReplace POR ID, asi que
    /// el segundo pisa al primero: el operador acepta un resumen que dice dos
    /// cajeros y la caja termina con uno, sin un solo error en pantalla.
    /// </summary>
    [Fact]
    public void Un_sobre_con_ids_repetidos_se_rechaza_entero()
    {
        var sobre = new CashierRosterEnvelope(
            "v1$1$c2FsdA==$aGFzaA==",
            [Cajero("mismo-id", "jperez"), Cajero("mismo-id", "mgomez")]);

        Assert.Null(CashierRosterEnvelope.FromJson(sobre.ToJson()));
    }

    /// <summary>
    /// EL PADRON VIAJA AUNQUE TRAIGA UNA RAREZA.
    ///
    /// Hubo una version de esto que rechazaba el sobre ENTERO si traia dos
    /// cajeros activos con el mismo usuario. Se quito: que una caja tenga un dato
    /// raro no puede dejar a la tienda sin poder replicar. El operador veria "no
    /// se pudo interpretar lo que llego", sin ninguna pista, y se quedaria sin la
    /// unica herramienta que tiene para montar la caja nueva.
    ///
    /// Ademas cubria un estado que la aplicacion no sabe crear —el alta y la
    /// reactivacion ya rechazan usuarios repetidos entre activos—, asi que solo
    /// podia venir de datos viejos. Y esos hay que poder moverlos.
    ///
    /// La ambiguedad se resuelve donde corresponde: en el ingreso, de forma
    /// determinista. Ver [Con_dos_activos_del_mismo_usuario_siempre_entra_el_mismo].
    /// </summary>
    [Fact]
    public void Un_sobre_con_dos_activos_del_mismo_usuario_viaja_igual()
    {
        var sobre = new CashierRosterEnvelope(
            "v1$1$c2FsdA==$aGFzaA==",
            [Cajero("a", "jperez"), Cajero("b", "JPEREZ")]);

        var leido = CashierRosterEnvelope.FromJson(sobre.ToJson());

        Assert.NotNull(leido);
        Assert.Equal(2, leido.Cajeros.Count);
    }

    /// <summary>
    /// Y entonces el ingreso tiene que ser ESTABLE: la misma caja, con los mismos
    /// datos, deja entrar siempre a la misma persona. Un criterio estable y
    /// discutible es manejable; uno que cambia con el orden de las filas no se
    /// puede ni diagnosticar. Se elige el mas reciente.
    /// </summary>
    [Fact]
    public async Task Con_dos_activos_del_mismo_usuario_siempre_entra_el_mismo()
    {
        var store = new InMemoryAuthStore();
        var auth = new AuthService(store);
        await auth.ConfigurarPinAdminAsync("246810");

        await store.GuardarCajeroAsync(new Cajero(
            Id: "antiguo", Usuario: "jperez", Nombre: string.Empty,
            ClaveHash: await PasswordHasher.HashAsync("clave-antigua"),
            Activo: true, CreadoEn: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await store.GuardarCajeroAsync(new Cajero(
            Id: "reciente", Usuario: "jperez", Nombre: string.Empty,
            ClaveHash: await PasswordHasher.HashAsync("clave-reciente"),
            Activo: true, CreadoEn: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var ok = Assert.IsType<ResultadoIngreso.Ok>(
            await auth.IngresarAsync("jperez", "clave-reciente"));
        Assert.Equal("reciente", ok.Cajero.Id);
    }

    /// <summary>
    /// Pero el mismo usuario con uno de baja SI es valido, y tiene que seguir
    /// viajando: es el caso de alguien dado de baja y vuelto a crear. Rechazarlo
    /// dejaria sin poder replicar a las tiendas que han tenido rotacion.
    /// </summary>
    [Fact]
    public void El_mismo_usuario_con_uno_de_baja_es_valido_y_viaja()
    {
        var sobre = new CashierRosterEnvelope(
            "v1$1$c2FsdA==$aGFzaA==",
            [Cajero("viejo", "jperez", activo: false), Cajero("nuevo", "jperez")]);

        var leido = CashierRosterEnvelope.FromJson(sobre.ToJson());

        Assert.NotNull(leido);
        Assert.Equal(2, leido.Cajeros.Count);
        Assert.Equal(1, leido.CajerosActivos);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // QUE LO QUE VIAJA SOBREVIVA AL VIAJE
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El hash de la clave tiene que llegar BYTE A BYTE. Lleva dentro su propio
    /// salt y su numero de iteraciones ([PasswordHasher]), asi que si se alterara
    /// —un recorte, un cambio de mayusculas, una normalizacion— la clave dejaria
    /// de verificar en la caja receptora y nadie podria entrar: "se replican pero
    /// no los estamos tomando bien", exactamente.
    /// </summary>
    [Fact]
    public async Task La_clave_sobrevive_al_viaje_y_sigue_verificando()
    {
        var hash = await PasswordHasher.HashAsync("clave-del-cajero");

        var sobre = new CashierRosterEnvelope(
            await PasswordHasher.HashAsync("246810"),
            [Cajero("id-1", "jperez", hash: hash)]);

        var leido = CashierRosterEnvelope.FromJson(sobre.ToJson());

        Assert.NotNull(leido);
        var viajado = Assert.Single(leido.Cajeros);
        Assert.Equal(hash, viajado.ClaveHash, StringComparer.Ordinal);
        Assert.True(PasswordHasher.Verify("clave-del-cajero", viajado.ClaveHash));
        Assert.False(PasswordHasher.Verify("otra-clave", viajado.ClaveHash));
    }

    /// <summary>
    /// Y el cajero que llega puede ingresar de verdad en la caja receptora: el
    /// recorrido entero, de la exportacion al login, sin tocar la clave en claro
    /// —que no viaja ni existe en ningun lado—.
    /// </summary>
    [Fact]
    public async Task Un_cajero_replicado_puede_ingresar_en_la_caja_que_lo_recibe()
    {
        // Caja de origen: se da de alta a alguien y se exporta el padron.
        var origen = new InMemoryAuthStore();
        var authOrigen = new AuthService(origen);
        await authOrigen.ConfigurarPinAdminAsync("246810");
        await authOrigen.AgregarCajeroAsync("jperez", string.Empty, "clave123");

        var sobre = await origen.ExportarPadronAsync();
        Assert.NotNull(sobre);

        // Viaja como texto, igual que por el socket.
        var recibido = CashierRosterEnvelope.FromJson(sobre.ToJson());
        Assert.NotNull(recibido);

        // Caja receptora: se importa y se intenta ingresar.
        var destino = new InMemoryAuthStore();
        Assert.True(await destino.ImportarPadronAsync(recibido));

        var authDestino = new AuthService(destino);
        Assert.IsType<ResultadoIngreso.Ok>(
            await authDestino.IngresarAsync("jperez", "clave123"));

        // Y el PIN de administrador tambien llego.
        Assert.True(await authDestino.VerificarPinAdminAsync("246810"));
    }

    /// <summary>
    /// Los inactivos viajan igual. Si se quedaran fuera, el administrador de la
    /// caja nueva no podria reactivar a nadie —no estan— y tampoco recrearlos con
    /// el mismo usuario sin chocar con el historico de la otra caja.
    /// </summary>
    [Fact]
    public async Task Los_cajeros_de_baja_tambien_se_replican()
    {
        var origen = new InMemoryAuthStore();
        var auth = new AuthService(origen);
        await auth.ConfigurarPinAdminAsync("246810");

        var activo = await auth.AgregarCajeroAsync("mgomez", string.Empty, "clave123");
        var baja = await auth.AgregarCajeroAsync("jperez", string.Empty, "clave123");
        await auth.DesactivarCajeroAsync(baja!.Id);

        var sobre = await origen.ExportarPadronAsync();

        Assert.NotNull(sobre);
        Assert.Equal(2, sobre.Cajeros.Count);
        Assert.Equal(1, sobre.CajerosActivos);
        Assert.Contains(sobre.Cajeros, c => c.Id == activo!.Id && c.Activo);
        Assert.Contains(sobre.Cajeros, c => c.Id == baja.Id && !c.Activo);
    }

    /// <summary>
    /// EL PADRON COMPLETO, DE PUNTA A PUNTA, CON LAS CLAVES INTACTAS.
    ///
    /// Es la garantia que se pidio: al escribir el padron en la caja nueva tienen
    /// que estar TODOS —los que entran y los que estan de baja— cada uno con su
    /// clave. Que alguien este desactivado es otro asunto; viajar, viajan siempre.
    ///
    /// La prueba recorre el camino entero: alta de tres personas en una caja, baja
    /// de una, exportar, viajar como texto igual que por el socket, importar en
    /// otra caja, y comprobar que estan las tres, que las dos activas entran con
    /// SU clave, y que la de baja se puede reactivar y entrar con la suya.
    /// </summary>
    [Fact]
    public async Task El_padron_llega_completo_y_cada_uno_con_su_clave()
    {
        var origen = new InMemoryAuthStore();
        var authOrigen = new AuthService(origen);
        await authOrigen.ConfigurarPinAdminAsync("246810");

        await authOrigen.AgregarCajeroAsync("jperez", string.Empty, "clave-jperez");
        await authOrigen.AgregarCajeroAsync("mgomez", string.Empty, "clave-mgomez");
        var retirada = await authOrigen.AgregarCajeroAsync("alopez", string.Empty, "clave-alopez");
        await authOrigen.DesactivarCajeroAsync(retirada!.Id);

        var sobre = await origen.ExportarPadronAsync();
        Assert.NotNull(sobre);
        var viajado = CashierRosterEnvelope.FromJson(sobre.ToJson());
        Assert.NotNull(viajado);

        var destino = new InMemoryAuthStore();
        Assert.True(await destino.ImportarPadronAsync(viajado));
        var authDestino = new AuthService(destino);

        // Estan los TRES, no solo los que pueden entrar.
        Assert.Equal(3, (await destino.GetCajerosAsync()).Count);

        // Y cada uno con SU clave, no con la del de al lado.
        Assert.IsType<ResultadoIngreso.Ok>(
            await authDestino.IngresarAsync("jperez", "clave-jperez"));
        Assert.IsType<ResultadoIngreso.Ok>(
            await authDestino.IngresarAsync("mgomez", "clave-mgomez"));
        Assert.IsType<ResultadoIngreso.ClaveIncorrecta>(
            await authDestino.IngresarAsync("jperez", "clave-mgomez"));

        // La de baja llego como lo que es: de baja, pero entera.
        Assert.IsType<ResultadoIngreso.NoHabilitado>(
            await authDestino.IngresarAsync("alopez", "clave-alopez"));

        // Y por eso se la puede reactivar en la caja nueva, con su clave de siempre.
        Assert.True(await authDestino.ReactivarCajeroAsync(retirada.Id));
        Assert.IsType<ResultadoIngreso.Ok>(
            await authDestino.IngresarAsync("alopez", "clave-alopez"));

        // El PIN de administrador tambien viajo.
        Assert.True(await authDestino.VerificarPinAdminAsync("246810"));
    }
}
