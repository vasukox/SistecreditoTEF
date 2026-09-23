using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// El volcado de extras del Intent existe para responder UNA pregunta: qué campo
/// distingue un cobro iniciado desde la terminal de una venta normal, dado que HioPos
/// usa la misma acción TRANSACTION para las dos.
///
/// Estos tests cubren las dos propiedades que lo hacen usable: que muestre los campos
/// de ruteo con su valor real, y que NUNCA publique datos del cliente. Lo segundo
/// importa porque en un POS con adb habilitado para soporte cualquiera con acceso USB
/// lee logcat, y un volcado crudo convertiría el diagnóstico en una fuga permanente.
/// </summary>
public class HioposExtrasDiagnosticsTests
{
    private const string Cedula = "1026260942";

    // ------------------------------------------------------------------
    // Lo que SÍ debe mostrar
    // ------------------------------------------------------------------

    [Fact]
    public void Muestra_los_campos_que_deciden_el_ruteo_con_su_valor_real()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType]   = "SALE",
            [HioposExtras.IsAdvancedPayment] = "true",
            [HioposExtras.OverPaymentType]   = "2",
            [HioposExtras.TenderType]        = "CREDIT"
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        // Son los cuatro candidatos a explicar el abono: tienen que verse enteros.
        Assert.Contains("TransactionType=SALE", linea);
        Assert.Contains("IsAdvancedPayment=true", linea);
        Assert.Contains("OverPaymentType=2", linea);
        Assert.Contains("TenderType=CREDIT", linea);
    }

    [Fact]
    public void Los_campos_de_ruteo_salen_en_orden_fijo_para_poder_comparar_capturas()
    {
        // El diagnóstico se usa comparando dos capturas —una venta y un abono— línea
        // contra línea. Si el orden variara, comparar sería a ojo.
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.OverPaymentType]   = "0",
            [HioposExtras.TransactionType]   = "SALE",
            [HioposExtras.IsAdvancedPayment] = "false"
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.True(
            linea.IndexOf("TransactionType", StringComparison.Ordinal)
            < linea.IndexOf("IsAdvancedPayment", StringComparison.Ordinal),
            "TransactionType debe ir antes que IsAdvancedPayment.");
        Assert.True(
            linea.IndexOf("IsAdvancedPayment", StringComparison.Ordinal)
            < linea.IndexOf("OverPaymentType", StringComparison.Ordinal),
            "IsAdvancedPayment debe ir antes que OverPaymentType.");
    }

    [Fact]
    public void Un_extra_desconocido_se_reporta_por_nombre()
    {
        // Es el caso más valioso: si HioPos marca el abono con un campo que no está
        // en el contrato que conocemos, tiene que aparecer.
        var extras = new Dictionary<string, string?>
        {
            ["CollectionMode"] = "DEBT"
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.Contains("NO_RECONOCIDOS", linea);
        Assert.Contains("CollectionMode=DEBT", linea);
    }

    // ------------------------------------------------------------------
    // Lo que NUNCA debe mostrar
    // ------------------------------------------------------------------

    [Fact]
    public void El_XML_del_documento_no_se_vuelca_nunca()
    {
        // DocumentData es la venta completa: nombre y cédula del cliente.
        var xml = $"<Document><Customer><FiscalId>{Cedula}</FiscalId>" +
                  "<Name>JUAN PEREZ GOMEZ</Name></Customer></Document>";
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.DocumentData] = xml
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.DoesNotContain(Cedula, linea);
        Assert.DoesNotContain("JUAN", linea);
        // Pero sí consta que llegó y su tamaño: es lo que se necesita para
        // diagnosticar que el documento se está recibiendo.
        Assert.Contains("DocumentData=(presente", linea);
    }

    [Fact]
    public void El_token_de_sesion_no_se_vuelca_nunca()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.Token] = "token-de-sesion-secreto"
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.DoesNotContain("token-de-sesion-secreto", linea);
    }

    [Fact]
    public void Una_cedula_dentro_de_un_extra_desconocido_se_enmascara()
    {
        // No hay nombre de campo que avise "acá viene una cédula", así que el
        // enmascarado se hace por forma: rachas largas de dígitos.
        var extras = new Dictionary<string, string?>
        {
            ["CampoNuevoDeICG"] = $"cliente:{Cedula}"
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.DoesNotContain(Cedula, linea);
        Assert.Contains("0942", linea);   // los últimos 4 sí, para poder conciliar
    }

    [Fact]
    public void Un_valor_desconocido_muy_largo_se_recorta()
    {
        var extras = new Dictionary<string, string?>
        {
            ["Payload"] = new string('x', 500)
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.Contains("...(500 chars)", linea);
        Assert.True(linea.Length < 200, "La línea debe seguir siendo legible en logcat.");
    }

    // ------------------------------------------------------------------
    // Robustez: es código de log, no debe poder tumbar una venta
    // ------------------------------------------------------------------

    [Fact]
    public void Sin_bundle_lo_dice_en_vez_de_lanzar()
    {
        Assert.Equal("EXTRAS: (sin bundle)", HioposExtrasDiagnostics.Describe(null));
    }

    [Fact]
    public void Con_bundle_vacio_lo_dice_en_vez_de_lanzar()
    {
        var linea = HioposExtrasDiagnostics.Describe(new Dictionary<string, string?>());

        Assert.Equal("EXTRAS: (bundle vacio)", linea);
    }

    [Fact]
    public void Un_extra_con_valor_nulo_no_rompe_la_descripcion()
    {
        var extras = new Dictionary<string, string?>
        {
            [HioposExtras.TransactionType] = null,
            ["OtroCampo"] = null
        };

        var linea = HioposExtrasDiagnostics.Describe(extras);

        Assert.Contains("TransactionType=(vacio)", linea);
        Assert.Contains("OtroCampo=(vacio)", linea);
    }
}
