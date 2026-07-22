using SistecreditoTEF.Maui.Dtos;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Mappers;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Mappers;

/// <summary>
/// Verifica que el mapper DTO -> Domain:
///  1. Copia todos los campos cuando el DTO los trae.
///  2. Aplica defaults seguros (0 / "" / false / enum default) cuando el DTO los trae null.
///  3. Mapea typeDocument correctamente a DocumentType (CC/CE).
///
/// V5: SimulatedCreditDto / CreditDetailsDto fueron consolidados en un
/// unico CreditDetailsDto, asi que los tests son mas simples.
/// </summary>
public class CredinetMapperTests
{
    [Fact]
    public void ClientDto_toDomain_copia_campos_cuando_presentes()
    {
        var dto = new ClientDto(
            TypeDocument: "CC",
            IdDocument: "1234567890",
            CreditLimit: 80_000_000.0,
            AvailableCreditLimit: 80_089_900.0,
            ValidatedMail: true,
            NewCreditButtonEnabled: true,
            Email: "cliente@credinet.co",
            Mobile: "3001234567",
            FullName: "Cliente Test",
            Defaulter: false,
            CreditLimitIncrease: true,
            IsAvailableCreditLimit: true,
            IsActive: true,
            Status: 1,
            StatusName: "Approved",
            FirstName: "Cliente",
            SecondName: "Test");

        var domain = dto.ToDomain();

        Assert.Equal(DocumentType.CedulaCiudadania, domain.DocumentType);
        Assert.Equal("1234567890", domain.DocumentId);
        Assert.Equal(80_000_000.0, domain.CreditLimit);
        Assert.Equal("cliente@credinet.co", domain.Email);
        Assert.True(domain.IsActive);
        Assert.Equal(1, domain.Status);
        Assert.Equal("Approved", domain.StatusName);
    }

    [Fact]
    public void ClientDto_toDomain_usa_defaults_seguros_con_nulls()
    {
        var dto = new ClientDto(
            TypeDocument: null, IdDocument: null,
            CreditLimit: null, AvailableCreditLimit: null,
            ValidatedMail: null, NewCreditButtonEnabled: null,
            Email: null, Mobile: null, FullName: null,
            Defaulter: null, CreditLimitIncrease: null,
            IsAvailableCreditLimit: null, IsActive: null,
            Status: null, StatusName: null,
            FirstName: null, SecondName: null);

        var domain = dto.ToDomain();

        Assert.Equal(DocumentType.CedulaCiudadania, domain.DocumentType);
        Assert.Equal("", domain.DocumentId);
        Assert.Equal("", domain.Email);
        Assert.Equal(0.0, domain.CreditLimit);
        Assert.Equal(0, domain.Status);
        Assert.False(domain.IsActive);
    }

    [Fact]
    public void ClientDto_toDomain_CE_mapea_CedulaExtranjeria()
    {
        var dto = new ClientDto(
            TypeDocument: "CE", IdDocument: "999",
            CreditLimit: 0.0, AvailableCreditLimit: 0.0,
            ValidatedMail: null, NewCreditButtonEnabled: null,
            Email: null, Mobile: null, FullName: null,
            Defaulter: null, CreditLimitIncrease: null,
            IsAvailableCreditLimit: null, IsActive: null,
            Status: null, StatusName: null,
            FirstName: null, SecondName: null);

        var domain = dto.ToDomain();

        Assert.Equal(DocumentType.CedulaExtranjeria, domain.DocumentType);
    }

    [Fact]
    public void CreditDetailsDto_toDomain_aplica_defaults_a_nulls()
    {
        var dto = new CreditDetailsDto(
            DownPayment: null, TotalFeeValue: null, CreditValue: null,
            Fees: null, AssuranceValue: null, InterestRate: null,
            TotalInterestValue: null, TotalDownPayment: null,
            FeeCreditValue: null, AssuranceFeeValue: null,
            AssuranceTotalValue: null, AssuranceTaxFeeValue: null,
            AssuranceTaxValue: null, DownPaymentPercentage: null,
            AssurancePercentage: null, AssuranceTotalFeeValue: null,
            TotalPaymentValue: null, CustomerAllowPhotoSignature: null);

        var domain = dto.ToDomain();

        Assert.Equal(0.0, domain.CreditValue);
        Assert.Equal(0, domain.Fees);
        Assert.False(domain.CustomerAllowPhotoSignature);
    }
}
