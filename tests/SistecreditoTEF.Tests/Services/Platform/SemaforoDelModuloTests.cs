using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Platform;

/// <summary>
/// EL SEMAFORO TIENE QUE PODER PONERSE EN ROJO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTO EXISTE
/// ─────────────────────────────────────────────────────────────────────────────
/// El indicador anterior miraba si el terminal tenia wifi. En una caja eso esta
/// en verde casi siempre, asi que decia "En linea" con Credinet caido, con el
/// certificado fallando y con la tienda rechazada. Se reporto desde la terminal
/// con el problema encima: "ahi aparece siempre el en linea y por eso uno no sabe
/// si esa es la verdad".
///
/// Un indicador que no puede contradecirse no informa; confunde, porque la gente
/// lo cree. Estas pruebas fijan los cinco estados y, sobre todo, cual de ellos
/// BLOQUEA.
/// </summary>
public class SemaforoDelModuloTests
{
    private sealed class Red(bool hay) : IEstadoDeRed
    {
        public bool HayConexion { get; set; } = hay;

        public event EventHandler? Cambio { add { } remove { } }
    }

    private static EstadoDeLaConexion Armar(bool conRed = true) => new(new Red(conRed));

    // ══════════════════════════════════════════════════════════════════════════
    // LO QUE SE SABE Y LO QUE NO
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Antes de hablar con Credinet NO se dice "En linea". Se dice que no se
    /// sabe, que es la verdad. Esta es la diferencia con el indicador viejo, que
    /// arrancaba en verde.
    /// </summary>
    [Fact]
    public void Al_arrancar_no_se_afirma_que_hay_linea()
    {
        var salud = Armar();

        Assert.Equal(SaludDelModulo.SinVerificar, salud.Estado);
        Assert.Null(salud.UltimaRespuesta);
        Assert.True(salud.PuedeOperar);
    }

    [Fact]
    public void Una_respuesta_de_Credinet_pone_el_semaforo_en_verde()
    {
        var salud = Armar();

        salud.RegistrarRespuesta(errorCode: 0);

        Assert.Equal(SaludDelModulo.EnLinea, salud.Estado);
        Assert.NotNull(salud.UltimaRespuesta);
    }

    /// <summary>
    /// EL CASO QUE EL INDICADOR VIEJO PINTABA DE VERDE: hay wifi, pero Credinet
    /// no contesta. Es el estado en el que el cajero se queda esperando sin saber
    /// por que.
    /// </summary>
    [Fact]
    public void Con_wifi_pero_sin_respuesta_de_Credinet_no_dice_en_linea()
    {
        var salud = Armar(conRed: true);

        salud.RegistrarFalloDeRed();

        Assert.Equal(SaludDelModulo.SinRespuesta, salud.Estado);
        Assert.True(salud.PuedeOperar);   // transitorio: no bloquea
    }

    [Fact]
    public void Sin_red_lo_dice_aunque_la_ultima_llamada_haya_salido_bien()
    {
        var red = new Red(true);
        var salud = new EstadoDeLaConexion(red);
        salud.RegistrarRespuesta(0);
        Assert.Equal(SaludDelModulo.EnLinea, salud.Estado);

        // Se cae el wifi. No se puede seguir afirmando "en linea" por una llamada
        // que salio bien hace un rato.
        red.HayConexion = false;

        Assert.Equal(SaludDelModulo.SinRed, salud.Estado);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EL UNICO ESTADO QUE BLOQUEA
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA PRUEBA QUE IMPORTA. errorCode 225 (StoreNotFound) significa que
    /// Sistecredito no reconoce la tienda de esta caja. A partir de ahi no se
    /// vende ni se recauda: la operacion saldria bien en el POS y quedaria a
    /// nombre de otra tienda, que es el defecto que se pago caro.
    /// </summary>
    [Fact]
    public void Si_Credinet_rechaza_la_tienda_la_caja_deja_de_operar()
    {
        var salud = Armar();

        salud.RegistrarRespuesta(EstadoDeLaConexion.StoreNotFound);

        Assert.Equal(SaludDelModulo.TiendaRechazada, salud.Estado);
        Assert.False(salud.PuedeOperar);
    }

    /// <summary>
    /// Y el rechazo NO se borra solo. Ni una llamada posterior que salga bien
    /// —puede ser otra que no lleve tienda— ni un corte de red lo tapan: es un
    /// problema de configuracion y sigue estando ahi.
    /// </summary>
    [Fact]
    public void El_rechazo_de_tienda_no_se_tapa_con_nada()
    {
        var red = new Red(true);
        var salud = new EstadoDeLaConexion(red);
        salud.RegistrarRespuesta(EstadoDeLaConexion.StoreNotFound);

        salud.RegistrarFalloDeRed();
        Assert.False(salud.PuedeOperar);

        red.HayConexion = false;
        Assert.Equal(SaludDelModulo.TiendaRechazada, salud.Estado);

        red.HayConexion = true;
        Assert.Equal(SaludDelModulo.TiendaRechazada, salud.Estado);
    }

    /// <summary>
    /// Pero SI se borra al elegir otra tienda. Si no, corregir el problema dejaria
    /// la caja bloqueada igual y el instalador no tendria forma de desbloquearla.
    /// Lo llama [TiendaViewModel] al aplicar una tienda.
    /// </summary>
    [Fact]
    public void Elegir_otra_tienda_limpia_el_rechazo()
    {
        var salud = Armar();
        salud.RegistrarRespuesta(EstadoDeLaConexion.StoreNotFound);
        Assert.False(salud.PuedeOperar);

        salud.Olvidar();

        Assert.Equal(SaludDelModulo.SinVerificar, salud.Estado);
        Assert.True(salud.PuedeOperar);
    }

    /// <summary>
    /// Un rechazo de NEGOCIO cualquiera (cliente sin cupo, credito inexistente) no
    /// es un problema de la caja: la linea funciona y la tienda sirve.
    /// </summary>
    [Theory]
    [InlineData(231)]   // CreditsNotFound
    [InlineData(222)]   // MonthsNumberNotValid
    [InlineData(226)]   // CustomerProfileInvalid
    public void Otros_errores_de_negocio_no_bloquean_la_caja(int errorCode)
    {
        var salud = Armar();

        salud.RegistrarRespuesta(errorCode);

        Assert.Equal(SaludDelModulo.EnLinea, salud.Estado);
        Assert.True(salud.PuedeOperar);
    }
}
