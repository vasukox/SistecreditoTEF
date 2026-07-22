using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 3: el cajero ingresa el monto + plazo y ve la cuota simulada.
/// </summary>
public partial class SeleccionCuotasViewModel(
    SistecreditoService service,
    ITransactionStateStore state,
    INavigationService nav) : ObservableObject
{
    public enum EstadoSimulacion { Idle, Loading, Success, Error }
    public enum EstadoLimite     { Idle, Loading, Success, Error }

    public static readonly int[] CandidateMonths = { 1, 2, 3, 6, 9, 12, 18, 24 };

    [ObservableProperty]
    private EstadoSimulacion status = EstadoSimulacion.Idle;

    [ObservableProperty]
    private EstadoLimite limitStatus = EstadoLimite.Idle;

    [ObservableProperty]
    private CreditDetails? detalles;

    [ObservableProperty]
    private int maxMonths;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int mesesSeleccionados = 12;

    [ObservableProperty]
    private decimal monto = 0m;

    public string ClienteNombre  => state.ValidatedClient?.FullName ?? "(cliente)";
    public string CupoTotal      => state.ValidatedClient?.CreditLimit.ToColombianCurrency() ?? "$ 0";
    public string CupoDisponible => state.ValidatedClient?.AvailableCreditLimit.ToColombianCurrency() ?? "$ 0";
    public string CuotaMensual   => Detalles?.TotalFeeValue.ToColombianCurrency() ?? "$ 0";
    public string TotalPagar     => Detalles?.TotalPaymentValue.ToColombianCurrency() ?? "$ 0";
    public string Intereses      => Detalles?.TotalInterestValue.ToColombianCurrency() ?? "$ 0";
    public string Aval           => Detalles?.AssuranceValue.ToColombianCurrency() ?? "$ 0";
    public string CuotaInicial   => Detalles?.TotalDownPayment.ToColombianCurrency() ?? "$ 0";

    public bool IsLoading => Status == EstadoSimulacion.Loading;
    public bool HasResults => Status == EstadoSimulacion.Success && Detalles is not null;
    public bool HasError => Status == EstadoSimulacion.Error;

    partial void OnDetallesChanged(CreditDetails? value)
    {
        OnPropertyChanged(nameof(CuotaMensual));
        OnPropertyChanged(nameof(TotalPagar));
        OnPropertyChanged(nameof(Intereses));
        OnPropertyChanged(nameof(Aval));
        OnPropertyChanged(nameof(CuotaInicial));
        OnPropertyChanged(nameof(HasResults));
    }

    partial void OnStatusChanged(EstadoSimulacion value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasResults));
    }

    public IReadOnlyList<int> PlazosDisponibles =>
        LimitStatus == EstadoLimite.Success
            ? CandidateMonths.Where(m => m <= MaxMonths).ToArray()
            : CandidateMonths;

    /// <summary>
    /// HU-134: precarga el monto con lo facturado en HioPos (venta SALE) para
    /// que el cajero no lo digite. Prioriza el Amount del Intent (centavos);
    /// si no viene, usa el Total del documento de venta. Deja el valor
    /// editable por si el cajero necesita ajustarlo.
    /// </summary>
    public void Inicializar()
    {
        // Respeta un valor ya ingresado (p.ej. al volver a esta pantalla).
        if (Monto > 0) return;

        var pesos = MoneyConverter.FromCentsToPesos(state.ActiveTransaction?.AmountCents);
        var precargado = pesos.HasValue
            ? (decimal)pesos.Value
            : state.ActiveDocument?.Total ?? 0m;

        if (precargado > 0)
            Monto = precargado;
    }

    [RelayCommand]
    private async Task CalcularAsync()
    {
        var cliente = state.ValidatedClient;
        if (cliente is null || Monto <= 0 || MesesSeleccionados <= 0) return;

        Status = EstadoSimulacion.Loading;
        state.CreditValue = Monto;
        state.Months = MesesSeleccionados;

        try
        {
            var result = await service.SimularAsync(
                (double)Monto, MesesSeleccionados, cliente.DocumentType, cliente.DocumentId);

            switch (result)
            {
                case ApiResult<CreditDetails>.Ok<CreditDetails> ok:
                    Detalles = ok.Data;
                    Status = EstadoSimulacion.Success;
                    break;
                case ApiResult<CreditDetails>.Failure<CreditDetails> f:
                    ErrorMessage = f.Cause.UserMessage;
                    Status = EstadoSimulacion.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("SeleccionCuotasViewModel",
                $"Excepcion inesperada simulando credito", ex);
            ErrorMessage = $"Error inesperado: {ex.Message}";
            Status = EstadoSimulacion.Error;
        }
    }

    [RelayCommand]
    private async Task ContinuarAsync()
    {
        if (Detalles is null) return;
        await nav.GoToOtpAsync();
    }

    /// <summary>
    /// Llama desde el code-behind cuando cambia el Entry de monto.
    /// Debounce 500ms para no spamear la API.
    /// </summary>
    public async Task OnMontoChangedAsync(decimal monto)
    {
        CancellationTokenSource? localCts = null;
        try
        {
            await Task.Delay(500);
            if (monto <= 0) return;

            LimitStatus = EstadoLimite.Loading;
            var result = await service.ObtenerLimiteMesesAsync((double)monto);
            switch (result)
            {
                case ApiResult<SimulatedMonthLimit>.Ok<SimulatedMonthLimit> ok:
                    MaxMonths = ok.Data.Months;
                    LimitStatus = EstadoLimite.Success;
                    OnPropertyChanged(nameof(PlazosDisponibles));
                    // HU8-973: si el plazo actual quedo fuera de los permitidos para
                    // este monto, seleccionar el maximo disponible; asi los chips
                    // siempre muestran una opcion valida marcada.
                    if (!PlazosDisponibles.Contains(MesesSeleccionados))
                        MesesSeleccionados = PlazosDisponibles.LastOrDefault();
                    break;
                case ApiResult<SimulatedMonthLimit>.Failure<SimulatedMonthLimit> f:
                    ErrorMessage = f.Cause.UserMessage;
                    LimitStatus = EstadoLimite.Error;
                    break;
            }
        }
        catch (TaskCanceledException) { /* debounced */ }
        catch (Exception ex)
        {
            AppLogger.E("SeleccionCuotasViewModel",
                "Excepcion inesperada calculando limite de meses", ex);
            ErrorMessage = $"Error inesperado: {ex.Message}";
            LimitStatus = EstadoLimite.Error;
        }
    }
}
