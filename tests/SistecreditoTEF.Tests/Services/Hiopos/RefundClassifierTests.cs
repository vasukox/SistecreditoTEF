using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// HioPos manda el MISMO intent (<c>TransactionType=REFUND</c>) para soltar la linea
/// de pago de una venta y para un abono. Aceptar todo REFUND es aceptar abonos.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// EL ABONO QUE SE COLO, Y POR QUE
/// ─────────────────────────────────────────────────────────────────────────────────
/// El contrato de ICG enumera los tipos de documento 1 a 7 y dice que los abonos son
/// el 3 y el 4. Se comprobaban esos dos, y el resto se trataba como venta.
///
/// El abono real llego con <c>DocumentTypeId = 28</c> —medido en caja 037 T.KOAJ
/// Calle 18 Montevideo, serie K50H—, un valor que no figura en ninguna lista del
/// contrato. No se reconocio, se trato como venta, y el abono se hizo.
///
/// El criterio pasa a ser el SIGNO de <c>Header.NetAmount</c>, que es semantico y no
/// un numero de catalogo: negativo significa que la plata va hacia el cliente.
/// </summary>
public class RefundClassifierTests
{
    private static string Doc(string? netAmount, string? documentTypeId)
    {
        var campos = "";
        if (documentTypeId is not null)
            campos += $"<HeaderField Key=\"DocumentTypeId\">{documentTypeId}</HeaderField>";
        if (netAmount is not null)
            campos += $"<HeaderField Key=\"NetAmount\">{netAmount}</HeaderField>";

        return $"<Document><Header><HeaderFields>{campos}</HeaderFields></Header></Document>";
    }

    // ------------------------------------------------------------------
    // El signo manda
    // ------------------------------------------------------------------

    /// <summary>
    /// El caso exacto que se colo: tipo 28, que no esta en el contrato. Lo atrapa el
    /// signo, sin necesidad de conocer el catalogo.
    /// </summary>
    [Fact]
    public void El_abono_real_con_tipo_28_se_detecta_por_el_signo()
    {
        var xml = Doc(netAmount: "-119700,0000", documentTypeId: "28");

        Assert.Equal(TipoDeRefund.NotaDeCredito, RefundClassifier.Clasificar(xml));
    }

    /// <summary>
    /// Un tipo que NADIE conoce, con importe negativo, sigue siendo abono. Esta es la
    /// propiedad que hace que el criterio no dependa del catalogo de ICG.
    /// </summary>
    [Theory]
    [InlineData("28")]
    [InlineData("99")]
    [InlineData("1234")]
    [InlineData(null)]
    public void Cualquier_tipo_con_importe_negativo_es_abono(string? tipo)
    {
        Assert.Equal(TipoDeRefund.NotaDeCredito,
            RefundClassifier.Clasificar(Doc("-39900,0000", tipo)));
    }

    /// <summary>
    /// Importe POSITIVO es una venta de la que se quiere soltar la linea. Se acepta,
    /// incluso con un tipo desconocido: el signo ya dijo que la plata no va al
    /// cliente.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("99")]
    public void Importe_positivo_es_venta_y_se_acepta(string tipo)
    {
        Assert.Equal(TipoDeRefund.DesmarcarLineaDePago,
            RefundClassifier.Clasificar(Doc("119700,0000", tipo)));
    }

    /// <summary>
    /// El valor viene con COMA decimal. Se mira el primer caracter y no se parsea:
    /// parsear obligaria a fijar una cultura para responder algo que ya esta en el
    /// signo, y con la coma mal interpretada un importe puede salir 10 000 veces
    /// mayor sin que nada falle.
    /// </summary>
    [Theory]
    [InlineData("-119700,0000")]
    [InlineData("-1,5000")]
    [InlineData("-0,0001")]
    [InlineData("  -39900,0000  ")]
    public void El_signo_se_lee_sin_parsear_el_numero(string netAmount)
    {
        Assert.Equal(TipoDeRefund.NotaDeCredito,
            RefundClassifier.Clasificar(Doc(netAmount, "2")));
    }

    // ------------------------------------------------------------------
    // Red secundaria por tipo, cuando no hay signo
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("28")]
    public void Sin_NetAmount_los_tipos_de_abono_conocidos_se_rechazan(string tipo)
    {
        Assert.Equal(TipoDeRefund.NotaDeCredito,
            RefundClassifier.Clasificar(Doc(netAmount: null, documentTypeId: tipo)));
    }

    // ------------------------------------------------------------------
    // Ante la duda, NO
    // ------------------------------------------------------------------

    /// <summary>
    /// Sin documento, con XML ilegible o sin los campos, NO se acepta.
    ///
    /// Antes esto se aceptaba, para no trabar el desmarcado de la linea. Ese fallar
    /// abierto es exactamente por donde se colo el abono. Entre trabar una papelera y
    /// regalar plata, se traba la papelera.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-xml")]
    [InlineData("<Document><Header><HeaderFields></HeaderFields></Header></Document>")]
    public void Sin_datos_suficientes_NO_se_acepta(string? xml)
    {
        var r = RefundClassifier.Clasificar(xml);

        Assert.NotEqual(TipoDeRefund.DesmarcarLineaDePago, r);
        Assert.Equal(TipoDeRefund.Indeterminado, r);
    }

    [Fact]
    public void Un_xml_malformado_no_lanza_y_no_se_acepta()
    {
        var roto = "<Document><Header><HeaderField Key=\"NetAmount\">-119700";

        Assert.Equal(TipoDeRefund.Indeterminado, RefundClassifier.Clasificar(roto));
    }

    /// <summary>
    /// Un tipo desconocido SIN importe tampoco se acepta: no hay con que decidir.
    /// </summary>
    [Fact]
    public void Tipo_desconocido_sin_importe_no_se_acepta()
    {
        var r = RefundClassifier.Clasificar(Doc(netAmount: null, documentTypeId: "77"));

        Assert.NotEqual(TipoDeRefund.DesmarcarLineaDePago, r);
    }

    // ------------------------------------------------------------------
    // Robustez del formato y traza
    // ------------------------------------------------------------------

    [Fact]
    public void Funciona_con_namespace_en_el_documento()
    {
        var conNamespace = """
            <d:Document xmlns:d="http://icg/doc">
              <d:Header><d:HeaderFields>
                <d:HeaderField Key="NetAmount">-119700,0000</d:HeaderField>
              </d:HeaderFields></d:Header>
            </d:Document>
            """;

        Assert.Equal(TipoDeRefund.NotaDeCredito, RefundClassifier.Clasificar(conNamespace));
    }

    /// <summary>
    /// Los valores crudos se exponen para el log: cuando algo se rechaza, el signo y
    /// el tipo son los unicos datos que explican por que.
    /// </summary>
    [Fact]
    public void Se_pueden_leer_los_valores_crudos_para_el_log()
    {
        var xml = Doc("-119700,0000", "28");

        Assert.Equal("-119700,0000", RefundClassifier.LeerNetAmount(xml));
        Assert.Equal("28", RefundClassifier.LeerDocumentTypeId(xml));
        Assert.Null(RefundClassifier.LeerNetAmount(null));
        Assert.Null(RefundClassifier.LeerDocumentTypeId("no-es-xml"));
    }
}
