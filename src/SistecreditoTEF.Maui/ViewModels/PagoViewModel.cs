using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 7: pago de un credito activo.
/// El creditId viene del [ITransactionStateStore.SelectedCredit]
/// (antes se pasaba vacio y fallaba siempre - O1).
/// </summary>
public partial class PagoViewModel(
    SistecreditoService service,
    INavigationService nav,
    ITransactionStateStore state) : ObservableObject
{
    public enum Estado { Idle, Loading, Success, Error }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private string? errorMessage;

    // HU8-973: sin NotifyCanExecuteChangedFor, el boton "Pagar" nunca re-evaluaba
    // CanExecute al escribir el monto y quedaba deshabilitado (tap = "no pasa nada").
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PagarCommand))]
    private decimal monto = 0m;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PagarCommand))]
    private ActiveCredit? creditoSeleccionado;

    public string Titulo       => CreditoSeleccionado is null ? "Pago" : $"Pago #{CreditoSeleccionado.CreditNumber}";
    public string Subtitulo    => $"Credito {CreditoSeleccionado?.CreditId?[..Math.Min(8, CreditoSeleccionado.CreditId.Length)]}...";
    public string SaldoTexto   => CreditoSeleccionado?.Balance.ToColombianCurrency() ?? "$ 0";
    public string MinimoTexto  => CreditoSeleccionado?.MinimumPayment.ToColombianCurrency() ?? "$ 0";

    public bool IsLoading => Status == Estado.Loading;
    public bool HasError => Status == Estado.Error;

    // HU8-973 BugFix #5: hint visible para el cajero sobre el rango valido.
    public string MontoHint
    {
        get
        {
            if (CreditoSeleccionado is null) return string.Empty;
            var min = CreditoSeleccionado.MinimumPayment;
            var max = CreditoSeleccionado.Balance;
            return $"Entre {min.ToColombianCurrency()} y {max.ToColombianCurrency()}";
        }
    }

    partial void OnStatusChanged(Estado value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnCreditoSeleccionadoChanged(ActiveCredit? value)
    {
        OnPropertyChanged(nameof(Titulo));
        OnPropertyChanged(nameof(Subtitulo));
        OnPropertyChanged(nameof(SaldoTexto));
        OnPropertyChanged(nameof(MinimoTexto));
        OnPropertyChanged(nameof(MontoHint));
        if (value is not null && Monto <= 0)
            Monto = (decimal)value.MinimumPayment;
    }

    partial void OnMontoChanged(decimal value)
    {
        OnPropertyChanged(nameof(MontoHint));
    }

    public void Inicializar()
    {
        CreditoSeleccionado = state.SelectedCredit;

        // Anti-doble-cobro: si hay un intento reciente (<60s) para el mismo
        // credito, avisamos Y bloqueamos el boton. CanPagar consulta el mismo
        // guard; aqui refrescamos el CanExecute para que el boton quede
        // APAGADO (antes el mensaje salia pero el boton seguia activo -> se
        // podia volver a cobrar).
        if (HasRecentPaymentAttempt())
        {
            ErrorMessage = "Ya registramos un pago reciente para este credito. Espera unos segundos antes de reintentar.";
            Status = Estado.Error;
        }
        PagarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// True si hay un intento de pago reciente (mismo credito, &lt;60s) todavia
    /// sin confirmar como exitoso. Es la barrera anti-doble-cobro compartida
    /// entre [Inicializar] (aviso) y [CanPagar] (bloqueo del boton).
    /// </summary>
    private bool HasRecentPaymentAttempt() =>
        CreditoSeleccionado is not null
        && state.LastPaymentAttemptCreditId == CreditoSeleccionado.CreditId
        && state.LastPaymentAttemptAt is { } lastAt
        && (DateTime.UtcNow - lastAt).TotalSeconds < 60;

    [RelayCommand(CanExecute = nameof(CanPagar))]
    private async Task PagarAsync()
    {
        if (CreditoSeleccionado is null || Monto <= 0) return;

        var creditId = CreditoSeleccionado.CreditId;

        // HU8-973 BugFix #1 (parte 1): marca de intento al INICIO.
        state.LastPaymentAttemptCreditId = creditId;
        state.LastPaymentAttemptAt       = DateTime.UtcNow;

        Status = Estado.Loading;
        try
        {
            var result = await service.PagarCreditoAsync(
                creditId, (double)Monto, userName: "Cajero Permoda");

            switch (result)
            {
                case ApiResult<Payment>.Ok<Payment> ok:
                    state.SetLastPayment(ok.Data);
                    Status = Estado.Success;
                    // Limpiamos la marca para permitir el siguiente pago normalmente.
                    state.LastPaymentAttemptCreditId = null;
                    state.LastPaymentAttemptAt       = null;
                    await nav.GoToReciboPagoAsync(ok.Data);
                    break;
                case ApiResult<Payment>.Failure<Payment> f:
                    ErrorMessage = f.Cause.UserMessage;
                    Status = Estado.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("PagoViewModel",
                $"Excepcion inesperada pagando credito {creditId}", ex);
            ErrorMessage = $"Error inesperado: {ex.Message}";
            Status = Estado.Error;
        }
    }

    // HU8-973 BugFix #5: CanPagar ahora valida tambien que el monto este
    // dentro del rango del credito (entre pago minimo y saldo pendiente).
    private bool CanPagar()
    {
        if (Monto <= 0 || Status == Estado.Loading || CreditoSeleccionado is null)
            return false;

        // Anti-doble-cobro (bloqueo REAL): no permitir un nuevo pago si hay uno
        // reciente sin confirmar para el mismo credito. Esto es lo que faltaba:
        // antes CanPagar solo miraba Loading, asi que el boton seguia tocable.
        if (HasRecentPaymentAttempt())
            return false;

        var min = (decimal)CreditoSeleccionado.MinimumPayment;
        var max = (decimal)CreditoSeleccionado.Balance;

        // Si el cajero quiere pagar mas que el saldo, no dejamos.
        if (Monto > max) return false;

        // Si el monto es menor que el pago minimo, tampoco (es politica del
        // banco). Excepcion: si el minimo es 0, aceptamos cualquier monto > 0.
        if (min > 0 && Monto < min) return false;

        return true;
    }
}
