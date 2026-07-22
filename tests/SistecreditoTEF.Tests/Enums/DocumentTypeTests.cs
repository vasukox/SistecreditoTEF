using SistecreditoTEF.Maui.Enums;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Enums;

public class DocumentTypeTests
{
    [Fact]
    public void FromCode_CC_returns_CedulaCiudadania()
    {
        Assert.Equal(DocumentType.CedulaCiudadania, DocumentTypeExtensions.FromCode("CC"));
    }

    [Fact]
    public void FromCode_CE_returns_CedulaExtranjeria()
    {
        Assert.Equal(DocumentType.CedulaExtranjeria, DocumentTypeExtensions.FromCode("CE"));
    }

    [Fact]
    public void FromCode_unknown_throws()
    {
        Assert.Throws<ArgumentException>(() => DocumentTypeExtensions.FromCode("XX"));
    }

    [Fact]
    public void FromCodeOrNull_returns_null_for_unknown()
    {
        Assert.Null(DocumentTypeExtensions.FromCodeOrNull("XX"));
        Assert.Null(DocumentTypeExtensions.FromCodeOrNull(null));
    }

    [Fact]
    public void Code_returns_correct_string()
    {
        Assert.Equal("CC", DocumentType.CedulaCiudadania.Code());
        Assert.Equal("CE", DocumentType.CedulaExtranjeria.Code());
    }
}
