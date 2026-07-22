using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 4: ingreso del OTP y confirmacion del credito.
///
/// HU8-973 anti-spam:
///   - Throttle de reenvios (cooldown 60s, max 3 por transaccion).
///   - Cooldown entre intentos de verificacion (2s) para evitar rate-limit.
///   - Reset del throttle al confirmar credito (ConfirmacionViewModel.Finalizar).
/// </summary>
public partial class OtpViewModel(
    SistecreditoService service,
    ITransactionStateStore state,
    INavigationService nav,
    ApiConfig config,
    OtpRequestThrottle throttle) : ObservableObject
{
    public enum Estado { Idle, Loading, OtpSent, Error, Done }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private int remainingSeconds;

    [ObservableProperty]
    private int attemptsLeft = 3;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string codigoOtp = string.Empty;

    [ObservableProperty]
    private bool puedeReenviar;

    [ObservableProperty]
    private int resendCooldownSeconds;          // 0 = no hay cooldown activo

    [ObservableProperty]
    private int verifyCooldownSeconds;         // 0 = no hay cooldown activo

    public string Mobile => state.ValidatedClient?.Mobile ?? string.Empty;
    public bool IsLoading => Status == Estado.Loading;
    public bool HasError => Status == Estado.Error;
    public bool AttemptsExhausted => AttemptsLeft <= 0 && Status == Estado.Error;

    /// <summary>
    /// True cuando se agotaron los reenvios permitidos. En ese punto el cajero
    /// ya no puede pedir mas codigos -> se ofrece "Volver a ingresar cedula"
    /// para reiniciar el proceso desde cero.
    /// </summary>
    public bool ReenviosAgotados => throttle.ResendCount >= throttle.MaxResends;

    public bool CanVerify =>
        CodigoOtp.Length == 6
        && Status != Estado.Loading
        && Status != Estado.Done
        && AttemptsLeft > 0
        && VerifyCooldownSeconds <= 0;

    public bool CanResend =>
        PuedeReenviar
        && !IsLoading
        && Status != Estado.Done
        && ResendCooldownSeconds <= 0;

    /// <summary>
    /// Texto del boton Reenviar: muestra countdown cuando el throttle esta activo
    /// o cuando se supero el maximo de reenvios.
    /// </summary>
    public string ResendButtonText
    {
        get
        {
            if (throttle.ResendCount >= throttle.MaxResends)
                return "Reenviar no disponible";
            if (ResendCooldownSeconds > 0)
                return $"Reenviar en {ResendCooldownSeconds}s";
            return "Reenviar codigo";
        }
    }

    public string ResendHelpText
    {
        get
        {
            if (throttle.ResendCount >= throttle.MaxResends)
                return $"Se alcanzo el maximo de {throttle.MaxResends} reenvios.";
            if (ResendCooldownSeconds > 0)
                return $"Espera {ResendCooldownSeconds}s para pedir otro codigo. " +
                       $"({throttle.ResendCount}/{throttle.MaxResends} reenvios usados)";
            if (throttle.ResendCount > 0)
                return $"{throttle.ResendCount}/{throttle.MaxResends} reenvios usados";
            return string.Empty;
        }
    }

    /// <summary>
    /// Tiempo restante en formato mm:ss (ej. "03:40"), mucho mas claro que
    /// "220 s". Si expiro, muestra "expirado".
    /// </summary>
    public string RemainingTimeText
    {
        get
        {
            if (RemainingSeconds <= 0) return "expirado";
            var ts = TimeSpan.FromSeconds(RemainingSeconds);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }
    }

    private int _attempts;
    private DateTime? _lastVerifyAttemptAt;

    // BugFix OTP: el countdown se controla con un flag propio, NO con Status.
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _cooldownCts;

    partial void OnStatusChanged(Estado value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(AttemptsExhausted));
        OnPropertyChanged(nameof(ReenviosAgotados));
    }

    partial void OnRemainingSecondsChanged(int value) =>
        OnPropertyChanged(nameof(RemainingTimeText));

    partial void OnCodigoOtpChanged(string value)
    {
        OnPropertyChanged(nameof(CanVerify));
    }

    partial void OnPuedeReenviarChanged(bool value)
    {
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendButtonText));
        OnPropertyChanged(nameof(ResendHelpText));
    }

    partial void OnResendCooldownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendButtonText));
        OnPropertyChanged(nameof(ResendHelpText));
    }

    partial void OnVerifyCooldownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(CanVerify));
    }

    [RelayCommand]
    public async Task SolicitarAsync()
    {
        var cliente = state.ValidatedClient;
        if (cliente is null) return;

        // HU8-973 throttle: aplicar politica anti-spam ANTES de pegarle a Credinet.
        var decision = throttle.CanRequest();
        switch (decision)
        {
            case OtpResendDecision.Wait w:
                var waitSec = Math.Ceiling(w.SecondsRemaining);
                ErrorMessage = $"Espera {waitSec} segundos antes de pedir otro codigo.";
                AppLogger.I("OtpViewModel", $"Solicitar bloqueado por throttle: {waitSec}s restantes.");
                return;
            case OtpResendDecision.Exceeded:
                ErrorMessage = $"Se alcanzo el maximo de {throttle.MaxResends} reenvios. " +
                               "Toca “Volver a ingresar cedula” para reiniciar el proceso.";
                Status = Estado.Error;
                OnPropertyChanged(nameof(ReenviosAgotados));
                AppLogger.W("OtpViewModel", $"Max resends alcanzado ({throttle.MaxResends}).");
                return;
        }

        Status = Estado.Loading;
        try
        {
            AppLogger.I("OtpViewModel",
                $"Solicitando OTP: creditValue={state.CreditValue}, months={state.Months}, " +
                $"destino={(config.OtpDestination == 1 ? "WhatsApp" : "SMS")}. " +
                $"resendCount={throttle.ResendCount}/{throttle.MaxResends}.");

            var result = await service.SolicitarClaveAsync(
                (double)state.CreditValue, state.Months,
                cliente.DocumentType, cliente.DocumentId, config.OtpDestination);

            switch (result)
            {
                case ApiResult<CreditToken>.Ok<CreditToken> ok:
                    AppLogger.I("OtpViewModel",
                        $"getCreditToken OK: TokenGenerated={ok.Data.TokenGenerated}, " +
                        $"RemainingSeconds={ok.Data.RemainingSeconds}.");
                    if (!ok.Data.TokenGenerated)
                        AppLogger.W("OtpViewModel",
                            "getCreditToken OK pero TokenGenerated=FALSE: posible rate-limit.");

                    CancelCountdown();
                    CancelCooldownTimers();
                    throttle.RecordRequest();
                    OnPropertyChanged(nameof(ReenviosAgotados));
                    RemainingSeconds = ok.Data.RemainingSeconds;
                    Status = Estado.OtpSent;
                    ErrorMessage = null;
                    _attempts = 0;
                    AttemptsLeft = 3;
                    PuedeReenviar = false;
                    ResendCooldownSeconds = (int)throttle.Cooldown.TotalSeconds;
                    StartCountdown(ok.Data.RemainingSeconds);
                    StartResendCooldown();
                    break;
                case ApiResult<CreditToken>.Failure<CreditToken> f:
                    AppLogger.W("OtpViewModel",
                        $"getCreditToken FAILURE: {f.Cause.UserMessage}");
                    ErrorMessage = FriendlyMessage.FromApiError(f.Cause);
                    Status = Estado.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("OtpViewModel", "Excepcion inesperada solicitando OTP", ex);
            ErrorMessage = "Algo salio mal. Intenta de nuevo.";
            Status = Estado.Error;
        }
    }

    [RelayCommand]
    private async Task VerificarAsync()
    {
        if (CodigoOtp.Length != 6) return;

        // Cooldown entre intentos: evita rate-limit de Credinet si el cajero
        // intenta 3 veces seguidas en 2s.
        if (VerifyCooldownSeconds > 0) return;

        var cliente = state.ValidatedClient;
        var saleId  = state.ActiveDocument?.SaleId
                      ?? state.ActiveTransaction?.TransactionId
                      ?? string.Empty;
        if (cliente is null) return;

        Status = Estado.Loading;
        _lastVerifyAttemptAt = DateTime.UtcNow;
        StartVerifyCooldown();
        try
        {
            var result = await service.CrearCreditoAsync(
                saleId, cliente.DocumentType, cliente.DocumentId,
                (double)state.CreditValue, state.Months, CodigoOtp);

            switch (result)
            {
                case ApiResult<Credit>.Ok<Credit> ok:
                    // Confirmar exitoso: resetear throttle para la proxima transaccion.
                    throttle.Reset();
                    CancelCooldownTimers();
                    CancelCountdown();
                    state.SetCreatedCredit(ok.Data);
                    Status = Estado.Done;
                    await nav.GoToConfirmacionAsync(ok.Data);
                    break;
                case ApiResult<Credit>.Failure<Credit> f:
                    _attempts++;
                    AttemptsLeft = Math.Max(0, 3 - _attempts);
                    AppLogger.W("OtpViewModel", $"Verificacion fallida: {f.Cause.UserMessage}");
                    ErrorMessage = BuildAttemptMessage();
                    Status = Estado.Error;
                    PuedeReenviar = AttemptsLeft <= 0 || ResendCooldownSeconds <= 0;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("OtpViewModel", "Excepcion inesperada creando credito", ex);
            _attempts++;
            AttemptsLeft = Math.Max(0, 3 - _attempts);
            ErrorMessage = BuildAttemptMessage() ?? "Algo salio mal. Intenta de nuevo.";
            Status = Estado.Error;
            PuedeReenviar = AttemptsLeft <= 0 || ResendCooldownSeconds <= 0;
        }
    }

    private string BuildAttemptMessage()
    {
        if (AttemptsLeft <= 0)
            return "Se agotaron los intentos. Toca \"Reenviar codigo\" para recibir una clave nueva.";
        var plural = AttemptsLeft == 1 ? "intento" : "intentos";
        var quedan = AttemptsLeft == 1 ? "Te queda" : "Te quedan";
        return $"El codigo no es valido. {quedan} {AttemptsLeft} {plural}.";
    }

    /// <summary>
    /// Reinicia el proceso desde la captura de cedula. Se usa cuando se
    /// agotaron los reenvios (o el cajero quiere empezar de nuevo): limpia el
    /// throttle y los timers y vuelve a la primera pantalla del flujo.
    /// </summary>
    [RelayCommand]
    private async Task VolverACedulaAsync()
    {
        throttle.Reset();
        CancelCountdown();
        CancelCooldownTimers();
        OnPropertyChanged(nameof(ReenviosAgotados));
        try
        {
            await nav.GoToCapturaCedulaAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("OtpViewModel", "Error volviendo a la captura de cedula", ex);
        }
    }

    // -----------------------------------------------------------------
    // Countdown del OTP (tiempo de expiracion)
    // -----------------------------------------------------------------

    private void StartCountdown(int seconds)
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();
        var token = _countdownCts.Token;

        _ = Task.Run(async () =>
        {
            const int resendGraceSeconds = 30;
            var remaining = seconds;
            try
            {
                while (remaining > 0 && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    remaining--;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                            RemainingSeconds = remaining;
                    });

                    var elapsed = seconds - remaining;
                    if (elapsed >= resendGraceSeconds && !PuedeReenviar)
                    {
                        MainThread.BeginInvokeOnMainThread(() => PuedeReenviar = true);
                    }
                }
            }
            catch (TaskCanceledException) { }

            MainThread.BeginInvokeOnMainThread(() => PuedeReenviar = true);
        }, token);
    }

    private void CancelCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _countdownCts = null;
    }

    // -----------------------------------------------------------------
    // Cooldown del boton Reenviar (HU8-973 anti-spam)
    // -----------------------------------------------------------------

    private void StartResendCooldown()
    {
        CancelResendCooldown();
        var total = (int)throttle.Cooldown.TotalSeconds;
        ResendCooldownSeconds = total;
        _cooldownCts = new CancellationTokenSource();
        var token = _cooldownCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (ResendCooldownSeconds > 0 && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested && ResendCooldownSeconds > 0)
                            ResendCooldownSeconds--;
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private void CancelResendCooldown()
    {
        _cooldownCts?.Cancel();
        _cooldownCts?.Dispose();
        _cooldownCts = null;
    }

    // -----------------------------------------------------------------
    // Cooldown entre intentos de verificacion (HU8-973 anti-rate-limit)
    // -----------------------------------------------------------------

    private void StartVerifyCooldown()
    {
        VerifyCooldownSeconds = config.OtpVerifyCooldownSeconds;
        var token = _cooldownCts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                while (VerifyCooldownSeconds > 0 && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested && VerifyCooldownSeconds > 0)
                            VerifyCooldownSeconds--;
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private void CancelCooldownTimers()
    {
        CancelResendCooldown();
        ResendCooldownSeconds = 0;
        VerifyCooldownSeconds = 0;
    }
}