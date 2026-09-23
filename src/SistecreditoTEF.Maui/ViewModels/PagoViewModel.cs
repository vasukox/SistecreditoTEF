using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 7: abono a un credito activo.
///
/// QA C-5: la barrera anti-doble-cobro ya no es solo un guard en memoria. La
/// idempotencia real vive en [SistecreditoService.PagarCreditoAsync], que
/// persiste el intento en la BD cifrada ANTES de llamar a Credinet, asi que
/// sobrevive a un reinicio del POS. Este ViewModel solo interpreta el
/// [PaymentOutcome] y le dice al cajero que hacer.
/// </summary>
public partial class PagoViewModel(
    SistecreditoService service,
    INavigationService nav,
    ITransactionStateStore state,
    ISesionCajero sesion) : ObservableObject
{
    public enum Estado { Idle, Loading, Success, Error, EnDuda }

    [ObservableProperty]
    private Estado status = Estado.Idle;

    [ObservableProperty]
    private string? errorMessage;

    // Sin NotifyCanExecuteChangedFor el boton "Pagar" no re-evaluaba CanExecute
    // al escribir el monto y quedaba deshabilitado (tap = "no pasa nada").
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PagarCommand))]
    private decimal monto = 0m;

    /// <summary>
    /// Texto del campo de monto, con separador de miles ("50.000").
    ///
    /// El Entry se bindea a ESTE string, no al decimal, a proposito: el punto es
    /// separador de MILES en es-CO y separador DECIMAL en cultura invariante, asi
    /// que reparsear "50.000" desde un binding podria dar 50 en vez de 50.000
    /// —cobrar mil veces menos, en silencio, segun como este configurado el POS—.
    /// Ver [MoneyInput].
    /// </summary>
    [ObservableProperty]
    private string montoTexto = string.Empty;

    /// <summary>
    /// Lee el valor a partir de los DIGITOS del texto. No reescribe el texto.
    ///
    /// Esta separacion no es estilistica: reescribir el texto desde aca CRASHEABA la
    /// app mientras el cajero escribia. Al borrar un digito el reformateo dejaba el
    /// texto mas corto ("5.000" -> "5.00" -> "500") y el Entry de Android intentaba
    /// restaurar una posicion de cursor que ya no existia.
    ///
    /// El formato visible lo pone [MoneyEntryBehavior], que si controla el cursor.
    /// Aca solo se interpreta el valor.
    /// </summary>
    partial void OnMontoTextoChanged(string value) => Monto = MoneyInput.Parse(value);

    /// <summary>
    /// Escribe el monto en el campo, ya formateado. Lo usan los atajos y la
    /// precarga, para que el texto visible y el valor cobrado siempre coincidan.
    ///
    /// Es seguro asignar el texto desde aca: no ocurre en medio de una edicion del
    /// cajero, y el behavior reubica el cursor cuando el texto se acorta.
    /// </summary>
    private void FijarMonto(decimal valor)
    {
        var entero = Math.Round(valor, MidpointRounding.AwayFromZero);
        MontoTexto = entero > 0 ? MoneyInput.FormatValue(entero) : string.Empty;
        Monto = entero;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PagarCommand))]
    private ActiveCredit? creditoSeleccionado;

    /// <summary>
    /// True cuando hay un abono cuyo resultado no se conoce. La UI debe pedirle
    /// al cajero que verifique el saldo antes de reintentar (QA A-10).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PagarCommand))]
    private bool requiereVerificacion;

    public string Titulo    => CreditoSeleccionado is null
        ? "Abono"
        : $"Crédito #{CreditoSeleccionado.CreditNumber}";
    public string Subtitulo => CreditoSeleccionado is null
        ? string.Empty
        : CreditoSeleccionado.StoreName;

    // ══════════════════════════════════════════════════════════════════════
    // CLARIDAD DE LOS MONTOS
    // ══════════════════════════════════════════════════════════════════════
    // El cajero veía un monto en la lista de créditos y otro distinto al entrar a
    // cobrar. El motivo: la lista mostraba `FeeValue` rotulado "Cuota", y esta
    // pantalla precargaba `MinimumPayment` — dos campos DIFERENTES de Credinet.
    //
    // Ahora las dos pantallas muestran los MISMOS tres importes con los MISMOS
    // rótulos (saldo, pago mínimo, cuota), y acá el monto que se va a cobrar se
    // elige explícitamente y se confirma en el botón. Nada queda implícito.

    /// <summary>Saldo total pendiente del crédito.</summary>
    public string SaldoTexto => CreditoSeleccionado?.Balance.ToColombianCurrency() ?? "$ 0";

    /// <summary>
    /// Pago mínimo TAL COMO lo reporta Credinet.
    ///
    /// Se muestra el valor crudo, sin transformarlo: la lista de créditos muestra
    /// este mismo número, y tener la misma etiqueta con dos valores distintos en dos
    /// pantallas es justo la confusión que hay que evitar. Si el módulo necesita
    /// ajustar algo, lo dice en un aviso aparte — no cambia el número en silencio.
    /// </summary>
    public string MinimoTexto => CreditoSeleccionado is null
        ? "$ 0"
        : ((double)MinimoReportado).ToColombianCurrency();

    /// <summary>
    /// Explica cuando el mínimo reportado excede el saldo de capital, que a primera
    /// vista parece un error del sistema y no lo es.
    /// </summary>
    public bool MinimoExcedeSaldo => Saldo > 0 && MinimoReportado > Saldo;

    public string AvisoMinimoExcedeSaldo => MinimoExcedeSaldo
        ? "El mínimo del mes es mayor al saldo de capital porque incluye intereses y " +
          "aval. No es un error: si el cliente quiere cerrar el crédito, usá " +
          "“Saldar todo”."
        : string.Empty;

    /// <summary>
    /// Rango de referencia con los cuatro importes que reporta Credinet, para que el
    /// cajero vea de dónde sale cada número.
    /// </summary>
    public string CuotaDetalle => CreditoSeleccionado is null
        ? string.Empty
        : $"Cuota del plan: {CuotaTexto} · Saldar todo: {TotalTexto}";

    /// <summary>Valor de la cuota pactada (puede diferir del pago mínimo si hay mora).</summary>
    public string CuotaTexto => CreditoSeleccionado?.FeeValue.ToColombianCurrency() ?? "$ 0";

    public string VencimientoTexto => CreditoSeleccionado?.DueDateDisplay ?? "-";

    /// <summary>True si el crédito está en mora: el cajero tiene que saberlo.</summary>
    public bool TieneMora => CreditoSeleccionado is { ArrearsDays: > 0 };

    public string MoraTexto => CreditoSeleccionado is { ArrearsDays: > 0 } c
        ? $"Crédito en mora: {c.ArrearsDays} {(c.ArrearsDays == 1 ? "día" : "días")}"
        : string.Empty;

    /// <summary>
    /// Explica por qué el pago mínimo no coincide con la cuota, cuando difieren.
    /// Sin esto, ver dos números distintos parece un error del sistema.
    /// </summary>
    public bool MinimoDifiereDeCuota =>
        CreditoSeleccionado is { } cr
        && Math.Abs(cr.MinimumPayment - cr.FeeValue) >= 1;

    public string ExplicacionMinimo => MinimoDifiereDeCuota
        ? "El pago mínimo no coincide con la cuota porque incluye intereses o mora."
        : string.Empty;

    // ══════════════════════════════════════════════════════════════════════
    // SEMÁNTICA DE LOS IMPORTES DE CREDINET
    // ══════════════════════════════════════════════════════════════════════
    // El manual de Credinet define creditValue, creditId, creditNumber y
    // creditLimit, pero NO define balance, minimumPayment, totalPayment ni
    // feeValue. Lo que sigue se INFIRIÓ de datos reales de dos créditos, y
    // conviene confirmarlo con Sistecrédito:
    //
    //   Campo           #583 ($500k, 3 cuotas)   #584 ($50k, 1 cuota)
    //   creditValue          500.000                  50.000
    //   balance              500.000                  50.000    = capital pendiente
    //   feeValue             172.990                  50.943    = valor de UNA cuota
    //   minimumPayment       183.707                  50.000    = mínimo de este mes
    //   totalPayment         500.000                  50.000    = saldar todo de una
    //
    // El dato decisivo es el #584: recién creado, sin mora y sin amortizar nada,
    // su feeValue (50.943) YA SUPERA su balance (50.000). Pagar en cuotas cuesta
    // más que saldar de una, así que feeValue y balance miden cosas distintas y
    // compararlos para validar produce bloqueos falsos.
    //
    // De ahí la regla del módulo: mostrar los cuatro números tal como llegan, no
    // deducir límites de su comparación, y dejar que Credinet valide el monto.

    public string BotonPagoMinimo => $"Mínimo del mes · {MinimoTexto}";

    /// <summary>
    /// Monto para saldar el crédito de una vez. Se usa <c>totalPayment</c>, que en
    /// los datos observados coincide con el capital pendiente; si viniera en cero se
    /// cae al <c>balance</c>.
    /// </summary>
    public string BotonSaldarTodo => $"Saldar todo · {TotalTexto}";

    public string TotalTexto =>
        ((double)(TotalReportado > 0 ? TotalReportado : Saldo)).ToColombianCurrency();

    /// <summary>
    /// Confirmación de lo que se va a cobrar, en el propio botón. Es la última
    /// oportunidad de que el cajero note un monto equivocado antes de cobrar.
    /// </summary>
    public string TextoBotonPagar => Monto > 0
        ? $"Cobrar {Monto.ToColombianCurrency()}"
        : "Ingresa el monto a cobrar";

    public bool IsLoading => Status == Estado.Loading;
    public bool HasError => Status == Estado.Error || Status == Estado.EnDuda;

    /// <summary>Rango válido, explícito.</summary>
    public string MontoHint
    {
        get
        {
            if (CreditoSeleccionado is null) return string.Empty;
            return $"Mínimo {MinimoTexto} · Máximo {SaldoTexto}";
        }
    }

    [RelayCommand]
    private void UsarPagoMinimo()
    {
        if (CreditoSeleccionado is null) return;
        // El mínimo REPORTADO, sin acotar: es el número que el cajero ve en la lista
        // y en el botón, así que precargar otro valor sería incoherente.
        FijarMonto(MinimoReportado);
    }

    [RelayCommand]
    private void UsarSaldarTodo()
    {
        if (CreditoSeleccionado is null) return;
        FijarMonto(TotalReportado > 0 ? TotalReportado : Saldo);
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
        OnPropertyChanged(nameof(CuotaTexto));
        OnPropertyChanged(nameof(VencimientoTexto));
        OnPropertyChanged(nameof(TieneMora));
        OnPropertyChanged(nameof(MoraTexto));
        OnPropertyChanged(nameof(MinimoDifiereDeCuota));
        OnPropertyChanged(nameof(ExplicacionMinimo));
        OnPropertyChanged(nameof(BotonPagoMinimo));
        OnPropertyChanged(nameof(BotonSaldarTodo));
        OnPropertyChanged(nameof(TotalTexto));
        OnPropertyChanged(nameof(CuotaDetalle));
        OnPropertyChanged(nameof(MontoHint));

        // Se precarga el mínimo del mes por comodidad, pero la pantalla dice
        // explícitamente cuánto se va a cobrar (en el botón) y ofrece elegir entre
        // el mínimo y saldar todo, así el monto nunca queda implícito.
        OnPropertyChanged(nameof(AvisoMinimoExcedeSaldo));
        OnPropertyChanged(nameof(MinimoExcedeSaldo));

        if (value is not null && Monto <= 0)
            FijarMonto(MinimoReportado);
    }

    partial void OnMontoChanged(decimal value)
    {
        OnPropertyChanged(nameof(MontoHint));
        OnPropertyChanged(nameof(TextoBotonPagar));
        OnPropertyChanged(nameof(AdvertenciaMonto));
        OnPropertyChanged(nameof(TieneAdvertenciaMonto));
        // Cambiar el monto invalida el bloqueo por "en duda": es otro cobro.
        if (RequiereVerificacion) RequiereVerificacion = false;
    }

    public void Inicializar()
    {
        CreditoSeleccionado = state.SelectedCredit;
        RequiereVerificacion = false;

        // Aviso temprano si hay un intento reciente en memoria. El bloqueo real
        // y persistente lo aplica el servicio al momento de cobrar.
        if (HasRecentPaymentAttempt())
        {
            ErrorMessage = "Ya registramos un intento de abono reciente para este credito. " +
                           "Verifica el saldo antes de volver a cobrar.";
            Status = Estado.Error;
        }

        PagarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Guard en memoria (rapido, misma sesion). La barrera que sobrevive al
    /// reinicio del proceso esta en el servicio.
    /// </summary>
    /// <summary>
    /// Primer valor que no sea null NI blanco. Existe porque <c>??</c> solo mira
    /// null: con una cadena vacia la cadena de respaldos se cortaba en el primer
    /// eslabon y el valor vacio seguia viaje. Ver el uso en [PagarAsync].
    /// </summary>
    private static string PrimeroNoVacio(params string?[] candidatos)
    {
        foreach (var c in candidatos)
            if (!string.IsNullOrWhiteSpace(c))
                return c.Trim();

        return string.Empty;
    }

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

        state.LastPaymentAttemptCreditId = creditId;
        state.LastPaymentAttemptAt       = DateTime.UtcNow;

        Status = Estado.Loading;
        ErrorMessage = null;

        try
        {
            // QUIEN COBRO, en orden de confiabilidad:
            //   1. El cajero IDENTIFICADO en la app. En los abonos manuales es el
            //      unico dato real: la operacion se hace fuera de HioPos, asi que no
            //      hay SellerData, y antes todos los recaudos quedaban en Credinet
            //      como "Cajero Permoda" — sin forma de saber quien cobro.
            //   2. El SellerData de HioPos, si el abono vino de una entrada de caja.
            //   3. El respaldo fijo, que ya no deberia usarse nunca.
            //
            // ─────────────────────────────────────────────────────────────────
            // POR QUE [NombreVisible] Y NO [Nombre], Y POR QUE NO ALCANZA "??"
            // ─────────────────────────────────────────────────────────────────
            // Esto rompio los abonos en produccion: Credinet devolvia HTTP 400
            // "[REP-E-003] El campo UserName es obligatorio en la peticion" y NINGUN
            // recaudo se podia cobrar.
            //
            // La causa: desde el rediseño del ingreso, el cajero se crea con
            // USUARIO obligatorio y NOMBRE opcional (ver [AdminCajerosViewModel]).
            // Un cajero dado de alta solo con usuario tiene Nombre = "", que NO es
            // null — asi que "??" no se disparaba nunca y salia userName vacio hacia
            // Credinet. El bug se veia como una falla del proveedor, no como algo
            // nuestro.
            //
            // [Cajero.NombreVisible] ya resuelve exactamente esto (cae al usuario si
            // no hay nombre), y los tres niveles se filtran por blanco —no por
            // null— para que un string vacio de cualquier origen no vuelva a pasar.
            var userName = PrimeroNoVacio(
                sesion.Actual?.NombreVisible,
                SistecreditoService.ExtractSellerName(state.ActiveTransaction?.SellerData),
                "Cajero Permoda");

            var outcome = await service.PagarCreditoAsync(creditId, Monto, userName);

            switch (outcome)
            {
                case PaymentOutcome.Ok ok:
                    await CompletarAsync(ok.Payment);
                    break;

                case PaymentOutcome.AlreadyPaid already:
                    // No se cobro de nuevo: se muestra el comprobante del abono
                    // que ya existia.
                    AppLogger.W("PagoViewModel",
                        $"El abono ya estaba registrado (creditId={creditId}); se reimprime.");
                    await CompletarAsync(already.Payment);
                    break;

                case PaymentOutcome.InDoubt:
                    RequiereVerificacion = true;
                    Status = Estado.EnDuda;
                    ErrorMessage =
                        "Hay un abono de este credito por el mismo monto cuyo resultado no " +
                        "pudimos confirmar. NO vuelvas a cobrar: consulta el saldo del credito " +
                        "y, si el pago ya quedo aplicado, entrega el comprobante desde " +
                        "Sistecredito.";
                    break;

                case PaymentOutcome.NetworkUncertain uncertain:
                    RequiereVerificacion = true;
                    Status = Estado.EnDuda;
                    AppLogger.E("PagoViewModel",
                        $"Abono en duda (key={uncertain.PaymentKey}) para creditId={creditId}.");
                    ErrorMessage =
                        "Se perdio la conexion mientras se registraba el abono, asi que puede " +
                        "haber quedado aplicado. NO vuelvas a cobrar: verifica el saldo del " +
                        "credito antes de reintentar.";
                    break;

                case PaymentOutcome.Failure failure:
                    ErrorMessage = FriendlyMessage.FromApiError(failure.Error);
                    Status = Estado.Error;
                    // Rechazo de negocio: Credinet NO cobro, se puede reintentar.
                    state.LastPaymentAttemptCreditId = null;
                    state.LastPaymentAttemptAt = null;
                    break;
            }
        }
        catch (Exception ex)
        {
            // QA M-16: no se expone ex.Message al cajero (podia traer host,
            // rutas o detalles de red en la pantalla del POS frente al cliente).
            AppLogger.E("PagoViewModel",
                $"Excepcion inesperada pagando credito {creditId}", ex);
            RequiereVerificacion = true;
            Status = Estado.EnDuda;
            ErrorMessage = "Ocurrio un error inesperado y no sabemos si el abono se aplico. " +
                           "Verifica el saldo del credito antes de reintentar.";
        }
        finally
        {
            PagarCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task CompletarAsync(Payment payment)
    {
        state.SetLastPayment(payment);
        Status = Estado.Success;
        state.LastPaymentAttemptCreditId = null;
        state.LastPaymentAttemptAt = null;
        await nav.GoToReciboPagoAsync(payment);
    }

    /// <summary>
    /// Motivo por el que el botón de cobrar está deshabilitado, o cadena vacía si
    /// está habilitado. Se muestra al cajero: un botón gris sin explicación es una
    /// de las peores experiencias posibles en un POS.
    /// </summary>
    [ObservableProperty]
    private string motivoBloqueo = string.Empty;

    public bool TieneMotivoBloqueo => !string.IsNullOrEmpty(MotivoBloqueo);

    partial void OnMotivoBloqueoChanged(string value) =>
        OnPropertyChanged(nameof(TieneMotivoBloqueo));

    private bool CanPagar()
    {
        var motivo = EvaluarBloqueo();

        // Se registra y se publica el motivo, para que el cajero lo vea en pantalla
        // y para que quede en el log si hay que diagnosticarlo en un terminal.
        if (MotivoBloqueo != motivo)
        {
            MotivoBloqueo = motivo;
            if (motivo.Length > 0)
                AppLogger.I("PagoViewModel",
                    $"Cobro deshabilitado: {motivo} " +
                    $"(monto={Monto}, minimo={CreditoSeleccionado?.MinimumPayment}, " +
                    $"saldo={CreditoSeleccionado?.Balance}, status={Status}, " +
                    $"requiereVerificacion={RequiereVerificacion})");
        }

        return motivo.Length == 0;
    }

    /// <summary>
    /// Devuelve el motivo del bloqueo en texto para el cajero, o vacío si se puede
    /// cobrar. Separado de [CanPagar] para poder explicarlo sin duplicar reglas.
    /// </summary>
    private string EvaluarBloqueo()
    {
        if (CreditoSeleccionado is null) return "No hay un crédito seleccionado.";

        // Mientras el cobro esta en vuelo, el boton se DESHABILITA.
        //
        // Antes esto devolvia cadena vacia, que para [CanPagar] significa "no hay
        // motivo de bloqueo": el boton quedaba habilitado durante todo el POST. Y
        // como este return corta antes de [HasRecentPaymentAttempt], tambien se
        // salteaba la barrera del minuto. Un segundo toque —cosa normal en un POS
        // lento— disparaba un segundo payCredit.
        //
        // No se duplicaba el cobro porque la idempotencia persistida lo atrapaba,
        // pero esa es la ultima red, no la primera: un doble toque legitimo
        // terminaba en un estado "en duda" que obligaba al cajero a ir a verificar
        // el saldo por algo que nunca fue ambiguo.
        if (Status == Estado.Loading) return "Procesando el cobro...";

        // Un abono en duda bloquea hasta que se verifique o se cambie el monto.
        if (RequiereVerificacion)
            return "Verifica el saldo del crédito antes de volver a cobrar.";

        if (HasRecentPaymentAttempt())
            return "Ya se registró un intento de cobro para este crédito hace menos de un minuto.";

        if (Monto <= 0) return "Ingresa el monto a cobrar.";

        // Techo ABSOLUTO: el mayor de los importes que reporta Credinet.
        // Por encima de eso es, con seguridad, un error de tipeo.
        if (Monto > TechoAbsoluto)
            return $"El monto supera lo que Sistecredito reporta para este credito " +
                   $"({((double)TechoAbsoluto).ToColombianCurrency()}).";

        return string.Empty;
    }

    /// <summary>Saldo pendiente que reporta Credinet.</summary>
    private decimal Saldo => (decimal)(CreditoSeleccionado?.Balance ?? 0);

    /// <summary>Pago mínimo que reporta Credinet.</summary>
    private decimal MinimoReportado => (decimal)(CreditoSeleccionado?.MinimumPayment ?? 0);

    /// <summary>Cuota que reporta Credinet.</summary>
    private decimal CuotaReportada => (decimal)(CreditoSeleccionado?.FeeValue ?? 0);

    /// <summary>Total a pagar que reporta Credinet.</summary>
    private decimal TotalReportado => (decimal)(CreditoSeleccionado?.TotalPayment ?? 0);

    /// <summary>
    /// Techo del monto: el MAYOR de los importes que reporta Credinet.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUÉ NO SE VALIDA CONTRA EL SALDO NI CONTRA EL MÍNIMO
    /// ─────────────────────────────────────────────────────────────────────────
    /// Observado en un crédito real del terminal:
    ///
    ///   saldo pendiente  181.580   ← MENOR que una cuota
    ///   cuota            185.004
    ///   pago mínimo      203.781
    ///   vence 23/09/2026 (sin mora)
    ///
    /// El saldo es menor que una sola cuota y el crédito no está en mora. Eso indica
    /// que <c>balance</c> mide algo distinto de <c>minimumPayment</c> y
    /// <c>feeValue</c> —muy probablemente capital vs. capital + intereses + aval—,
    /// así que compararlos entre sí no tiene sentido.
    ///
    /// Con las reglas anteriores esto producía dos fallas seguidas: primero el botón
    /// quedaba gris para siempre (mínimo &gt; saldo, ninguna de las dos condiciones
    /// se podía satisfacer), y después de acotar el mínimo al saldo, se bloqueaban
    /// pagos que Credinet sí acepta.
    ///
    /// LA REGLA CORRECTA: **la autoridad sobre cuánto se puede pagar es Credinet, no
    /// este módulo.** Aquí solo se atajan errores evidentes de tipeo (monto cero,
    /// negativo, o mayor que cualquier importe que el propio proveedor reporta). Si
    /// el monto no cumple una regla de negocio, Credinet lo rechaza — y desde la
    /// corrección de los errores con HTTP 4xx, ese rechazo llega con su motivo real y
    /// se le muestra al cajero.
    ///
    /// Es preferible una llamada rechazada con mensaje claro que un botón gris que
    /// impide operar.
    /// </summary>
    private decimal TechoAbsoluto
    {
        get
        {
            var candidatos = new[] { Saldo, MinimoReportado, CuotaReportada, TotalReportado };
            var techo = candidatos.Max();
            // Si Credinet no reportó ningún importe, no se impone techo.
            return techo > 0 ? techo : decimal.MaxValue;
        }
    }

    /// <summary>
    /// Advertencias (no bloqueos) sobre el monto elegido. El cajero decide con la
    /// información a la vista; Credinet valida.
    /// </summary>
    public string AdvertenciaMonto
    {
        get
        {
            if (CreditoSeleccionado is null || Monto <= 0) return string.Empty;

            if (MinimoReportado > 0 && Monto < MinimoReportado)
                return $"El monto es menor al pago mínimo reportado " +
                       $"({((double)MinimoReportado).ToColombianCurrency()}). " +
                       "Sistecrédito puede rechazarlo.";

            if (Saldo > 0 && Monto > Saldo)
                return $"El monto supera el saldo de capital " +
                       $"({SaldoTexto}), pero puede ser correcto si cubre intereses.";

            return string.Empty;
        }
    }

    public bool TieneAdvertenciaMonto => !string.IsNullOrEmpty(AdvertenciaMonto);

    /// <summary>Primeros 8 caracteres del GUID, sin riesgo de NRE.</summary>
    private static string Shorten(string? id) =>
        string.IsNullOrEmpty(id) ? "-" : id[..Math.Min(8, id.Length)];
}
