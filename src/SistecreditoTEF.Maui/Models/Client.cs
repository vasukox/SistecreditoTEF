using SistecreditoTEF.Maui.Enums;

namespace SistecreditoTEF.Maui.Models;

/// <summary>
/// Modelo de negocio: Cliente validado por CREDINET.
/// 1:1 con Client.kt de Kotlin. Todos los campos son no nulos con defaults
/// seguros (cualquier capa superior puede usar Client sin defenderse de nulls).
/// </summary>
public record Client(
    DocumentType DocumentType,
    string DocumentId,
    double CreditLimit,
    double AvailableCreditLimit,
    bool ValidatedMail,
    bool NewCreditButtonEnabled,
    string Email,
    string Mobile,
    string FullName,
    bool Defaulter,
    bool CreditLimitIncrease,
    bool IsAvailableCreditLimit,
    bool IsActive,
    int Status,
    string StatusName,
    string FirstName,
    string SecondName);
