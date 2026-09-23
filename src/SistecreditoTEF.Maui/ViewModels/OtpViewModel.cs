using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 4: ingreso del OTP y creacion del credito.
///
/// Anti-abuso:
///   - Throttle de reenvios (cooldown + maximo por transaccion).
///   - Tope de intentos de verificacion POR TRANSACCION.
///   - Cooldown entre intentos para no gatillar el rate-limit de Credinet.
///
/// QA A-12: el contador de intentos vive en [OtpRequestThrottle] (singleton), no
/// aqui. Antes era un campo de este ViewModel, y los ViewModels son Transient:
/// bastaba navegar atras y volver para obtener uno nuevo con el contador en cero
/// mientras el mismo OTP seguia vigente, o sea que el tope de 3 intentos se
/// reiniciaba a voluntad.
///
/// QA M-8: los tres temporizadores tienen su PROPIO CancellationTokenSource y la
/// clase es IDisposable. Antes el cooldown de verificacion reutilizaba el CTS del
/// cooldown de reenvio (<c>_cooldownCts</c>): cancelar uno mataba el otro, y si
/// era null el bucle quedaba sin poder cancelarse. Ademas, tras el Dispose del
/// CTS, el <c>Task.Delay(1000, token)</c> en vuelo lanzaba
/// ObjectDisposedException, que no estaba capturada y se perdia como excepcion no
/// observada.
/// </summary>
public partial class OtpViewModel(
    SistecreditoService service,
    ITransactionStateStore state,
    INavigationService nav,
    ApiConfig config,
    OtpRequestThrottle throttle) : ObservableObject, IDisposable
{
    public enum Estado { Idle, Loading, OtpSent, Error, Done }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private int remainingSeconds;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string codigoOtp = string.Empty;

    [ObservableProperty]
    private bool puedeReenviar;

    [ObservableProperty]
    private int resendCooldownSeconds;      // 0 = sin cooldown activo

    [ObservableProperty]
    private int verifyCooldownSeconds;      // 0 = sin cooldown activo

    /// <summary>
    /// Aviso cuando Credinet devolvió el mismo código en vez de uno nuevo.
    /// Vacío cuando el código es nuevo. Ver [OtpTokenReuse].
    /// </summary>
    [ObservableProperty]
    private string avisoCodigoReutilizado = string.Empty;

    public bool TieneAvisoCodigoReutilizado => !string.IsNullOrEmpty(AvisoCodigoReutilizado);

    partial void OnAvisoCodigoReutilizadoChanged(string value) =>
        OnPropertyChanged(nameof(TieneAvisoCodigoReutilizado));

    /// <summary>
    /// Tiempo restante que informó la solicitud anterior. Permite detectar la
    /// reutilización del token comparando contadores.
    /// </summary>
    private int? _ultimoRemainingSeconds;

    /// <summary>
    /// True si en esta transacción ya se detectó que el proveedor reutiliza tokens.
    /// Cambia el consejo ante un código quemado: reenviar no sirve, hay que esperar
    /// la expiración.
    /// </summary>
    private bool _proveedorReutilizaTokens;

    /// <summary>Intentos de verificacion restantes (fuente: el throttle singleton).</summary>
    public int AttemptsLeft => throttle.VerifyAttemptsLeft;

    public string Mobile => state.ValidatedClient?.Mobile ?? string.Empty;
    public bool IsLoading => Status == Estado.Loading;
    public bool HasError => Status == Estado.Error;
    public bool AttemptsExhausted => throttle.VerifyAttemptsExhausted && Status == Estado.Error;

    /// <summary>
    /// Canal por el que llega la clave, segun configuracion (QA M-15).
    /// Se muestra al cajero para que le diga bien al cliente donde buscarla.
    /// </summary>
    public string CanalOtp => SistecreditoService.DescribeOtpChannel(config.OtpDestination);

    public string CanalOtpTexto => $"Enviamos la clave por {CanalOtp} al celular del cliente.";

    /// <summary>True cuando se agotaron los reenvios permitidos.</summary>
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
        && !ReenviosAgotados
        && ResendCooldownSeconds <= 0;

    public string ResendButtonText
    {
        get
        {
            // HU-134 (Fase 3): solo un contador visible (el tiempo real de
            // expiracion del OTP, en RemainingTimeText). El boton de reenviar
            // se deshabilita durante el cooldown (CanResend = false) pero el
            // texto del boton NO muestra la cuenta regresiva del cooldown
            // para no confundir al cajero con dos timers simultaneos.
            if (ReenviosAgotados) return "Reenviar no disponible";
            return "Reenviar codigo";
        }
    }

    public string ResendHelpText
    {
        get
        {
            // ─────────────────────────────────────────────────────────────────
            // SIEMPRE SE EXPLICA POR QUE EL BOTON ESTA GRIS
            // ─────────────────────────────────────────────────────────────────
            // Hay DOS mecanismos que deshabilitan el reenvio: el cooldown del
            // throttle y la gracia de 30 s del countdown ([PuedeReenviar]). El
            // caso "cooldown terminado pero sin llegar a los 30 s" no estaba
            // contemplado y devolvia cadena vacia, asi que la etiqueta se ocultaba
            // (IsVisible depende de que el texto no sea vacio) y el boton quedaba
            // deshabilitado SIN NINGUN MENSAJE. El cajero lo tocaba y no pasaba
            // nada, sin forma de saber por que.
            if (ReenviosAgotados)
                return $"Se alcanzo el maximo de {throttle.MaxResends} reenvios. " +
                       "Vuelve a ingresar la cedula para reiniciar el proceso.";
            if (ResendCooldownSeconds > 0)
                return $"Espera {ResendCooldownSeconds} segundos para pedir otro codigo.";
            if (!PuedeReenviar)
                return "Podras pedir otro codigo en unos segundos.";
            if (throttle.ResendCount > 0)
                return $"{throttle.ResendCount}/{throttle.MaxResends} reenvios usados";
            return string.Empty;
        }
    }

    /// <summary>Tiempo restante en mm:ss (ej. "03:40"), o "expirado".</summary>
    public string RemainingTimeText
    {
        get
        {
            if (RemainingSeconds <= 0) return "expirado";
            var ts = TimeSpan.FromSeconds(RemainingSeconds);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }
    }

    // QA M-8: un CTS por temporizador, con vidas independientes.
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _resendCooldownCts;
    private CancellationTokenSource? _verifyCooldownCts;
    private bool _disposed;

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

    partial void OnCodigoOtpChanged(string value) =>
        OnPropertyChanged(nameof(CanVerify));

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

    partial void OnVerifyCooldownSecondsChanged(int value) =>
        OnPropertyChanged(nameof(CanVerify));

    private void NotifyAttempts()
    {
        OnPropertyChanged(nameof(AttemptsLeft));
        OnPropertyChanged(nameof(AttemptsExhausted));
        OnPropertyChanged(nameof(CanVerify));
    }

    // ------------------------------------------------------------------
    // Solicitar / reenviar codigo
    // ------------------------------------------------------------------

    [RelayCommand]
    public async Task SolicitarAsync()
    {
        var cliente = state.ValidatedClient;
        if (cliente is null) return;

        var decision = throttle.CanRequest();
        switch (decision)
        {
            case OtpResendDecision.Wait w:
                var waitSec = Math.Ceiling(w.SecondsRemaining);
                ErrorMessage = $"Espera {waitSec} segundos antes de pedir otro codigo.";
                // Sin marcar el estado, la pantalla no muestra ErrorMessage (depende
                // de HasError) y el rechazo quedaba invisible: otro camino por el que
                // tocar "Reenviar" no producia ningun efecto perceptible.
                Status = Estado.Error;
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
                $"Solicitando OTP: monto={state.CreditValue}, meses={state.Months}, " +
                $"destino={CanalOtp}, reenvios={throttle.ResendCount}/{throttle.MaxResends}.");

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

                    // Credinet puede devolver el MISMO codigo en vez de generar uno
                    // nuevo (ver [OtpTokenReuse]). Si pasa, hay que decirselo al
                    // cajero: si no, se queda esperando un WhatsApp nuevo que no va a
                    // llegar, o reintentando un codigo quemado.
                    var remainingAnterior = _ultimoRemainingSeconds;
                    var reutilizado = OtpTokenReuse.EsElMismoToken(
                        remainingAnterior, ok.Data.RemainingSeconds);
                    _ultimoRemainingSeconds = ok.Data.RemainingSeconds;
                    _proveedorReutilizaTokens |= reutilizado;

                    AvisoCodigoReutilizado = OtpTokenReuse.Aviso(
                        reutilizado, ok.Data.RemainingSeconds);
                    if (reutilizado)
                        AppLogger.W("OtpViewModel",
                            $"Credinet reutilizo el token: el tiempo restante bajo de " +
                            $"{remainingAnterior}s a {ok.Data.RemainingSeconds}s. " +
                            "El cliente NO recibe un codigo nuevo.");

                    CancelAllTimers();
                    // Reinicia tambien los intentos de verificacion: son intentos
                    // contra ESTE codigo.
                    throttle.RecordRequest();
                    NotifyAttempts();
                    OnPropertyChanged(nameof(ReenviosAgotados));

                    RemainingSeconds = ok.Data.RemainingSeconds;
                    Status = Estado.OtpSent;
                    ErrorMessage = null;
                    PuedeReenviar = false;
                    StartCountdown(ok.Data.RemainingSeconds);
                    StartResendCooldown();
                    break;

                case ApiResult<CreditToken>.Failure<CreditToken> f:
                    AppLogger.W("OtpViewModel", $"getCreditToken FAILURE: {f.Cause.UserMessage}");
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

    // ------------------------------------------------------------------
    // Verificar codigo y crear credito
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task VerificarAsync()
    {
        if (CodigoOtp.Length != 6) return;
        if (VerifyCooldownSeconds > 0) return;

        if (throttle.VerifyAttemptsExhausted)
        {
            ErrorMessage = "Se agotaron los intentos. Toca “Reenviar codigo” " +
                           "para recibir una clave nueva.";
            Status = Estado.Error;
            NotifyAttempts();
            return;
        }

        var cliente = state.ValidatedClient;
        if (cliente is null) return;

        var saleId = state.ActiveDocument?.SaleId
                     ?? state.ActiveTransaction?.TransactionId
                     ?? string.Empty;

        Status = Estado.Loading;
        StartVerifyCooldown();
        try
        {
            var result = await service.CrearCreditoAsync(
                saleId, cliente.DocumentType, cliente.DocumentId,
                (double)state.CreditValue, state.Months, CodigoOtp);

            switch (result)
            {
                case ApiResult<Credit>.Ok<Credit> ok:
                    throttle.Reset();
                    CancelAllTimers();
                    state.SetCreatedCredit(ok.Data);
                    Status = Estado.Done;
                    await nav.GoToConfirmacionAsync(ok.Data);
                    break;

                case ApiResult<Credit>.Failure<Credit> f:
                    HandleVerificationFailure(f.Cause);
                    break;
            }
        }
        catch (Exception ex)
        {
            // QA M-9: una excepcion aqui es un fallo de infraestructura, NO un
            // codigo incorrecto. Antes se contaba como intento fallido, asi que
            // tres cortes de red dejaban al cajero con "Se agotaron los intentos"
            // y un OTP perfectamente valido.
            AppLogger.E("OtpViewModel", "Excepcion inesperada creando credito", ex);
            ErrorMessage = "No pudimos confirmar la operacion. Revisa la conexion e intenta " +
                           "de nuevo; el codigo sigue siendo valido.";
            Status = Estado.Error;
            PuedeReenviar = ResendCooldownSeconds <= 0;
        }
    }

    /// <summary>
    /// Decide si el fallo consume un intento. Solo el rechazo de NEGOCIO
    /// (codigo invalido/expirado) lo hace; red y HTTP no (QA M-9).
    /// </summary>
    private void HandleVerificationFailure(ApiError cause)
    {
        // Un codigo MUERTO (ya usado o expirado) no se arregla reintentando: hay
        // que pedir otro. Consumir intentos aca no protege de nada —el riesgo de
        // fuerza bruta es sobre un codigo VIVO— y solo deja al cajero sin intentos
        // para el codigo nuevo.
        //
        // Confirmado en el terminal: se vieron 8 intentos seguidos contra el mismo
        // codigo, todos con errorCode 230 (TokenAlreadyUsed), porque el mensaje que
        // veia el cajero era "error de comunicacion con el servidor" y no le decia
        // que el codigo estaba quemado.
        if (EsCodigoMuerto(cause))
        {
            AppLogger.W("OtpViewModel",
                $"El codigo esta quemado ({cause.UserMessage}); se habilita el reenvio " +
                $"sin consumir intentos. Proveedor reutiliza tokens: {_proveedorReutilizaTokens}.");

            // Si ya vimos que el proveedor devuelve el mismo codigo, mandar a
            // "Reenviar" seria un consejo falso: devolveria el mismo codigo quemado.
            ErrorMessage = _proveedorReutilizaTokens
                ? "Ese codigo ya fue usado, y Sistecredito esta devolviendo el mismo " +
                  "codigo en vez de generar uno nuevo. Hay que esperar a que expire " +
                  "para poder continuar con esta cedula."
                : FriendlyMessage.FromApiError(cause);

            Status = Estado.Error;
            RemainingSeconds = 0;
            PuedeReenviar = true;
            OnPropertyChanged(nameof(CanResend));
            return;
        }

        var esCodigoInvalido = cause is ApiError.Business;

        if (esCodigoInvalido)
        {
            var left = throttle.RecordFailedVerification();
            NotifyAttempts();
            AppLogger.W("OtpViewModel",
                $"Verificacion fallida (codigo invalido). Intentos restantes: {left}. " +
                $"Detalle: {cause.UserMessage}");

            ErrorMessage = left <= 0
                ? "Se agotaron los intentos. Toca “Reenviar codigo” para recibir una clave nueva."
                : $"{FriendlyMessage.FromApiError(cause)} " +
                  $"({(left == 1 ? "Te queda 1 intento" : $"Te quedan {left} intentos")}.)";
        }
        else
        {
            AppLogger.E("OtpViewModel",
                $"Verificacion fallida por infraestructura (no consume intento): {cause.UserMessage}");
            ErrorMessage = FriendlyMessage.FromApiError(cause) +
                           " El codigo sigue siendo valido.";
        }

        Status = Estado.Error;
        PuedeReenviar = throttle.VerifyAttemptsExhausted || ResendCooldownSeconds <= 0;
    }

    /// <summary>
    /// True si el código ya no sirve y hay que pedir uno nuevo (usado o expirado).
    /// Se detecta por el errorCode y, como respaldo, por el mensaje de Credinet,
    /// para no depender de un único código si el proveedor agrega variantes.
    /// </summary>
    private static bool EsCodigoMuerto(ApiError cause)
    {
        if (cause is not ApiError.Business business) return false;

        // 230 = TokenAlreadyUsed (confirmado en el terminal).
        if (business.Code == 230) return true;

        var msg = business.Message ?? string.Empty;
        return msg.Contains("TokenAlreadyUsed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TokenExpired", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reinicia el proceso desde la captura de cedula (cuando se agotaron los
    /// reenvios o el cajero quiere empezar de nuevo).
    /// </summary>
    [RelayCommand]
    private async Task VolverACedulaAsync()
    {
        throttle.Reset();
        CancelAllTimers();
        NotifyAttempts();
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

    // ------------------------------------------------------------------
    // Temporizadores (QA M-8: uno por responsabilidad)
    // ------------------------------------------------------------------

    private void StartCountdown(int seconds)
    {
        Replace(ref _countdownCts, out var token);

        RunTimer(token, async () =>
        {
            const int resendGraceSeconds = 30;
            var remaining = seconds;

            while (remaining > 0 && !token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                remaining--;
                var snapshot = remaining;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!token.IsCancellationRequested) RemainingSeconds = snapshot;
                });

                if (seconds - remaining >= resendGraceSeconds && !PuedeReenviar)
                    MainThread.BeginInvokeOnMainThread(() => PuedeReenviar = true);
            }

            if (!token.IsCancellationRequested)
                MainThread.BeginInvokeOnMainThread(() => PuedeReenviar = true);
        });
    }

    private void StartResendCooldown()
    {
        Replace(ref _resendCooldownCts, out var token);
        ResendCooldownSeconds = (int)throttle.Cooldown.TotalSeconds;

        RunTimer(token, async () =>
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
        });
    }

    private void StartVerifyCooldown()
    {
        Replace(ref _verifyCooldownCts, out var token);
        VerifyCooldownSeconds = config.OtpVerifyCooldownSeconds;

        RunTimer(token, async () =>
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
        });
    }

    /// <summary>
    /// Cancela y reemplaza un CTS, devolviendo el token nuevo. Concentra el
    /// patron para que ningun temporizador use el CTS de otro.
    /// </summary>
    private static void Replace(ref CancellationTokenSource? cts, out CancellationToken token)
    {
        var old = cts;
        var fresh = new CancellationTokenSource();
        cts = fresh;
        token = fresh.Token;

        // Se cancela DESPUES de publicar el nuevo, y el Dispose se posterga: si
        // se libera aca, el bucle en vuelo revienta con ObjectDisposedException.
        if (old is not null)
        {
            try { old.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Ejecuta un bucle de temporizador atrapando TODAS las excepciones
    /// esperables. Antes solo se capturaba TaskCanceledException y una
    /// ObjectDisposedException quedaba como excepcion no observada.
    /// </summary>
    private static void RunTimer(CancellationToken token, Func<Task> body)
    {
        // [Fire.AndForget] ya trata la cancelacion como salida normal y registra
        // cualquier otra excepcion. Solo queda aparte la ObjectDisposedException,
        // que aca es esperada: el CTS puede liberarse mientras corre el delay.
        Fire.AndForget(async () =>
        {
            try
            {
                await body();
            }
            catch (ObjectDisposedException) { /* CTS liberado durante el delay */ }
        }, "OtpViewModel");
    }

    private void CancelAllTimers()
    {
        Cancel(ref _countdownCts);
        Cancel(ref _resendCooldownCts);
        Cancel(ref _verifyCooldownCts);
        ResendCooldownSeconds = 0;
        VerifyCooldownSeconds = 0;
    }

    private static void Cancel(ref CancellationTokenSource? cts)
    {
        var local = cts;
        cts = null;
        if (local is null) return;
        try { local.Cancel(); } catch (ObjectDisposedException) { }
        local.Dispose();
    }

    /// <summary>
    /// QA (warning CA1001): la clase tenia campos IDisposable y no era
    /// IDisposable, asi que cada navegacion a la pantalla de OTP filtraba dos
    /// CancellationTokenSource.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAllTimers();
        GC.SuppressFinalize(this);
    }
}
