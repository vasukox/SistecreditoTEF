using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Pairing;

/// <summary>
/// Actualizar los cajeros de una caja que ya opera, copiandolos de otra.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// ES OTRA OPERACION, NO LA MISMA CON UN PARAMETRO
/// ─────────────────────────────────────────────────────────────────────────────────
/// Copiar la configuracion es para MONTAR una caja: escribe el PIN de administrador
/// porque no habia ninguno. Actualizar es para una caja que ya trabaja, donde el
/// unico motivo para copiar de otra es que dieron de alta o de baja a un cajero.
///
/// La caja que REPARTE no distingue las dos: manda el mismo sobre de siempre. Eso es
/// deliberado — las cajas ya instaladas siguen sirviendo de emisoras sin
/// actualizarlas—. Todo lo que cambia pasa del lado que recibe.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// EL PELIGRO QUE NO SE VE
/// ─────────────────────────────────────────────────────────────────────────────────
/// El padron se escribe COMPLETO. Entonces, si la caja de la que se copia tiene el
/// padron viejo, "actualizar" BORRA cajeros sin decir nada: el operador creia estar
/// sumando y estaba restando. Por eso la mitad de estas pruebas son sobre el
/// diferencial.
/// </summary>
public class ActualizacionDeCajerosTests
{
    private static Cajero Cajero(
        string usuario, bool activo = true, string hash = "hash-1", string? id = null) =>
        new(id ?? $"id-{usuario}", usuario, usuario, hash, activo, new DateTime(2026, 1, 1));

    private static ReplicatedCajero Entrante(
        string usuario, bool activo = true, string hash = "hash-1", string? id = null) =>
        new(id ?? $"id-{usuario}", usuario, usuario, hash, activo, new DateTime(2026, 1, 1));

    // ══════════════════════════════════════════════════════════════════════════════
    // EL DIFERENCIAL
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Padrones_iguales_no_muestran_ningun_cambio()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana"), Cajero("beto")],
            [Entrante("ana"), Entrante("beto")]);

        Assert.True(diferencia.SinCambios);
        Assert.False(diferencia.HayBajas);
        Assert.Contains("No hay cambios", diferencia.Resumen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// LA PRUEBA QUE JUSTIFICA TODA ESTA PANTALLA.
    ///
    /// La caja de origen tiene el padron viejo: no conoce a dos de los cajeros de
    /// esta caja. Aplicar eso los borra. "7 cajeros" no lo deja ver; "se quitan 2"
    /// si.
    /// </summary>
    [Fact]
    public void Un_padron_viejo_se_delata_como_bajas()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana"), Cajero("beto"), Cajero("carla")],
            [Entrante("ana")]);

        Assert.True(diferencia.HayBajas);
        Assert.Equal(["beto", "carla"], diferencia.Quitados);
        Assert.Contains("se quitan 2", diferencia.Resumen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Las_bajas_se_nombran_para_que_el_operador_las_reconozca()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("jperez"), Cajero("mgomez")],
            [Entrante("otro")]);

        Assert.Equal("jperez, mgomez", diferencia.DetalleDeBajas);
    }

    [Fact]
    public void Se_distingue_agregar_de_reactivar_y_de_desactivar()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana", activo: true), Cajero("beto", activo: false)],
            [Entrante("ana", activo: false), Entrante("beto", activo: true), Entrante("nuevo")]);

        Assert.Equal(["nuevo"], diferencia.Agregados);
        Assert.Equal(["beto"], diferencia.Reactivados);
        Assert.Equal(["ana"], diferencia.Desactivados);
        Assert.False(diferencia.HayBajas);
    }

    [Fact]
    public void Una_clave_distinta_se_cuenta_como_cambio()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana", hash: "viejo")],
            [Entrante("ana", hash: "nuevo")]);

        Assert.Equal(["ana"], diferencia.ClavesCambiadas);
        Assert.False(diferencia.HayBajas);
    }

    /// <summary>
    /// Se compara por USUARIO, no por Id. Dos cajas configuradas a mano tienen Ids
    /// distintos para el mismo cajero, y comparar por Id mostraria "se agregan 2 · se
    /// quitan 2" en una actualizacion donde no cambia nadie.
    /// </summary>
    [Fact]
    public void Ids_distintos_para_el_mismo_usuario_no_son_un_cambio()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana", id: "id-de-la-caja-1")],
            [Entrante("ana", id: "id-de-la-caja-2")]);

        Assert.True(diferencia.SinCambios);
    }

    /// <summary>El login no distingue mayusculas; el diferencial tampoco puede.</summary>
    [Fact]
    public void El_usuario_se_compara_sin_distinguir_mayusculas()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("JPerez")],
            [Entrante("jperez")]);

        Assert.True(diferencia.SinCambios);
    }

    [Fact]
    public void El_resumen_nombra_en_singular_cuando_es_uno_solo()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana")],
            [Entrante("ana"), Entrante("beto")]);

        Assert.Equal("Se agrega 1", diferencia.Resumen);
    }

    [Fact]
    public void El_resumen_encadena_los_cambios_que_si_ocurren()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [Cajero("ana"), Cajero("beto"), Cajero("carla"), Cajero("dani")],
            [Entrante("ana", activo: false), Entrante("nuevo1"), Entrante("nuevo2")]);

        // Se agregan 2 · se desactiva 1 · se quitan 3
        Assert.Contains("se agregan 2", diferencia.Resumen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("se desactiva 1", diferencia.Resumen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("se quitan 3", diferencia.Resumen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orden estable. Sin esto el listado sale en el orden del diccionario, que
    /// cambia entre corridas: el operador que actualiza tres cajas veria la misma
    /// lista en tres ordenes distintos y desconfiaria con razon.
    /// </summary>
    [Fact]
    public void Las_listas_salen_ordenadas()
    {
        var diferencia = DiferenciaDePadron.Entre(
            [],
            [Entrante("zulma"), Entrante("ana"), Entrante("marta")]);

        Assert.Equal(["ana", "marta", "zulma"], diferencia.Agregados);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // LA ESCRITURA
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LO QUE SEPARA ESTA OPERACION DE LA OTRA: el PIN de administrador no se toca.
    ///
    /// Si se copiara en cada actualizacion, un PIN cambiado por error en UNA caja se
    /// reparte a toda la tienda, y el que lo cambio bien queda afuera de las demas.
    /// </summary>
    [Fact]
    public async Task Actualizar_cajeros_no_toca_el_PIN_de_administrador()
    {
        var store = new InMemoryAuthStore();
        await store.SetAdminPinHashAsync("pin-de-esta-caja");

        var sobre = new CashierRosterEnvelope("pin-de-la-otra-caja", [Entrante("ana")]);
        await store.ActualizarCajerosAsync(sobre, incluirPinAdmin: false);

        Assert.Equal("pin-de-esta-caja", await store.GetAdminPinHashAsync());
        Assert.Single(await store.GetCajerosAsync());
    }

    /// <summary>Y cuando el operador lo pide explicitamente, si lo trae.</summary>
    [Fact]
    public async Task Con_la_casilla_marcada_el_PIN_tambien_se_actualiza()
    {
        var store = new InMemoryAuthStore();
        await store.SetAdminPinHashAsync("pin-de-esta-caja");

        var sobre = new CashierRosterEnvelope("pin-de-la-otra-caja", [Entrante("ana")]);
        await store.ActualizarCajerosAsync(sobre, incluirPinAdmin: true);

        Assert.Equal("pin-de-la-otra-caja", await store.GetAdminPinHashAsync());
    }

    /// <summary>
    /// El padron se reemplaza completo, igual que al importar. Es lo que hace
    /// posible la baja silenciosa, y por eso el diferencial va antes.
    /// </summary>
    [Fact]
    public async Task El_padron_se_reemplaza_completo()
    {
        var store = new InMemoryAuthStore();
        await store.SetAdminPinHashAsync("pin");
        await store.GuardarCajeroAsync(Cajero("viejo"));

        var sobre = new CashierRosterEnvelope("pin", [Entrante("ana"), Entrante("beto")]);
        await store.ActualizarCajerosAsync(sobre, incluirPinAdmin: false);

        var quedaron = await store.GetCajerosAsync();
        Assert.Equal(2, quedaron.Count);
        Assert.DoesNotContain(quedaron, c => c.Usuario == "viejo");
    }

    /// <summary>
    /// La caja que reparte no cambia: el sobre sigue siendo el de siempre, con el PIN
    /// adentro. Lo que decide si ese PIN se escribe es el lado que recibe.
    ///
    /// Esto es lo que permite que las cajas ya instaladas sigan sirviendo de emisoras
    /// sin actualizarles el APK.
    /// </summary>
    [Fact]
    public async Task El_sobre_del_emisor_es_el_mismo_de_siempre()
    {
        var emisor = new InMemoryAuthStore();
        await emisor.SetAdminPinHashAsync("pin-del-emisor");
        await emisor.GuardarCajeroAsync(Cajero("ana"));

        var sobre = await emisor.ExportarPadronAsync();

        Assert.NotNull(sobre);
        Assert.Equal("pin-del-emisor", sobre!.AdminPinHash);
        Assert.Single(sobre.Cajeros);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // COMPATIBILIDAD CON LAS CAJAS QUE YA ESTAN INSTALADAS
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA PRUEBA QUE PROTEGE A LAS TIENDAS QUE YA ESTAN EN PRODUCCION.
    ///
    /// El StoreId se agrego al SALUDO para poder rechazar un padron de otra tienda.
    /// Las cajas con el APK anterior no lo mandan, y tienen que seguir sirviendo de
    /// emisoras sin actualizarlas: si su saludo dejara de entenderse, las tres cajas
    /// de cada tienda ya desplegada quedarian sin poder compartir nada.
    ///
    /// Este es el JSON exacto que manda una caja vieja.
    /// </summary>
    [Fact]
    public void El_saludo_de_una_caja_vieja_sin_StoreId_se_sigue_entendiendo()
    {
        const string deLaCajaVieja =
            """{"Version":1,"Challenge":"Y2hhbGxlbmdl","Tienda":"KOAJ Centro","Cajeros":3}""";

        var saludo = System.Text.Json.JsonSerializer.Deserialize<PairingGreeting>(deLaCajaVieja);

        Assert.NotNull(saludo);
        Assert.Equal(CashierRosterEnvelope.CurrentVersion, saludo!.Version);
        Assert.Equal("KOAJ Centro", saludo.Tienda);
        Assert.Equal(3, saludo.Cajeros);

        // Ausente, no roto. La pantalla cae a comparar por nombre de tienda.
        Assert.Null(saludo.StoreId);
    }

    /// <summary>
    /// Y la version del SOBRE no se toco. Subirla habria rechazado a todas las cajas
    /// viejas de un saque, que es exactamente lo que no se puede hacer.
    /// </summary>
    [Fact]
    public void La_version_del_sobre_no_cambio()
    {
        Assert.Equal(1, CashierRosterEnvelope.CurrentVersion);
    }

    [Fact]
    public void Una_caja_nueva_anuncia_su_StoreId_en_el_saludo()
    {
        var saludo = new PairingGreeting(
            CashierRosterEnvelope.CurrentVersion, "Y2hhbGxlbmdl", "KOAJ Centro", 3, "1043");

        var ida = System.Text.Json.JsonSerializer.Serialize(saludo);
        var vuelta = System.Text.Json.JsonSerializer.Deserialize<PairingGreeting>(ida);

        Assert.Equal("1043", vuelta!.StoreId);
    }

    /// <summary>
    /// EL SOBRE SIGUE SIN LLEVAR IDENTIDAD DE LA *CAJA*.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ESTE TEST CAMBIO, Y QUE SIGUE PROTEGIENDO
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes exigia que NO hubiera <c>StoreId</c> tampoco. Esa regla se escribio
    /// cuando la tienda bajaba de CloudLicense y llegaba sola a cada terminal; hoy
    /// se ELIGE en la caja, asi que sin copiarla habria que elegirla tres veces por
    /// local.
    ///
    /// El enunciado original de la regla era "si es de la TIENDA se copia, si es de
    /// la CAJA no", y el StoreId es de la tienda: la hoja de Sistecredito lo dice
    /// ("dato especifico por tienda") y las tres cajas de un local comparten el
    /// mismo. Copiarlo cumple la regla en vez de romperla.
    ///
    /// Lo que NO puede aparecer nunca es un identificador de TERMINAL: dos cajas
    /// con el mismo id firman igual sus operaciones y el descuadre se descubre
    /// semanas despues. Eso es lo que este test sigue cuidando.
    /// </summary>
    [Fact]
    public void El_sobre_sigue_sin_llevar_identidad_de_la_caja()
    {
        var nombres = typeof(CashierRosterEnvelope)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("TerminalId", nombres, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CajaId", nombres, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Source", nombres, StringComparer.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════
    // LA TIENDA VIAJA EN EL SOBRE (montaje de una caja nueva)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void El_sobre_lleva_la_tienda_del_local()
    {
        var sobre = new CashierRosterEnvelope(
            "hash-del-pin",
            [new ReplicatedCajero("1", "marcela4321", "", "h", true, DateTime.UtcNow)],
            StoreId: "607af8e38c91f70001436058");

        var vuelta = CashierRosterEnvelope.FromJson(sobre.ToJson());

        Assert.Equal("607af8e38c91f70001436058", vuelta!.StoreId);
    }

    /// <summary>
    /// CONVIVENCIA CON EL APK ANTERIOR, EN LOS DOS SENTIDOS.
    ///
    /// Las tiendas no se actualizan todas el mismo dia. Una caja nueva se va a
    /// encontrar cajas con el APK viejo, que emiten sobres SIN tienda, y esos
    /// tienen que seguir sirviendo para montar: por eso el campo es opcional y la
    /// version del sobre NO subio. (Al reves tambien funciona: un APK viejo que
    /// reciba un sobre nuevo ignora la propiedad que no conoce.)
    /// </summary>
    /// <summary>
    /// LA REGLA QUE SEPARA LAS DOS OPERACIONES.
    ///
    /// "Actualizar cajeros" no puede mover la tienda. Es un botón que dice cajeros:
    /// si además cambiara a nombre de qué tienda se venden los créditos, nadie lo
    /// vería hasta conciliar. Montar una caja nueva sí la aplica — ese es todo el
    /// punto de copiarla.
    /// </summary>
    [Fact]
    public void Al_actualizar_cajeros_la_tienda_NO_se_toca()
    {
        var delLocal = "607af8e38c91f70001436058";

        Assert.Null(SistecreditoTEF.Maui.ViewModels.ReplicacionViewModel
            .TiendaAAplicar(soloCajeros: true, storeIdDelSobre: delLocal));

        var alMontar = SistecreditoTEF.Maui.ViewModels.ReplicacionViewModel
            .TiendaAAplicar(soloCajeros: false, storeIdDelSobre: delLocal);

        Assert.NotNull(alMontar);
        Assert.Equal("037", alMontar!.Codigo);
    }

    [Theory]
    [InlineData(null)]                             // APK anterior: no la manda
    [InlineData("")]
    [InlineData("000000000000000000000000")]       // hoja mas nueva que este APK
    public void Una_tienda_que_no_se_reconoce_no_se_aplica(string? storeId)
    {
        Assert.Null(SistecreditoTEF.Maui.ViewModels.ReplicacionViewModel
            .TiendaAAplicar(soloCajeros: false, storeIdDelSobre: storeId));
    }

    [Fact]
    public void Un_sobre_sin_tienda_del_APK_anterior_se_sigue_aceptando()
    {
        // JSON exacto que emite el APK anterior: sin el campo StoreId.
        const string viejo = """
            {"v":1,"AdminPinHash":"hash-del-pin","Cajeros":[
              {"Id":"1","Usuario":"marcela4321","Nombre":"","ClaveHash":"h",
               "Activo":true,"CreadoEn":"2026-09-01T00:00:00Z"}]}
            """;

        var sobre = CashierRosterEnvelope.FromJson(viejo);

        Assert.NotNull(sobre);
        Assert.Null(sobre!.StoreId);
        Assert.Single(sobre.Cajeros);
    }
}
