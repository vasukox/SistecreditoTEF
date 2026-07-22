using SistecreditoTEF.Maui.Dtos;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Mappers;

/// <summary>
/// DTO -> Domain. 1:1 con CredinetMapper.kt de Kotlin.
///
/// Cada mapper aplana nulls a defaults seguros para que la UI nunca
/// reciba nulls (cualquier nullable en JSON se traduce a 0 / "" / false
/// segun corresponda al tipo del destino).
///
/// V5: SimulatedCreditDto / CreditDetailsDto fueron consolidados en
/// un unico CreditDetailsDto, asi que [ToDomain] de CreditDetails
/// queda unico.
/// </summary>
public static class CredinetMapper
{
    public static Client ToDomain(this ClientDto dto) => new(
        DocumentType:           DocumentTypeExtensions.FromCodeOrNull(dto.TypeDocument)
                                ?? DocumentType.CedulaCiudadania,
        DocumentId:             dto.IdDocument ?? "",
        CreditLimit:            dto.CreditLimit ?? 0.0,
        AvailableCreditLimit:   dto.AvailableCreditLimit ?? 0.0,
        ValidatedMail:          dto.ValidatedMail ?? false,
        NewCreditButtonEnabled: dto.NewCreditButtonEnabled ?? false,
        Email:                  dto.Email ?? "",
        Mobile:                 dto.Mobile ?? "",
        FullName:               dto.FullName ?? "",
        Defaulter:              dto.Defaulter ?? false,
        CreditLimitIncrease:    dto.CreditLimitIncrease ?? false,
        IsAvailableCreditLimit: dto.IsAvailableCreditLimit ?? false,
        IsActive:               dto.IsActive ?? false,
        Status:                 dto.Status ?? 0,
        StatusName:             dto.StatusName ?? "",
        FirstName:              dto.FirstName ?? "",
        SecondName:             dto.SecondName ?? "");

    public static CreditDetails ToDomain(this CreditDetailsDto dto) => new(
        DownPayment:                dto.DownPayment ?? 0.0,
        TotalFeeValue:              dto.TotalFeeValue ?? 0.0,
        CreditValue:                dto.CreditValue ?? 0.0,
        Fees:                       dto.Fees ?? 0,
        AssuranceValue:             dto.AssuranceValue ?? 0.0,
        InterestRate:               dto.InterestRate ?? 0.0,
        TotalInterestValue:         dto.TotalInterestValue ?? 0.0,
        TotalDownPayment:           dto.TotalDownPayment ?? 0.0,
        FeeCreditValue:             dto.FeeCreditValue ?? 0.0,
        AssuranceFeeValue:          dto.AssuranceFeeValue ?? 0.0,
        AssuranceTotalValue:        dto.AssuranceTotalValue ?? 0.0,
        AssuranceTaxFeeValue:       dto.AssuranceTaxFeeValue ?? 0.0,
        AssuranceTaxValue:          dto.AssuranceTaxValue ?? 0.0,
        DownPaymentPercentage:      dto.DownPaymentPercentage ?? 0.0,
        AssurancePercentage:        dto.AssurancePercentage ?? 0.0,
        AssuranceTotalFeeValue:     dto.AssuranceTotalFeeValue ?? 0.0,
        TotalPaymentValue:          dto.TotalPaymentValue ?? 0.0,
        CustomerAllowPhotoSignature: dto.CustomerAllowPhotoSignature ?? false);

    public static CreditToken ToDomain(this CreditTokenDto dto) => new(
        TokenValue:       dto.Token?.Value ?? "",
        RemainingSeconds: dto.Token?.RemainingSeconds ?? 0,
        FormattedToken:   dto.FormattedToken ?? "",
        TotalTime:        dto.TotalTime ?? 0,
        Duration:         dto.Duration ?? "",
        ExpirationDate:   dto.ExpirationDate ?? "",
        GenerationDate:   dto.GenerationDate ?? "",
        TokenGenerated:   dto.TokenGenerated ?? false);

    public static Credit ToDomain(this CreditDto dto) => new(
        TypeDocument:        dto.TypeDocument ?? "",
        IdDocument:          dto.IdDocument ?? "",
        CreditId:            dto.CreditId ?? "",
        CreditNumber:        dto.CreditNumber ?? 0,
        EffectiveAnnualRate: dto.EffectiveAnnualRate ?? 0.0,
        DownPayment:         dto.DownPayment ?? 0.0,
        TotalFeeValue:       dto.TotalFeeValue ?? 0.0,
        CreditValue:         dto.CreditValue ?? 0.0,
        Fees:                dto.Fees ?? 0,
        AssuranceValue:      dto.AssuranceValue ?? 0.0,
        InterestRate:        dto.InterestRate ?? 0.0,
        TotalInterestValue:  dto.TotalInterestValue ?? 0.0,
        TotalDownPayment:    dto.TotalDownPayment ?? 0.0,
        FeeCreditValue:      dto.FeeCreditValue ?? 0.0,
        AssuranceFeeValue:   dto.AssuranceFeeValue ?? 0.0,
        AssuranceTotalValue: dto.AssuranceTotalValue ?? 0.0,
        AssuranceTaxFeeValue: dto.AssuranceTaxFeeValue ?? 0.0);

    public static ActiveCredit ToDomain(this ActiveCreditDto dto) => new(
        TypeDocument:   dto.TypeDocument ?? "",
        IdDocument:     dto.IdDocument ?? "",
        CreditId:       dto.CreditId ?? "",
        CreditNumber:   dto.CreditNumber ?? 0,
        CreateDate:     dto.CreateDate ?? "",
        CreditValue:    dto.CreditValue ?? 0.0,
        ArrearsDays:    dto.ArrearsDays ?? 0,
        MinimumPayment: dto.MinimumPayment ?? 0.0,
        TotalPayment:   dto.TotalPayment ?? 0.0,
        FeeValue:       dto.FeeValue ?? 0.0,
        StoreName:      dto.StoreName ?? "",
        Balance:        dto.Balance ?? 0.0,
        DueDate:        dto.DueDate ?? "");

    public static Payment ToDomain(this PaymentDto dto) => new(
        TypeDocument:        dto.TypeDocument ?? "",
        IdDocument:          dto.IdDocument ?? "",
        CreditId:            dto.CreditId ?? "",
        PaymentId:           dto.PaymentId ?? "",
        PaymentNumber:       dto.PaymentNumber ?? 0,
        CreditValuePaid:     dto.CreditValuePaid ?? 0.0,
        InterestValuePaid:   dto.InterestValuePaid ?? 0.0,
        ArrearsValuePaid:    dto.ArrearsValuePaid ?? 0.0,
        AssuranceValuePaid:  dto.AssuranceValuePaid ?? 0.0,
        ChargeValuePaid:     dto.ChargeValuePaid ?? 0.0,
        Balance:             dto.Balance ?? 0.0,
        NextDueDate:         dto.NextDueDate ?? "",
        NextMinimumPayment:  dto.NextMinimumPayment ?? 0.0);

    public static SimulatedMonthLimit ToDomain(this SimulatedMonthLimitDto dto) =>
        new(dto.Months ?? 0);
}
