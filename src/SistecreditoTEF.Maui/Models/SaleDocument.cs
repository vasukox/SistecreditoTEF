using System.Xml.Serialization;

namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo deserializado del XML <c>&lt;Document&gt;</c> que HioPosCloud
/// escribe cuando [OnlyUseDocumentPath=true] (doc §6).
///
/// Contiene SOLO los campos que el modulo TEF usa. El resto se ignora
/// silenciosamente por XmlSerializer.
///
/// Doc §6.2: los campos criticos para Sistecredito son:
///   - Header.SaleId           -> idempotencia
///   - Header.DocumentTypeId   -> tipo comprobante
///   - Header.TaxesAmount+NetAmount -> total
///   - Lines                   -> productos (descriptivo)
///   - PaymentMeans            -> medios ya aplicados
/// </summary>
[XmlRoot("Document")]
public sealed class SaleDocument
{
    [XmlElement("Header")]  public DocumentHeader? Header { get; set; }
    [XmlElement("Lines")]   public DocumentLines?  Lines  { get; set; }

    /// <summary>
    /// Medios de pago ya presentes en el documento (doc §6.2). Se usan para
    /// construir el <c>ModifyDocumentResult</c> (HU-134) con el PaymentMeanId
    /// correcto sin inventarlo. Poblado por [XmlDocumentReader] via XDocument.
    /// </summary>
    public List<DocumentPaymentMean> PaymentMeans { get; set; } = new();

    /// <summary>
    /// Cliente asignado a la venta en HioPos (doc §21). Se usa para
    /// autocompletar la cedula en la captura. Poblado por [XmlDocumentReader].
    /// null si la venta no tiene cliente asignado (venta anonima).
    /// </summary>
    public DocumentCustomer? Customer { get; set; }

    public string? SaleId           => Header?.Fields.GetValue("SaleId");
    public string? DocumentTypeId   => Header?.Fields.GetValue("DocumentTypeId");
    public decimal TaxesAmount      => Header?.Fields.GetDecimal("TaxesAmount") ?? 0m;
    public decimal NetAmount        => Header?.Fields.GetDecimal("NetAmount") ?? 0m;
    public decimal Total            => TaxesAmount + NetAmount;

    /// <summary>Cedula del cliente asignado en HioPos (FiscalId), o null.</summary>
    public string? CustomerFiscalId      => Customer?.Fields.GetValue("FiscalId");
    /// <summary>Tipo de documento del cliente segun HioPos (FiscalIdDocType).</summary>
    public string? CustomerFiscalDocType => Customer?.Fields.GetValue("FiscalIdDocType");
    public string? CustomerName          => Customer?.Fields.GetValue("Name");

    public IEnumerable<string> ProductDescriptions =>
        Lines?.Lines?
            .Select(l => $"{l.Fields.GetValue("Units") ?? "1"}x {l.Fields.GetValue("Name") ?? "Item"}")
        ?? Enumerable.Empty<string>();
}

/// <summary>
/// Un medio de pago del documento con sus campos (PaymentMeanId, Type,
/// LineNumber, Amount, ...). Estructura generica: [XmlDocumentReader]
/// recolecta cualquier campo con atributo <c>Key</c>.
/// </summary>
public sealed class DocumentPaymentMean
{
    public List<DocumentField> Fields { get; set; } = new();

    public string? PaymentMeanId => Fields.GetValue("PaymentMeanId");
    public string? Type          => Fields.GetValue("Type");
    public string? LineNumber    => Fields.GetValue("LineNumber");
    public decimal Amount        => Fields.GetDecimal("Amount") ?? 0m;
}

/// <summary>
/// Cliente del documento con sus campos (FiscalId, FiscalIdDocType, Name, ...).
/// Estructura generica: [XmlDocumentReader] recolecta cualquier campo con
/// atributo <c>Key</c> dentro de <c>&lt;Customer&gt;</c> (doc §21).
/// </summary>
public sealed class DocumentCustomer
{
    public List<DocumentField> Fields { get; set; } = new();
}

public sealed class DocumentHeader
{
    [XmlArray("HeaderFields"), XmlArrayItem("HeaderField")]
    public List<DocumentField> Fields { get; set; } = new();
}

public sealed class DocumentLines
{
    [XmlElement("Line")]
    public List<DocumentLine>? Lines { get; set; }
}

public sealed class DocumentLine
{
    [XmlArray("LineFields"), XmlArrayItem("LineField")]
    public List<DocumentField> Fields { get; set; } = new();
}

/// <summary>
/// Estructura generica <c>&lt;...Field Key="..."&gt;VALUE&lt;/...Field&gt;</c>
/// que HioPosCloud usa repetidamente (doc §6.1).
/// </summary>
public sealed class DocumentField
{
    [XmlAttribute("Key")] public string Key { get; set; } = string.Empty;
    [XmlText]             public string Value { get; set; } = string.Empty;

    /// <summary>KISS: serializa como un Attribute + Text que el XML acepta.</summary>
    [XmlIgnore] public string XmlRaw
    {
        get => $"{Key}={Value}";
        set { /* solo deserializacion */ }
    }
}

public static class DocumentFieldCollectionExtensions
{
    public static string? GetValue(this IEnumerable<DocumentField> fields, string key) =>
        fields.FirstOrDefault(f =>
            string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    public static decimal? GetDecimal(this IEnumerable<DocumentField> fields, string key)
    {
        var raw = fields.GetValue(key);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
