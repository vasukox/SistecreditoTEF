using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Platform;

/// <summary>
/// El lector migro de XmlSerializer (fallaba en Android) a XDocument. Estas
/// pruebas cubren el parseo puro, incluida la lectura de PaymentMeans que se
/// usara para el ModifyDocumentResult (HU-134).
/// </summary>
public class XmlDocumentReaderTests
{
    private const string Sample = @"<Document>
  <Header>
    <HeaderFields>
      <HeaderField Key=""SaleId"">ABC-123</HeaderField>
      <HeaderField Key=""DocumentTypeId"">1</HeaderField>
      <HeaderField Key=""NetAmount"">29900</HeaderField>
      <HeaderField Key=""TaxesAmount"">0</HeaderField>
    </HeaderFields>
  </Header>
  <Lines>
    <Line><LineFields>
      <LineField Key=""Units"">1</LineField>
      <LineField Key=""Name"">Camisa</LineField>
    </LineFields></Line>
  </Lines>
  <PaymentMeans>
    <PaymentMean>
      <PaymentMeanFields>
        <PaymentMeanField Key=""PaymentMeanId"">7</PaymentMeanField>
        <PaymentMeanField Key=""Type"">0</PaymentMeanField>
        <PaymentMeanField Key=""Amount"">29900</PaymentMeanField>
      </PaymentMeanFields>
    </PaymentMean>
  </PaymentMeans>
</Document>";

    [Fact]
    public void Parse_lee_header_y_total()
    {
        var doc = XmlDocumentReader.Parse(Sample);

        Assert.NotNull(doc);
        Assert.Equal("ABC-123", doc!.SaleId);
        Assert.Equal("1", doc.DocumentTypeId);
        Assert.Equal(29900m, doc.Total);
    }

    [Fact]
    public void Parse_lee_lineas_de_producto()
    {
        var doc = XmlDocumentReader.Parse(Sample);

        Assert.Contains("1x Camisa", doc!.ProductDescriptions);
    }

    [Fact]
    public void Parse_lee_payment_means()
    {
        var doc = XmlDocumentReader.Parse(Sample);

        var pm = Assert.Single(doc!.PaymentMeans);
        Assert.Equal("7", pm.PaymentMeanId);
        Assert.Equal("0", pm.Type);
        Assert.Equal(29900m, pm.Amount);
    }

    [Fact]
    public void Parse_es_agnostico_al_namespace()
    {
        var conNs = Sample.Replace("<Document>", "<Document xmlns=\"urn:icg:hipos\">");

        var doc = XmlDocumentReader.Parse(conNs);

        Assert.Equal("ABC-123", doc!.SaleId);
        Assert.Single(doc.PaymentMeans);
    }
}
