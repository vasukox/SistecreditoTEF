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

        var clientes = ReadCustomers(root);

        return new SaleDocument
        {
            Header       = new DocumentHeader { Fields = ReadFields(root, "HeaderField") },
            Lines        = new DocumentLines  { Lines  = ReadLines(root) },
            PaymentMeans = ReadPaymentMeans(root),
            Customers    = clientes,
            Customer     = SelectCustomer(clientes)
        };
    }

    /// <summary>
    /// TODOS los elementos <c>Customer</c> del documento, con sus campos hoja que
    /// tengan atributo <c>Key</c> (FiscalId, FiscalIdDocType, Name, ...).
    ///
    /// Se leen todos —no solo el primero— porque el documento de HioPos puede
    /// traer más de uno: el cliente genérico del POS y el realmente asignado a la
    /// venta. Ver [SelectCustomer].
    /// </summary>
    private static List<DocumentCustomer> ReadCustomers(XElement root) =>
        root.DescendantsAndSelf()
            .Where(e => string.Equals(e.Name.LocalName, "Customer", StringComparison.OrdinalIgnoreCase))
            .Select(customer => new DocumentCustomer
            {
                Fields = customer.Descendants()
                    .Where(f => f.Attribute("Key") is not null && !f.HasElements)
                    .Select(ToField)
                    .ToList()
            })
            .ToList();

    /// <summary>
    /// Elige el cliente ASIGNADO A LA VENTA entre los que trae el documento.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// EL PROBLEMA QUE RESUELVE
    /// ─────────────────────────────────────────────────────────────────────────
    /// Antes se tomaba <c>FirstOrDefault</c>: el primer <c>Customer</c> en orden
    /// del documento. Cuando HioPos incluye el **cliente genérico** del POS
    /// (222222222222) antes del cliente real de la venta, el módulo autocompletaba
    /// la cédula del genérico. En el terminal se veía así: se facturaba a un
    /// cliente cuya cédula empieza por 430 y la pantalla de consulta traía
    /// 222222222222.
    ///
    /// Ahora se prefiere el primer cliente cuyo documento NO tenga forma de
    /// marcador genérico. Si todos son genéricos se devuelve el primero de todos
    /// modos —para no perder el resto de sus campos— y es
    /// [CapturaCedulaViewModel] el que decide no autocompletar, dejando el campo
    /// en blanco para captura manual.
    /// </summary>
    private static DocumentCustomer? SelectCustomer(List<DocumentCustomer> clientes)
    {
        if (clientes.Count == 0) return null;

        var real = clientes.FirstOrDefault(c =>
            DocumentNumber.IsUsableForAutocomplete(c.Fields.GetValue("FiscalId")));

        if (clientes.Count > 1)
        {
            // Diagnóstico con las cédulas ENMASCARADAS: permite ver en el terminal
            // cuántos clientes trae el documento y cuál se eligió.
            var descritos = string.Join(", ", clientes.Select((c, i) =>
            {
                var id = c.Fields.GetValue("FiscalId");
                var generico = DocumentNumber.IsGenericPlaceholder(id) ? " [GENERICO]" : "";
                return $"#{i}={PiiMask.Document(id)}{generico}";
            }));
            AppLogger.W("IDocumentReader",
                $"El documento trae {clientes.Count} elementos Customer: {descritos}. " +
                $"Elegido: {(real is null ? "ninguno usable" : PiiMask.Document(real.Fields.GetValue("FiscalId")))}.");
        }

        return real ?? clientes[0];
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
