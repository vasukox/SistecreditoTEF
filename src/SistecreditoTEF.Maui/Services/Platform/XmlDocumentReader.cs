using System.Xml.Linq;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Implementacion de [IDocumentReader] usando XDocument (LINQ to XML).
///
/// Antes usaba XmlSerializer, que fallaba en Android por el trimming
/// (busca el assembly generado <c>*.XmlSerializers</c> que no existe y la
/// deserializacion por reflexion queda recortada) -> ActiveDocument null.
/// XDocument no depende de metadata recortable, asi que es robusto.
///
/// Doc §6: el XML viene en disco (no en el Intent) cuando
/// [OnlyUseDocumentPath=true]. Hay que leerlo ANTES de Finish() porque el
/// sistema puede borrar el fichero (doc §12 gotcha #10).
///
/// El parseo es AGNOSTICO al namespace (compara por LocalName) y a la
/// estructura exacta: recolecta cualquier elemento con atributo <c>Key</c>.
/// </summary>
public class XmlDocumentReader : IDocumentReader
{
    public SaleDocument? Read(string documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
        {
            AppLogger.W("IDocumentReader", $"DocumentPath no existe: '{documentPath}'.");
            return null;
        }

        try
        {
            var raw = File.ReadAllText(documentPath);

            // NO loguear el XML crudo: contiene PII (cedula, nombre, montos).
            // Solo registramos el tamano para diagnostico.
            AppLogger.I("IDocumentReader", $"SaleDocument recibido ({raw.Length} chars).");

            var doc = Parse(raw);
            if (doc is null)
            {
                AppLogger.W("IDocumentReader", "Documento sin elemento raiz.");
                return null;
            }

            AppLogger.I("IDocumentReader",
                $"Doc leido: SaleId={doc.SaleId ?? "?"}, Lines={doc.Lines?.Lines?.Count ?? 0}, " +
                $"PaymentMeans={doc.PaymentMeans.Count}.");
            return doc;
        }
        catch (Exception ex)
        {
            AppLogger.E("IDocumentReader", $"Error parseando XML en {documentPath}.", ex);
            return null;
        }
    }

    /// <summary>
    /// Parseo puro (sin IO ni logging) para poder testearlo. Agnostico al
    /// namespace y a la estructura exacta.
    /// </summary>
    public static SaleDocument? Parse(string rawXml)
    {
        var root = XDocument.Parse(rawXml).Root;
        if (root is null) return null;

        return new SaleDocument
        {
            Header       = new DocumentHeader { Fields = ReadFields(root, "HeaderField") },
            Lines        = new DocumentLines  { Lines  = ReadLines(root) },
            PaymentMeans = ReadPaymentMeans(root),
            Customer     = ReadCustomer(root)
        };
    }

    /// <summary>
    /// Cliente asignado a la venta (doc §21). Toma el primer elemento
    /// <c>Customer</c> y recolecta sus campos hoja con atributo <c>Key</c>
    /// (FiscalId, FiscalIdDocType, Name, ...). null si la venta va sin cliente.
    /// </summary>
    private static DocumentCustomer? ReadCustomer(XElement root)
    {
        var customer = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Customer");
        if (customer is null) return null;

        return new DocumentCustomer
        {
            Fields = customer.Descendants()
                .Where(f => f.Attribute("Key") is not null && !f.HasElements)
                .Select(ToField)
                .ToList()
        };
    }

    /// <summary>Todos los campos con LocalName dado, en cualquier parte.</summary>
    private static List<DocumentField> ReadFields(XElement root, string fieldLocalName) =>
        root.Descendants()
            .Where(e => e.Name.LocalName == fieldLocalName)
            .Select(ToField)
            .ToList();

    private static List<DocumentLine> ReadLines(XElement root) =>
        root.Descendants()
            .Where(e => e.Name.LocalName == "Line")
            .Select(line => new DocumentLine
            {
                Fields = line.Descendants()
                    .Where(f => f.Name.LocalName == "LineField")
                    .Select(ToField)
                    .ToList()
            })
            .ToList();

    private static List<DocumentPaymentMean> ReadPaymentMeans(XElement root) =>
        root.Descendants()
            .Where(e => e.Name.LocalName == "PaymentMean")
            .Select(pm => new DocumentPaymentMean
            {
                // Generico: cualquier elemento hoja con atributo Key
                // (PaymentMeanField y CustomPaymentMeanField por igual).
                Fields = pm.Descendants()
                    .Where(f => f.Attribute("Key") is not null && !f.HasElements)
                    .Select(ToField)
                    .ToList()
            })
            .ToList();

    private static DocumentField ToField(XElement e) => new()
    {
        Key   = e.Attribute("Key")?.Value ?? string.Empty,
        Value = (e.Value ?? string.Empty).Trim()
    };
}
