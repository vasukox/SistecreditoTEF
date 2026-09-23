using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Platform;

/// <summary>
/// Elección del cliente asignado a la venta cuando el documento de HioPos trae
/// más de un <c>Customer</c>.
///
/// Caso reproducido del terminal: se factura a un cliente cuya cédula empieza por
/// 430, y el módulo autocompletaba 222222222222 —el cliente genérico del POS—
/// porque tomaba el PRIMER <c>Customer</c> en orden del documento.
/// </summary>
public class ClienteDelDocumentoTests
{
    private const string Generico = "222222222222";
    private const string Real = "430123456";

    private static string Doc(params string[] clientes)
    {
        var nodos = string.Concat(clientes.Select(c => $"""
            <Customer>
              <CustomerField Key="FiscalId">{c}</CustomerField>
              <CustomerField Key="FiscalIdDocType">CC</CustomerField>
              <CustomerField Key="Name">Cliente {c}</CustomerField>
            </Customer>
            """));

        return $"""
            <Document>
              <Header><HeaderFields>
                <HeaderField Key="SaleId">venta-1</HeaderField>
              </HeaderFields></Header>
              {nodos}
            </Document>
            """;
    }

    [Fact]
    public void Con_el_generico_PRIMERO_se_elige_el_cliente_real()
    {
        // Este es exactamente el orden que producía el bug.
        var doc = XmlDocumentReader.Parse(Doc(Generico, Real));

        Assert.NotNull(doc);
        Assert.Equal(2, doc!.Customers.Count);
        Assert.Equal(Real, doc.CustomerFiscalId);
    }

    [Fact]
    public void Con_el_cliente_real_PRIMERO_tambien_se_elige_el_real()
    {
        var doc = XmlDocumentReader.Parse(Doc(Real, Generico));

        Assert.Equal(Real, doc!.CustomerFiscalId);
    }

    [Fact]
    public void Con_un_solo_cliente_real_se_usa_ese()
    {
        var doc = XmlDocumentReader.Parse(Doc(Real));

        Assert.Single(doc!.Customers);
        Assert.Equal(Real, doc.CustomerFiscalId);
    }

    [Fact]
    public void Con_solo_el_generico_se_conserva_pero_queda_marcado_como_no_usable()
    {
        // Se devuelve el cliente (para no perder sus otros campos), pero la
        // pantalla de captura no debe autocompletar con él.
        var doc = XmlDocumentReader.Parse(Doc(Generico));

        Assert.Equal(Generico, doc!.CustomerFiscalId);
        Assert.True(SistecreditoTEF.Maui.Common.DocumentNumber
            .IsGenericPlaceholder(doc.CustomerFiscalId));
    }

    [Fact]
    public void Sin_elemento_Customer_el_cliente_es_null()
    {
        var doc = XmlDocumentReader.Parse("""
            <Document><Header><HeaderFields>
              <HeaderField Key="SaleId">venta-1</HeaderField>
            </HeaderFields></Header></Document>
            """);

        Assert.NotNull(doc);
        Assert.Null(doc!.Customer);
        Assert.Empty(doc.Customers);
        Assert.Null(doc.CustomerFiscalId);
    }

    [Fact]
    public void Se_leen_los_clientes_aunque_el_XML_tenga_namespace()
    {
        var xml = $"""
            <Document xmlns="http://schemas.icg.es/hiopos/document">
              <Customer>
                <CustomerField Key="FiscalId">{Generico}</CustomerField>
              </Customer>
              <Customer>
                <CustomerField Key="FiscalId">{Real}</CustomerField>
              </Customer>
            </Document>
            """;

        var doc = XmlDocumentReader.Parse(xml);

        Assert.Equal(2, doc!.Customers.Count);
        Assert.Equal(Real, doc.CustomerFiscalId);
    }

    [Fact]
    public void Se_conserva_el_tipo_de_documento_del_cliente_elegido()
    {
        var doc = XmlDocumentReader.Parse(Doc(Generico, Real));

        Assert.Equal("CC", doc!.CustomerFiscalDocType);
        Assert.Contains(Real, doc.CustomerName, StringComparison.Ordinal);
    }

    [Fact]
    public void El_resto_del_documento_se_sigue_leyendo()
    {
        // La elección del cliente no debe afectar el resto del parseo.
        var doc = XmlDocumentReader.Parse(Doc(Generico, Real));

        Assert.Equal("venta-1", doc!.SaleId);
    }
}
