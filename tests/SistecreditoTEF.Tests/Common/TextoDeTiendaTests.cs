using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// Con que tienda el modulo dice que esta operando.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTAS PRUEBAS FIJAN
/// ─────────────────────────────────────────────────────────────────────────────
/// Una caja de Suba sin provisionar tomaba el StoreId horneado en el APK —el de la
/// 037— y reportaba sus creditos a la 037. La venta salia bien. El error se
/// descubria conciliando en la plataforma de Sistecredito semanas despues.
///
/// Estos tests fijan las reglas del texto que se muestra, que es lo que convierte
/// ese silencio en algo que el instalador ve en el momento de montar la caja.
/// </summary>
public class TextoDeTiendaTests
{
    [Fact]
    public void Linea_muestra_el_nombre_y_el_StoreId_juntos()
    {
        var linea = TextoDeTienda.Linea("037 MULTIMARCA PUNTO CALLE 18", "607af8e38c91f70001436058");

        Assert.Contains("037 MULTIMARCA PUNTO CALLE 18", linea);
        Assert.Contains("607af8e38c91f70001436058", linea);
    }

    /// <summary>
    /// El StoreId se muestra COMPLETO. Recortado no sirve: contra la hoja hay que
    /// comparar los 24 caracteres, y un "607af8e…036058" no descarta nada.
    /// </summary>
    [Fact]
    public void El_StoreId_se_muestra_completo_para_poder_cotejarlo_con_la_hoja()
    {
        const string storeId = "607af8e38c91f70001436058";

        var linea = TextoDeTienda.Linea("037", storeId);

        Assert.Contains(storeId, linea);
        Assert.DoesNotContain("…", linea);
    }

    [Fact]
    public void El_nombre_y_el_StoreId_no_se_confunden_cuando_falta_el_nombre()
    {
        // Sin nombre de tienda lo que se ve tiene que seguir siendo inequivoco:
        // un pie en blanco se lee como "no hay nada".
        var linea = TextoDeTienda.Linea(null, "607d8d208c91f70001439630");

        Assert.Contains("(sin nombre de tienda)", linea);
        Assert.Contains("607d8d208c91f70001439630", linea);
    }

    [Fact]
    public void Una_caja_sin_StoreId_lo_dice_de_forma_explicita()
    {
        var linea = TextoDeTienda.Linea("Permoda", null);

        Assert.Contains("(sin StoreId)", linea);
    }

    /// <summary>
    /// El aviso es una INSTRUCCION, no un diagnostico.
    ///
    /// Decia "ESTA CAJA NO ESTA PROVISIONADA COMO TIENDA… no cobre hasta que
    /// sistemas lo habilite": describia un estado y no dejaba salida, porque la
    /// tienda dependia de que ICG provisionara el terminal. Ahora se elige en el
    /// propio terminal, asi que el texto tiene que decir donde tocar.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sin_tienda_el_aviso_dice_donde_elegirla(bool esProduccion)
    {
        var aviso = TextoDeTienda.Aviso(storeId: null, esProduccion: esProduccion);

        Assert.NotEmpty(aviso);
        Assert.Contains("elegir", aviso, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tienda", aviso, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El MISMO texto en los dos ambientes. Antes en sandbox decia "es normal en el
    /// ambiente de pruebas", y con eso el paso de elegir la tienda se podia saltar
    /// en pruebas y aparecer recien en produccion, con la caja ya instalada.
    /// </summary>
    [Fact]
    public void El_aviso_no_cambia_entre_ambientes()
    {
        Assert.Equal(
            TextoDeTienda.Aviso(storeId: null, esProduccion: true),
            TextoDeTienda.Aviso(storeId: null, esProduccion: false));
    }

    [Theory]
    [InlineData("607af8e38c91f70001436058")]
    [InlineData("607d8d208c91f70001439630")]
    [InlineData(" 607d9eef8475180001b167a6 ")]
    public void Con_StoreId_no_hay_aviso_que_mostrar(string storeId)
    {
        // Vacio es lo que permite enlazar el Label tal cual, sin logica de ocultar.
        Assert.Equal(string.Empty, TextoDeTienda.Aviso(storeId, esProduccion: true));
        Assert.Equal(string.Empty, TextoDeTienda.Aviso(storeId, esProduccion: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Un_StoreId_en_blanco_cuenta_como_ausente(string? storeId)
    {
        Assert.NotEqual(string.Empty, TextoDeTienda.Aviso(storeId, esProduccion: true));
    }

    /// <summary>
    /// El nombre y el StoreId se recortan de espacios: un padding de HioPos no
    /// puede dejar la linea con dos espacios en el medio ni el StoreId pegado,
    /// porque eso rompe el cotejo contra la hoja.
    /// </summary>
    [Fact]
    public void Se_recortan_los_espacios_que_trae_la_configuracion()
    {
        var linea = TextoDeTienda.Linea("  037 MULTIMARCA  ", "  607af8e38c91f70001436058  ");

        Assert.Equal("037 MULTIMARCA  ·  607af8e38c91f70001436058", linea);
    }
}
