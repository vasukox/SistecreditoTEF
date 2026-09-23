using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Qué está pidiendo realmente un <c>TransactionType=REFUND</c>.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// HIOPOS MANDA EL MISMO INTENT PARA DOS COSAS
/// ─────────────────────────────────────────────────────────────────────────────────
///   • SOLTAR LA LINEA DE PAGO ("la papelera"). El cajero cobro con el TEF, algo
///     fallo despues, y necesita liberar ese medio para seguir con la venta.
///     Se acepta.
///   • ABONO / NOTA DE CREDITO. La plata va hacia el cliente. Credinet no tiene
///     operacion de reverso, asi que se rechaza.
///
/// Si se acepta todo REFUND, se aceptan abonos.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// EL CRITERIO ES EL SIGNO DE NetAmount, NO EL DocumentTypeId
/// ─────────────────────────────────────────────────────────────────────────────────
/// Esto nos costo un abono aceptado en produccion. El contrato de ICG enumera los
/// tipos de documento 1 a 7 y dice que los abonos son el 3 y el 4. Se comprobaban
/// esos dos.
///
/// El abono REAL llego con <c>DocumentTypeId = 28</c>. Medido en caja
/// 037 T.KOAJ Calle 18 Montevideo, serie K50H. Ese valor no esta en ninguna lista
/// del contrato: no se reconocio, se trato como venta, y el abono se hizo.
///
/// El signo de <c>Header.NetAmount</c> es SEMANTICO y no un numero de catalogo:
///
///     &lt;HeaderField Key="NetAmount"&gt;-119700,0000&lt;/HeaderField&gt;
///
/// Negativo significa que la plata va hacia el cliente. Una venta de la que solo se
/// quiere soltar la linea lo trae positivo. No depende de conocer de antemano cada
/// tipo que ICG invente.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// ANTE LA DUDA, NO
/// ─────────────────────────────────────────────────────────────────────────────────
/// Si no hay documento legible se RECHAZA. Antes se aceptaba —para no trabar el
/// desmarcado— y ese "fallar abierto" es justamente por donde se cuela un abono.
/// Entre trabar una papelera y regalar plata, se traba la papelera.
///
/// Es codigo puro: se testea sin Android.
/// </summary>
public enum TipoDeRefund
{
    /// <summary>Venta en curso: el cajero quiere soltar la linea de pago. Se acepta.</summary>
    DesmarcarLineaDePago,

    /// <summary>Abono o nota de credito. Se rechaza.</summary>
    NotaDeCredito,

    /// <summary>
    /// No se pudo determinar: sin documento, XML ilegible, o sin los campos.
    /// <strong>Se trata como nota de credito</strong>: ante la duda, no.
    /// </summary>
    Indeterminado
}

public static class RefundClassifier
{
    /// <summary>
    /// DocumentTypeId conocidos de abono. El 3 y el 4 vienen del contrato; el
    /// <strong>28</strong> se midio en caja y no figura en ninguna lista de ICG.
    ///
    /// Es una red secundaria: el criterio principal es el signo de NetAmount, que no
    /// necesita que este catalogo este completo. Si aparece un tipo nuevo, el signo
    /// lo atrapa igual.
    /// </summary>
    private static readonly string[] TiposDeAbono = ["3", "4", "28"];

    /// <summary>
    /// Clasifica el REFUND. Orden de decision, y el orden importa:
    ///
    ///   1. NetAmount negativo        -> abono
    ///   2. DocumentTypeId de abono   -> abono
    ///   3. sin documento legible     -> abono (ante la duda, no)
    ///   4. resto                     -> venta, soltar la linea
    /// </summary>
    public static TipoDeRefund Clasificar(string? documentXml)
    {
        if (string.IsNullOrWhiteSpace(documentXml)) return TipoDeRefund.Indeterminado;

        var doc = LeerDocumento(documentXml);
        if (doc is null) return TipoDeRefund.Indeterminado;

        // 1. EL SIGNO MANDA.
        //
        // Se mira el PRIMER CARACTER, no se parsea el numero. El valor viene con coma
        // decimal ("-119700,0000") y parsearlo obligaria a fijar una cultura para
        // responder algo que ya esta en el signo. Menos codigo y ningun riesgo de
        // interpretar la coma como separador de miles.
        var netAmount = doc.Header?.Fields.GetValue("NetAmount")?.Trim();
        if (!string.IsNullOrEmpty(netAmount))
        {
            if (netAmount[0] == '-') return TipoDeRefund.NotaDeCredito;

            // Hay NetAmount y es positivo: es una venta. Ni siquiera hace falta el
            // DocumentTypeId.
            return TipoDeRefund.DesmarcarLineaDePago;
        }

        // 2. Sin NetAmount, la red secundaria por tipo de documento.
        var tipo = doc.DocumentTypeId?.Trim();
        if (!string.IsNullOrWhiteSpace(tipo) && Array.IndexOf(TiposDeAbono, tipo) >= 0)
            return TipoDeRefund.NotaDeCredito;

        // 3. Ni signo ni tipo reconocible: no se sabe, y no se acepta.
        return TipoDeRefund.Indeterminado;
    }

    /// <summary>
    /// El <c>NetAmount</c> crudo, tal como vino. Para el log: cuando algo se rechaza,
    /// el signo es el unico dato que explica por que.
    /// </summary>
    public static string? LeerNetAmount(string? documentXml) =>
        LeerDocumento(documentXml)?.Header?.Fields.GetValue("NetAmount")?.Trim();

    /// <summary>El <c>DocumentTypeId</c> crudo, o null. Para el log.</summary>
    public static string? LeerDocumentTypeId(string? documentXml)
    {
        var valor = LeerDocumento(documentXml)?.DocumentTypeId?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    private static SistecreditoTEF.Maui.Models.SaleDocument? LeerDocumento(string? documentXml)
    {
        if (string.IsNullOrWhiteSpace(documentXml)) return null;

        try
        {
            // Se reutiliza el parser del modulo: es agnostico al namespace y ya
            // recolecta los HeaderField por atributo Key.
            return XmlDocumentReader.Parse(documentXml);
        }
        catch (Exception)
        {
            // Parse LANZA ante XML malformado. Un documento ilegible no es una venta
            // confirmada: es no saber, y eso ahora se rechaza.
            return null;
        }
    }
}
