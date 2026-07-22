namespace SistecreditoTEF.Maui.Enums;

/// <summary>
/// Frecuencia de pago del credito (cada cuanto paga el cliente).
///
/// Sistecredito maneja dos frecuencias:
///  - 14 dias (quincenal)
///  - 30 dias (mensual) - la mas comun
///
/// En el codigo actual siempre se envia 30 (mensual), pero el enum
/// queda definido para cuando se agregue soporte de quincenal.
/// </summary>
public enum CreditFrequency
{
    Quincenal,
    Mensual
}

public static class CreditFrequencyExtensions
{
    public static int Days(this CreditFrequency f) => f switch
    {
        CreditFrequency.Quincenal => 14,
        CreditFrequency.Mensual   => 30,
        _                         => throw new ArgumentOutOfRangeException(nameof(f))
    };

    public static string DisplayName(this CreditFrequency f) => f switch
    {
        CreditFrequency.Quincenal => "Quincenal",
        CreditFrequency.Mensual   => "Mensual",
        _                         => throw new ArgumentOutOfRangeException(nameof(f))
    };

    public static CreditFrequency FromDays(int days) =>
        days switch
        {
            14 => CreditFrequency.Quincenal,
            30 => CreditFrequency.Mensual,
            _  => throw new ArgumentException($"Frecuencia no soportada: {days} dias")
        };
}
