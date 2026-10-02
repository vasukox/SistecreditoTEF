using System.Net.Sockets;
using System.Text;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Tests.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Pairing;

/// <summary>
/// Replicacion del padron de cajeros entre cajas de una misma tienda.
///
/// Todo corre sobre 127.0.0.1 con un puerto que asigna el sistema, asi que el
/// emparejamiento completo se ejercita sin dispositivo y varias pruebas pueden
/// correr en paralelo sin pelearse el puerto.
/// </summary>
public class ReplicacionEntreCajasTests
{
    private const string PinHash = "PBKDF2$210000$c2FsdA==$aGFzaA==";

    private static InMemoryAuthStore CajaConfigurada(int cajeros = 2)
    {
        var store = new InMemoryAuthStore();
        store.SetAdminPinHashAsync(PinHash).GetAwaiter().GetResult();

        for (var i = 1; i <= cajeros; i++)
            store.GuardarCajeroAsync(new Cajero(
                Id: $"id-{i}",
                Usuario: $"cajero{i}",
                Nombre: $"Cajero {i}",
                ClaveHash: $"PBKDF2$210000$c2FsdA==$Y2xhdmUte{i}",
                Activo: true,
                CreadoEn: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)))
                .GetAwaiter().GetResult();

        return store;
    }

    private static PairingHost Abrir(IAuthStore store, Func<DateTimeOffset>? clock = null)
    {
        var host = new PairingHost(_ => store.ExportarPadronAsync(), "KOAJ CALLE 18", clock, port: 0);
        host.Start();
        return host;
    }

    // ==================================================================
    // El camino feliz
    // ==================================================================

    [Fact]
    public async Task Con_el_codigo_correcto_llega_el_padron_completo()
    {
        var origen = CajaConfigurada(cajeros: 3);
        await using var host = Abrir(origen);

        var resultado = await new PairingClient()
            .FetchAsync("127.0.0.1", host.Code, host.Port);

        Assert.True(resultado.Succeeded);
        Assert.Equal(3, resultado.Envelope!.Cajeros.Count);
        Assert.Equal(PinHash, resultado.Envelope.AdminPinHash);
        Assert.Equal("KOAJ CALLE 18", resultado.Tienda);
    }

    /// <summary>
    /// La prueba que cierra el circulo: el cajero entra en la caja nueva con la
    /// clave de siempre, sin que nadie la vuelva a teclear.
    /// </summary>
    [Fact]
    public async Task La_caja_nueva_queda_con_el_mismo_PIN_y_los_mismos_cajeros()
    {
        var origen = CajaConfigurada(cajeros: 2);
        var destino = new InMemoryAuthStore();

        await using var host = Abrir(origen);

        var resultado = await new PairingClient().FetchAsync("127.0.0.1", host.Code, host.Port);
        Assert.True(await destino.ImportarPadronAsync(resultado.Envelope!));

        Assert.Equal(PinHash, await destino.GetAdminPinHashAsync());

        var copiados = await destino.GetCajerosAsync();
        var originales = await origen.GetCajerosAsync();

        Assert.Equal(originales.Count, copiados.Count);
        Assert.Equal(
            originales.Select(c => (c.Usuario, c.ClaveHash)).OrderBy(x => x.Usuario),
            copiados.Select(c => (c.Usuario, c.ClaveHash)).OrderBy(x => x.Usuario));
    }

    /// <summary>
    /// El sobre se RELEE en cada entrega: entre la primera caja y la tercera el
    /// administrador pudo dar de alta un cajero, y la tercera tiene que recibir el
    /// padron de verdad y no una foto de hace ocho minutos.
    /// </summary>
    [Fact]
    public async Task Un_cajero_dado_de_alta_a_mitad_de_la_ventana_llega_a_la_caja_siguiente()
    {
        var origen = CajaConfigurada(cajeros: 1);
        await using var host = Abrir(origen);

        var primera = await new PairingClient().FetchAsync("127.0.0.1", host.Code, host.Port);
        Assert.Single(primera.Envelope!.Cajeros);

        await origen.GuardarCajeroAsync(new Cajero(
            "id-nuevo", "recien", "Recien Llegado", "PBKDF2$210000$c2FsdA==$bnVldm8=",
            true, DateTime.UtcNow));

        var segunda = await new PairingClient().FetchAsync("127.0.0.1", host.Code, host.Port);

        Assert.Equal(2, segunda.Envelope!.Cajeros.Count);
        Assert.Contains(segunda.Envelope.Cajeros, c => c.Usuario == "recien");
    }

    /// <summary>
    /// Un codigo sirve para varias cajas dentro de la ventana: quien instala genera
    /// uno y camina la tienda sin volver a la primera caja. Los aciertos no queman
    /// intentos.
    /// </summary>
    [Fact]
    public async Task Un_mismo_codigo_configura_varias_cajas()
    {
        var origen = CajaConfigurada();
        await using var host = Abrir(origen);

        for (var i = 0; i < 3; i++)
            Assert.True((await new PairingClient()
                .FetchAsync("127.0.0.1", host.Code, host.Port)).Succeeded);

        Assert.Equal(3, host.SuccessfulTransfers);
        Assert.Equal(PairingSecret.MaxAttempts, host.AttemptsRemaining);
    }

    // ==================================================================
    // Los caminos malos
    // ==================================================================

    [Fact]
    public async Task Con_el_codigo_equivocado_no_se_entrega_nada()
    {
        var origen = CajaConfigurada();
        await using var host = Abrir(origen);

        var codigoMalo = host.Code == "000000" ? "111111" : "000000";
        var resultado = await new PairingClient().FetchAsync("127.0.0.1", codigoMalo, host.Port);

        Assert.Equal(PairingOutcome.InvalidCode, resultado.Outcome);
        Assert.Null(resultado.Envelope);
        Assert.Equal(PairingSecret.MaxAttempts - 1, resultado.AttemptsRemaining);
    }

    /// <summary>
    /// Tres fallos queman el codigo, y despues NI EL BUENO revive: es lo que hace
    /// que adivinar un codigo de un millon no sea una opcion.
    /// </summary>
    [Fact]
    public async Task Tres_fallos_queman_el_codigo_y_despues_ni_el_bueno_sirve()
    {
        var origen = CajaConfigurada();
        await using var host = Abrir(origen);

        var malo = host.Code == "000000" ? "111111" : "000000";
        var cliente = new PairingClient();

        for (var i = 0; i < PairingSecret.MaxAttempts; i++)
            await cliente.FetchAsync("127.0.0.1", malo, host.Port);

        Assert.Equal(0, host.AttemptsRemaining);

        var conElBueno = await cliente.FetchAsync("127.0.0.1", host.Code, host.Port);

        Assert.Equal(PairingOutcome.NoAttemptsLeft, conElBueno.Outcome);
        Assert.Null(conElBueno.Envelope);
    }

    /// <summary>
    /// La trampa que costo media hora en una tienda en el modulo de origen: el
    /// servidor mandaba el error SIN el saludo previo, y el cliente —que esperaba
    /// un saludo— reportaba "no se entiende la respuesta" cuando lo que pasaba era,
    /// textualmente, que se habia vencido la ventana.
    /// </summary>
    [Fact]
    public async Task Una_ventana_vencida_se_reporta_como_vencida_y_no_como_error_generico()
    {
        var ahora = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var reloj = () => ahora;

        var origen = CajaConfigurada();
        await using var host = Abrir(origen, reloj);

        ahora = ahora + PairingProtocol.Window + TimeSpan.FromMinutes(1);

        var resultado = await new PairingClient().FetchAsync("127.0.0.1", host.Code, host.Port);

        Assert.Equal(PairingOutcome.WindowClosed, resultado.Outcome);
        Assert.False(host.IsOpen);
    }

    [Fact]
    public async Task Sin_nadie_repartiendo_se_reporta_inalcanzable_y_no_revienta()
    {
        // Puerto libre: se abre y se cierra para quedarse con uno que nadie atiende.
        var libre = new TcpListener(System.Net.IPAddress.Loopback, 0);
        libre.Start();
        var port = ((System.Net.IPEndPoint)libre.LocalEndpoint).Port;
        libre.Stop();

        var resultado = await new PairingClient().FetchAsync("127.0.0.1", "123456", port);

        Assert.Equal(PairingOutcome.Unreachable, resultado.Outcome);
    }

    [Fact]
    public async Task Cerrar_la_pantalla_deja_de_responder_en_el_acto()
    {
        var origen = CajaConfigurada();
        var host = Abrir(origen);
        var codigo = host.Code;
        var port = host.Port;

        await host.DisposeAsync();

        var resultado = await new PairingClient().FetchAsync("127.0.0.1", codigo, port);

        Assert.Equal(PairingOutcome.Unreachable, resultado.Outcome);
    }

    // ==================================================================
    // Una caja incompleta no reparte
    // ==================================================================

    [Fact]
    public async Task Una_caja_sin_cajeros_no_puede_exportar_nada()
    {
        var soloPin = new InMemoryAuthStore();
        await soloPin.SetAdminPinHashAsync(PinHash);

        Assert.Null(await soloPin.ExportarPadronAsync());
    }

    [Fact]
    public async Task Una_caja_sin_PIN_no_puede_exportar_nada()
    {
        var soloCajeros = new InMemoryAuthStore();
        await soloCajeros.GuardarCajeroAsync(new Cajero(
            "id-1", "cajero1", "Cajero 1", "PBKDF2$210000$c2FsdA==$aGFzaA==",
            true, DateTime.UtcNow));

        Assert.Null(await soloCajeros.ExportarPadronAsync());
    }

    // ==================================================================
    // El sobre
    // ==================================================================

    /// <summary>
    /// El sobre NO lleva identidad de la CAJA ni credenciales, y no es un olvido.
    ///
    /// Las credenciales y el endpoint los asigna CloudLicense; copiarlos seria
    /// pisar con un valor prestado algo que la central ya definio. Y un
    /// identificador de TERMINAL es peor todavia: dos cajas con el mismo id firman
    /// igual sus operaciones y el descuadre se descubre semanas despues.
    ///
    /// <c>StoreId</c> salio de esta lista a proposito —ver
    /// [CashierRosterEnvelope]—. La regla es "si es de la TIENDA se copia, si es de
    /// la CAJA no", y la tienda es del local: las tres cajas comparten la misma, y
    /// desde que se elige en el terminal (y ya no baja de CloudLicense) copiarla es
    /// lo que evita tener que acertarla tres veces por local.
    ///
    /// Se verifica por reflexion para que nadie agregue el resto "por conveniencia"
    /// dentro de seis meses.
    /// </summary>
    [Theory]
    [InlineData("TerminalId")]
    [InlineData("SubscriptionKey")]
    [InlineData("BaseUrl")]
    [InlineData("Source")]
    public void El_sobre_no_lleva_identidad_de_la_caja_ni_credenciales(string prohibido)
    {
        var propiedades = typeof(CashierRosterEnvelope)
            .GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(prohibido, propiedades);
    }

    [Fact]
    public void Un_sobre_de_otra_version_se_rechaza_entero()
    {
        var sobre = new CashierRosterEnvelope(PinHash,
            [new ReplicatedCajero("id", "u", "n", "h", true, DateTime.UtcNow)])
        { Version = CashierRosterEnvelope.CurrentVersion + 1 };

        Assert.Null(CashierRosterEnvelope.FromJson(sobre.ToJson()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no es json")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"AdminPinHash\":\"x\",\"Cajeros\":[]}")]
    public void Una_entrada_rota_o_truncada_devuelve_null_y_nunca_lanza(string? json)
    {
        Assert.Null(CashierRosterEnvelope.FromJson(json));
    }

    // ==================================================================
    // Criptografia
    // ==================================================================

    [Fact]
    public void El_codigo_tiene_seis_digitos_y_no_se_repite_siempre()
    {
        var codigos = Enumerable.Range(0, 50).Select(_ => PairingSecret.GenerateCode()).ToList();

        Assert.All(codigos, c => Assert.Matches(@"^\d{6}$", c));
        Assert.True(codigos.Distinct().Count() > 1, "el generador devolvio siempre lo mismo");
    }

    [Fact]
    public void El_sobre_cifrado_no_se_abre_con_otro_codigo()
    {
        var reto = PairingSecret.GenerateChallenge();
        var cifrado = PairingSecret.Encrypt("123456", reto, "{\"v\":1}");

        Assert.Null(PairingSecret.Decrypt("654321", reto, cifrado));
    }

    /// <summary>
    /// AES-GCM autentica: si algo en la red altera un byte, el descifrado FALLA en
    /// vez de entregar un padron corrupto que la caja guardaria como bueno.
    /// </summary>
    [Fact]
    public void Un_paquete_alterado_no_se_descifra()
    {
        var reto = PairingSecret.GenerateChallenge();
        var cifrado = PairingSecret.Encrypt("123456", reto, "{\"v\":1}");

        cifrado[^1] ^= 0xFF;

        Assert.Null(PairingSecret.Decrypt("123456", reto, cifrado));
    }

    [Fact]
    public void Un_paquete_truncado_no_se_descifra()
    {
        var reto = PairingSecret.GenerateChallenge();
        var cifrado = PairingSecret.Encrypt("123456", reto, "{\"v\":1}");

        Assert.Null(PairingSecret.Decrypt("123456", reto, cifrado[..4]));
    }

    /// <summary>
    /// Vale por varias: se abre un socket a pelo y se lee lo PRIMERO que manda el
    /// servidor, comprobando que ahi no estan ni el codigo, ni el hash del PIN, ni
    /// ninguna clave.
    /// </summary>
    [Fact]
    public async Task El_padron_nunca_viaja_legible()
    {
        var origen = CajaConfigurada();
        await using var host = Abrir(origen);

        using var crudo = new TcpClient();
        await crudo.ConnectAsync("127.0.0.1", host.Port);

        await using var stream = crudo.GetStream();
        var buffer = new byte[8192];
        var leidos = await stream.ReadAsync(buffer);
        var saludo = Encoding.UTF8.GetString(buffer, 0, leidos);

        Assert.DoesNotContain(host.Code, saludo, StringComparison.Ordinal);
        Assert.DoesNotContain(PinHash, saludo, StringComparison.Ordinal);
        Assert.DoesNotContain("cajero1", saludo, StringComparison.Ordinal);
    }
}
