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

    /// <summary>
    /// Plazo mas largo que se le llega a preguntar a Credinet.
    ///
    /// No es una regla nuestra: es el techo observado de la oferta. Existe para que
    /// el sondeo tenga un final cuando <c>getSimulatedMonthLimit</c> no contesta y
    /// no hay techo con el que podar.
    /// </summary>
    public const int PlazoMaximo = 24;

    /// <summary>
    /// TODOS los plazos, uno por uno. No una seleccion.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// EL DEFECTO QUE ESTO CORRIGE
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Esto era una lista escrita a mano:
    ///
    ///     { 1, 2, 3, 6, 9, 12, 18, 24 }
    ///
    /// O sea que 4, 5, 7, 8, 10, 11… NUNCA se le preguntaban a Credinet. Y lo que
    /// la pantalla ofrece son los plazos que Credinet confirmo DE LOS QUE SE LE
    /// PREGUNTARON, asi que un plazo ausente de esta lista no aparecia jamas,
    /// aunque Credinet lo aceptara sin problema.
    ///
    /// Reportado desde produccion: la pantalla decia "Plazos que Sistecredito
    /// acepta para este monto: 1, 2, 3, 6" y en Credinet estaban tambien el 4 y el
    /// 5. No faltaba el techo —el 6 se ofrecia—: faltaban los numeros del medio,
    /// porque nadie los habia escrito aqui.
    ///
    /// El cajero perdia la venta o la cerraba en un plazo que no era el que el
    /// cliente podia pagar. De los dos, el segundo es peor.
    ///
    /// La lista escrita a mano era ademas una suposicion sobre el negocio de otra
    /// empresa: dice que Sistecredito financia a 9 pero no a 10, a 18 pero no a 15.
    /// Nadie verifico eso nunca. Preguntando por todos, la respuesta la da quien la
    /// sabe, y el dia que Sistecredito habilite el 7 aparece solo, sin recompilar.
    ///
    /// El costo esta acotado por la PODA: [SistecreditoService.ObtenerPlazosValidosAsync]
    /// recorta por el techo que devuelve getSimulatedMonthLimit antes de sondear,
    /// asi que para los montos de tienda —techos de 2, 3 o 6— se consultan 2, 3 o 6
    /// plazos. Son MENOS llamadas que los 8 candidatos de antes, y ahora completas.
    /// </summary>
    public static readonly int[] CandidateMonths =
        Enumerable.Range(1, PlazoMaximo).ToArray();

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
    private int mesesSeleccionados = 0;

    [ObservableProperty]
    private decimal monto = 0m;

    /// <summary>
    /// Texto del campo de monto, con separador de miles ("500.000").
    ///
    /// El Entry se bindea a ESTE string y no al decimal: el punto es separador de
    /// MILES en es-CO y separador DECIMAL en cultura invariante, así que un binding
    /// numérico podría leer "500.000" como 500. En la pantalla que define el monto
    /// del crédito, un error de mil veces sería grave. Ver [MoneyInput].
    /// </summary>
    [ObservableProperty]
    private string montoTexto = string.Empty;

    /// <summary>
    /// Lee el valor a partir de los DIGITOS del texto, sin reescribirlo.
    ///
    /// Reformatear el texto desde este handler crasheaba la app al borrar digitos:
    /// el texto quedaba mas corto que la posicion de cursor que el Entry de Android
    /// intentaba restaurar. El formato visible lo pone [MoneyEntryBehavior].
    /// </summary>
    partial void OnMontoTextoChanged(string value) => Monto = MoneyInput.Parse(value);

    /// <summary>
    /// Escribe el monto formateado, manteniendo texto y valor sincronizados. La usa
    /// la precarga desde el documento de HioPos.
    /// </summary>
    private void FijarMonto(decimal valor)
    {
        var entero = Math.Round(valor, MidpointRounding.AwayFromZero);
        MontoTexto = entero > 0 ? MoneyInput.FormatValue(entero) : string.Empty;
        Monto = entero;
    }

    public string ClienteNombre  => state.ValidatedClient?.FullName ?? "(cliente)";
    public string CupoTotal      => state.ValidatedClient?.CreditLimit.ToColombianCurrency() ?? "$ 0";
    public string CupoDisponible => state.ValidatedClient?.AvailableCreditLimit.ToColombianCurrency() ?? "$ 0";
    /// <summary>
    /// Cuota del periodo. Manual (M-SCL-03 v05): <c>totalFeeValue</c> es "el valor
    /// total de la cuota para cada periodo, incluye los valores de capital,
    /// financiacion y aval". Verificado con la respuesta real de $199.900 a 3 meses:
    /// 69.162 (credito) + 6.663 (aval) + 1.266 (IVA del aval) = 77.091. Exacto.
    /// </summary>
    public string CuotaMensual   => Detalles?.TotalFeeValue.ToColombianCurrency() ?? "$ 0";

    /// <summary>
    /// Total a pagar. Comprobado que es exactamente la cuota por el plazo:
    /// 77.091 x 3 = 231.273.
    /// </summary>
    public string TotalPagar     => Detalles?.TotalPaymentValue.ToColombianCurrency() ?? "$ 0";

    /// <summary>
    /// El monto que Credinet realmente financio, tomado de <c>creditValue</c> de la
    /// respuesta y NO del campo de captura: si el cajero sigue escribiendo despues de
    /// simular, el desglose tiene que seguir describiendo la simulacion que se ve, no
    /// lo que hay tipeado en ese instante.
    /// </summary>
    public string MontoFinanciado => Detalles?.CreditValue.ToColombianCurrency() ?? "$ 0";
    public string Intereses      => Detalles?.TotalInterestValue.ToColombianCurrency() ?? "$ 0";

    /// <summary>
    /// Aval CON su IVA, que es lo que el cliente realmente paga.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE NO ES AssuranceValue
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Mostraba <c>AssuranceValue</c>, que es el aval SIN impuesto. El efecto era que
    /// el desglose en pantalla NO SUMABA el total, y el cajero no tenia como
    /// explicarlo. Con la respuesta real de Credinet para $199.900 a 3 meses:
    ///
    ///     monto              199.900
    ///     intereses            7.586   totalInterestValue
    ///     aval                19.990   assuranceValue        &lt;- lo que se mostraba
    ///                        ────────
    ///     suma               227.476
    ///     total a pagar      231.273   totalPaymentValue
    ///     diferencia           3.797   = assuranceTaxValue (IVA del aval)
    ///
    /// Con <c>AssuranceTotalValue</c> (23.788 = aval + IVA) el desglose cierra:
    /// 199.900 + 7.586 + 23.788 = 231.274, contra un total de 231.273 — un peso de
    /// redondeo de Credinet, no un error nuestro.
    ///
    /// En una pantalla que origina un credito, un desglose que no cuadra es algo que
    /// el cliente puede objetar con razon.
    /// </summary>
    public string Aval           => Detalles?.AssuranceTotalValue.ToColombianCurrency() ?? "$ 0";
    public string CuotaInicial   => Detalles?.TotalDownPayment.ToColombianCurrency() ?? "$ 0";

    public bool IsLoading => Status == EstadoSimulacion.Loading;
    public bool HasResults => Status == EstadoSimulacion.Success && Detalles is not null;
    public bool HasError => Status == EstadoSimulacion.Error;

    /// <summary>
    /// Texto del boton de simular. Cambia mientras se calcula en lugar de dejar el
    /// texto fijo con un spinner aparte: el boton ES el indicador de progreso, asi el
    /// cajero ve el estado en el elemento que acaba de tocar y no mas abajo.
    /// </summary>
    public string TextoBotonCalcular => IsLoading ? "Calculando…" : "Calcular y simular";

    /// <summary>
    /// El boton se apaga mientras hay un cálculo en vuelo. Sin esto, dos toques
    /// seguidos —normales en un POS lento— disparaban dos simulaciones en paralelo y
    /// la segunda respuesta podia pisar a la primera.
    /// </summary>
    public bool PuedeCalcular => !IsLoading;

    partial void OnDetallesChanged(CreditDetails? value)
    {
        OnPropertyChanged(nameof(CuotaMensual));
        OnPropertyChanged(nameof(TotalPagar));
        OnPropertyChanged(nameof(Intereses));
        OnPropertyChanged(nameof(Aval));
        OnPropertyChanged(nameof(CuotaInicial));
        OnPropertyChanged(nameof(MontoFinanciado));
        OnPropertyChanged(nameof(HasResults));
    }

    partial void OnStatusChanged(EstadoSimulacion value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(TextoBotonCalcular));
        OnPropertyChanged(nameof(PuedeCalcular));
    }

    partial void OnLimitStatusChanged(EstadoLimite value)
    {
        OnPropertyChanged(nameof(PlazosDisponibles));
        OnPropertyChanged(nameof(HayPlazos));
        OnPropertyChanged(nameof(MensajePlazos));

        // HU-134 (Fase 3): cuando llega el resultado del limite de meses
        // desde Credinet, ajustamos el plazo seleccionado al mayor permitido.
        // Antes se hacia solo dentro de [OnMontoChangedAsync], pero la
        // CollectionView puede pisar el plazo con su primer elemento cuando
        // cambia el ItemsSource (porque el valor anterior queda fuera de
        // la lista filtrada). Reaccionando al cambio de estado garantizamos
        // que el ajuste ocurre despues de que la UI se actualiza.
        //
        // Se ajusta en CUALQUIER cambio de estado, no solo al pasar a Success.
        // Antes solo se hacia en Success, asi que al volver a Loading —el cajero
        // cambio el monto y se esta reconsultando— la lista quedaba vacia pero el
        // chip del plazo anterior seguia marcado: la pantalla mostraba
        // seleccionado un plazo que ya no estaba autorizado.
        AjustarPlazoSiQuedoFueraDeRango();
    }

    partial void OnMaxMonthsChanged(int value)
    {
        OnPropertyChanged(nameof(PlazosDisponibles));
        OnPropertyChanged(nameof(HayPlazos));
        OnPropertyChanged(nameof(MensajePlazos));

        // Tambien reaccionamos al cambio del maximo de meses para cubrir el
        // caso en el que la CollectionView tarda en propagar el nuevo
        // ItemsSource.
        if (LimitStatus == EstadoLimite.Success)
            AjustarPlazoSiQuedoFueraDeRango();
    }

    /// <summary>
    /// Plazos que el cajero puede elegir para ESTE monto.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// FALLA CERRADO: SIN CONFIRMACION DE CREDINET NO SE OFRECE NINGUN PLAZO
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Antes el caso "todavia no se sabe" devolvia CandidateMonths completo, o sea
    /// todos los plazos hasta [PlazoMaximo]:
    ///
    ///     LimitStatus == Success ? CandidateMonths.Where(...) : CandidateMonths;
    ///
    /// Y ese es el estado por DEFECTO: al entrar a la pantalla, mientras corre el
    /// debounce, y durante los ~4 segundos que tarda getSimulatedMonthLimit. En toda
    /// esa ventana el cajero veia —y podia seleccionar— plazos que el cliente no
    /// tiene habilitados. Si la consulta fallaba, quedaban ofrecidos para siempre.
    ///
    /// En una pantalla que origina un credito, el caso desconocido no puede
    /// resolverse a favor del maximo. Ahora la lista esta VACIA hasta que Credinet
    /// confirma el limite: se ofrece solo lo que esta autorizado.
    ///
    /// [MensajePlazos] explica al cajero por que no hay opciones todavia, para que
    /// una lista vacia se lea como "esperando" y no como "la app esta rota".
    /// </summary>
    /// <summary>
    /// Plazos que Credinet CONFIRMO para el monto actual, con su cuota ya calculada.
    /// Lo llena [OnMontoChangedAsync] preguntando plazo por plazo.
    /// </summary>
    private IReadOnlyList<SistecreditoService.PlazoSimulado> _plazosConCuota = [];

    /// <summary>
    /// Monto con el que se consultaron [_plazosConCuota]. Sella el cache: si el
    /// monto en pantalla ya no es este, el cache no aplica.
    /// </summary>
    private decimal _montoDeLosPlazos;

    /// <summary>Aviso si algun plazo no se pudo verificar por un fallo tecnico.</summary>
    [ObservableProperty]
    private string? avisoPlazosIncompletos;

    /// <summary>
    /// Credinet no financia ESTE MONTO para este cliente, con ningun plazo: no tiene
    /// una oferta que lo cubra.
    ///
    /// Es distinto de "ningun plazo sirve" y merece su propio mensaje. Sin esta
    /// distincion el cajero probaba plazo por plazo un monto que nunca iba a pasar, y
    /// el rechazo real recien aparecia en la pantalla del OTP, en ingles
    /// ("InvalidAmountCredit"). Ver [SistecreditoService.ObtenerPlazosValidosAsync].
    /// </summary>
    private bool _sinOferta;

    public bool TieneAvisoPlazos => !string.IsNullOrEmpty(AvisoPlazosIncompletos);

    partial void OnAvisoPlazosIncompletosChanged(string? value) =>
        OnPropertyChanged(nameof(TieneAvisoPlazos));

    public IReadOnlyList<int> PlazosDisponibles =>
        LimitStatus == EstadoLimite.Success
            ? _plazosConCuota.Select(p => p.Months).ToArray()
            : [];

    /// <summary>true si hay al menos un plazo confirmado para elegir.</summary>
    public bool HayPlazos => PlazosDisponibles.Count > 0;

    /// <summary>
    /// Por que no hay plazos para elegir. Sin esto, la lista vacia parece un error
    /// de la aplicacion en vez de una espera o un limite real del cliente.
    /// </summary>
    public string MensajePlazos => LimitStatus switch
    {
        // Va ANTES del caso general de "sin plazos": la causa es el monto, no el
        // plazo, y decirle al cajero que pruebe otro plazo lo manda a perder tiempo.
        // OJO con el texto: NO se menciona al cliente ni su cupo, porque el tope no
        // es del cliente. Verificado contra Credinet: getSimulatedMonthLimit —el
        // endpoint que decide esto— NI RECIBE la cedula, y el corte cae en el mismo
        // monto exacto para cualquier documento. Un cliente con $8.973.700 de cupo
        // disponible es rechazado igual.
        //
        // Culpar al cliente mandaba al cajero a mirar el cupo, que esta perfecto.
        EstadoLimite.Success when _sinOferta =>
            "Sistecredito no financia este monto (no hay plan de credito para ese " +
            "valor). No es el cupo del cliente. Proba un monto menor o cobra la " +
            "venta con otro medio de pago.",

        EstadoLimite.Success when _plazosConCuota.Count == 0 =>
            "Sistecredito no acepta ningun plazo para este monto. Proba con otro valor.",
        EstadoLimite.Success =>
            "Plazos que Sistecredito acepta para este monto: " +
            string.Join(", ", _plazosConCuota.Select(p => p.Months)) + ".",
        EstadoLimite.Loading => "Consultando los plazos que acepta Sistecredito...",
        EstadoLimite.Error =>
            "No pudimos consultar los plazos. Corregi el monto para reintentar.",
        _ => Monto > 0
            ? "Consultando los plazos que acepta Sistecredito..."
            : "Ingresa el monto para ver los plazos disponibles."
    };

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
            FijarMonto(precargado);
    }

    /// <summary>
    /// Monto y plazo con los que se calculo [Detalles]. Si el cajero cambia
    /// cualquiera de los dos, la simulacion en pantalla deja de corresponder.
    /// </summary>
    private decimal _montoSimulado;
    private int _mesesSimulados;

    /// <summary>
    /// Descarta la simulacion mostrada.
    ///
    /// Es necesario porque [ContinuarAsync] solo miraba <c>Detalles is null</c>: si
    /// el cajero simulaba 500.000 a 12 meses y despues cambiaba el monto o el plazo,
    /// la tarjeta de resultado seguia mostrando la cuota ANTERIOR y el boton de
    /// confirmar seguia habilitado. Se podia originar un credito sobre numeros que
    /// ya no correspondian a lo que estaba en pantalla.
    /// </summary>
    private void InvalidarSimulacion()
    {
        if (Detalles is null && Status == EstadoSimulacion.Idle) return;

        Detalles = null;
        Status = EstadoSimulacion.Idle;
        ErrorMessage = null;
    }

    partial void OnMontoChanged(decimal value)
    {
        InvalidarSimulacion();

        // ─────────────────────────────────────────────────────────────────────
        // LOS PLAZOS CONFIRMADOS PERTENECEN A UN MONTO
        // ─────────────────────────────────────────────────────────────────────
        // Al cambiar el monto dejan de aplicar, y hay que descartarlos EN EL
        // ACTO. Antes solo se descartaba la simulacion, asi que durante los 500 ms
        // del debounce LimitStatus seguia en Success y _plazosConCuota conservaba
        // los plazos del monto anterior. En esa ventana:
        //
        //   1. Se simulaba $500.000 -> plazos y cuotas de $500.000 en cache.
        //   2. El cajero escribia y el monto pasaba a $600.000.
        //   3. Tocaba un chip de plazo -> lo encontraba en el cache VIEJO, mostraba
        //      la cuota de $500.000 y sellaba _montoSimulado con el monto NUEVO.
        //   4. La barrera de ContinuarAsync comparaba Monto == _montoSimulado y
        //      pasaba: se originaba un credito por $600.000 con la cuota de
        //      $500.000 a la vista.
        //
        // Volver a Idle deja PlazosDisponibles vacio (falla cerrado) hasta que
        // Credinet confirme los plazos del monto nuevo.
        _plazosConCuota = [];
        _montoDeLosPlazos = 0m;
        _sinOferta = false;
        AvisoPlazosIncompletos = null;
        LimitStatus = EstadoLimite.Idle;

        OnPropertyChanged(nameof(PlazosDisponibles));
        OnPropertyChanged(nameof(HayPlazos));
        OnPropertyChanged(nameof(MensajePlazos));
    }

    /// <summary>
    /// Al elegir un plazo, la cuota se muestra AL INSTANTE: ya vino calculada en la
    /// consulta de plazos validos, asi que no hace falta otra llamada de ~4 segundos.
    /// Si por alguna razon no esta en cache, se descarta la simulacion y el cajero
    /// usa "Calcular".
    /// </summary>
    partial void OnMesesSeleccionadosChanged(int value)
    {
        // El cache solo vale para el monto con el que se consulto. La comparacion
        // es la garantia estructural de que la cuota mostrada corresponde al monto
        // en pantalla, en vez de depender de que ningun evento se cruce.
        var enCache = _montoDeLosPlazos == Monto
            ? _plazosConCuota.FirstOrDefault(p => p.Months == value)
            : null;

        if (enCache is not null)
        {
            // Se sella con el monto de la CONSULTA, no con la propiedad viva.
            _montoSimulado = _montoDeLosPlazos;
            _mesesSimulados = value;
            Detalles = enCache.Detalles;
            ErrorMessage = null;
            Status = EstadoSimulacion.Success;
            return;
        }

        InvalidarSimulacion();
    }

    [RelayCommand]
    private async Task CalcularAsync()
    {
        var cliente = state.ValidatedClient;
        if (cliente is null || Monto <= 0 || MesesSeleccionados <= 0) return;

        // BARRERA DE PLAZO: no se simula un plazo que Credinet no autorizo para este
        // monto. Es la ultima linea de defensa si la UI alcanza a ofrecer un chip
        // obsoleto mientras cambia el ItemsSource de la lista.
        var autorizados = PlazosDisponibles;
        if (!autorizados.Contains(MesesSeleccionados))
        {
            AppLogger.W("SeleccionCuotasViewModel",
                $"Simulacion bloqueada: {MesesSeleccionados} meses no esta autorizado " +
                $"(autorizados: {(autorizados.Count == 0 ? "ninguno todavia" : string.Join(",", autorizados))}).");
            ErrorMessage = autorizados.Count == 0
                ? "Todavia no se confirmaron los plazos autorizados para este monto."
                : "El plazo seleccionado no esta autorizado para este monto.";
            Status = EstadoSimulacion.Error;
            return;
        }

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
                    // Se anota con QUE monto y plazo se calculo, para poder detectar
                    // despues si lo mostrado sigue correspondiendo a lo elegido.
                    _montoSimulado = Monto;
                    _mesesSimulados = MesesSeleccionados;
                    Detalles = ok.Data;
                    Status = EstadoSimulacion.Success;
                    break;
                case ApiResult<CreditDetails>.Failure<CreditDetails> f:
                    // Traducido, no crudo: asignaba [UserMessage] y el cajero leia
                    // "CREDINET: MonthsNumberNotValid".
                    AppLogger.W("SeleccionCuotasViewModel",
                        $"getCreditDetails rechazado: {f.Cause.UserMessage}");
                    ErrorMessage = FriendlyMessage.FromApiError(f.Cause);
                    Status = EstadoSimulacion.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.E("SeleccionCuotasViewModel",
                $"Excepcion inesperada simulando credito", ex);
            // El detalle tecnico queda en el log; en pantalla va lo accionable.
            ErrorMessage = "No pudimos calcular la cuota. Revisa la conexion e intenta " +
                           "de nuevo.";
            Status = EstadoSimulacion.Error;
        }
    }

    [RelayCommand]
    private async Task ContinuarAsync()
    {
        if (Detalles is null) return;

        // BARRERA FINAL antes del OTP: lo que se va a originar tiene que ser
        // exactamente lo que el cajero tiene en pantalla y lo que Credinet autorizo.
        // Es el ultimo punto donde se puede detener un credito con numeros que ya no
        // corresponden.
        if (Monto != _montoSimulado || MesesSeleccionados != _mesesSimulados)
        {
            AppLogger.W("SeleccionCuotasViewModel",
                $"Confirmacion bloqueada: se simulo {_mesesSimulados} meses sobre " +
                $"{_montoSimulado} y ahora hay {MesesSeleccionados} meses sobre {Monto}.");
            InvalidarSimulacion();
            ErrorMessage = "El monto o el plazo cambiaron. Volve a simular antes de continuar.";
            Status = EstadoSimulacion.Error;
            return;
        }

        if (!PlazosDisponibles.Contains(MesesSeleccionados))
        {
            AppLogger.W("SeleccionCuotasViewModel",
                $"Confirmacion bloqueada: {MesesSeleccionados} meses ya no esta autorizado.");
            InvalidarSimulacion();
            ErrorMessage = "El plazo dejo de estar autorizado para este monto. Volve a simular.";
            Status = EstadoSimulacion.Error;
            return;
        }

        // El estado que viaja al OTP y a la creacion del credito se fija con los
        // valores YA validados, no con lo que quede en las propiedades observables.
        state.CreditValue = _montoSimulado;
        state.Months = _mesesSimulados;

        await nav.GoToOtpAsync();
    }

    // QA A-11: estado del debounce REAL.
    private CancellationTokenSource? _debounceCts;
    private int _limitRequestSequence;

    /// <summary>Ventana de debounce del campo de monto.</summary>
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// La llama el code-behind cuando cambia el Entry de monto.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// QA A-11 — EL "DEBOUNCE" NO HACÍA DEBOUNCE
    /// ─────────────────────────────────────────────────────────────────────────
    /// La versión anterior era:
    /// <code>
    ///   CancellationTokenSource? localCts = null;   // declarado y JAMÁS usado
    ///   await Task.Delay(500);                      // sin token: no cancela nada
    /// </code>
    /// El comentario decía "Debounce 500ms para no spamear la API", pero lo que
    /// hacía era RETRASAR cada pulsación 500 ms y luego lanzarlas TODAS. Escribir
    /// "1500000" (7 dígitos) disparaba 7 llamadas concurrentes a
    /// <c>getSimulatedMonthLimit</c>. Y como las respuestas pueden llegar
    /// desordenadas, <c>MaxMonths</c> podía quedar fijado por la respuesta de un
    /// monto ANTERIOR: el cajero veía plazos que no correspondían al monto en
    /// pantalla, y <c>MesesSeleccionados</c> se ajustaba a esa lista incorrecta.
    /// El <c>catch (TaskCanceledException)</c> era código muerto.
    ///
    /// Ahora: se cancela la espera pendiente en cada pulsación (debounce real) y
    /// además se descartan las respuestas obsoletas por número de secuencia, para
    /// cubrir la carrera cuando dos peticiones alcanzan a salir.
    /// </summary>
    public async Task OnMontoChangedAsync(decimal monto)
    {
        // Cancela la espera anterior: solo la última pulsación llega a la API.
        var previous = _debounceCts;
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        if (previous is not null)
        {
            try { previous.Cancel(); } catch (ObjectDisposedException) { }
            previous.Dispose();
        }

        var token = cts.Token;
        var sequence = ++_limitRequestSequence;

        try
        {
            await Task.Delay(DebounceDelay, token);
            if (token.IsCancellationRequested || monto <= 0) return;

            var cliente = state.ValidatedClient;
            if (cliente is null) return;

            LimitStatus = EstadoLimite.Loading;

            // Se le pregunta a Credinet plazo por plazo cual acepta para este monto.
            // getSimulatedMonthLimit ya NO decide la lista: devolvia un techo que no
            // se corresponde con los plazos realmente validos (ver
            // [SistecreditoService.ObtenerPlazosValidosAsync]).
            var result = await service.ObtenerPlazosValidosAsync(
                (double)monto, CandidateMonths, cliente.DocumentType, cliente.DocumentId, token);

            // Respuesta obsoleta: el cajero siguió escribiendo. Descartar.
            if (token.IsCancellationRequested || sequence != _limitRequestSequence)
            {
                AppLogger.I("SeleccionCuotasViewModel",
                    $"Respuesta de plazos descartada (obsoleta, seq={sequence}).");
                return;
            }

            _plazosConCuota = result.Plazos;
            _montoDeLosPlazos = monto;
            _sinOferta = result.SinOferta;
            MaxMonths = result.Plazos.Count == 0 ? 0 : result.Plazos[^1].Months;
            AvisoPlazosIncompletos = result.ErrorTecnico is not null
                ? "Algunos plazos no se pudieron verificar por un problema de conexion; " +
                  "puede faltar alguna opcion."
                : null;

            LimitStatus = EstadoLimite.Success;
            OnPropertyChanged(nameof(PlazosDisponibles));
            OnPropertyChanged(nameof(HayPlazos));
            OnPropertyChanged(nameof(MensajePlazos));
            AjustarPlazoSiQuedoFueraDeRango();
        }
        catch (OperationCanceledException)
        {
            // Debounce: llegó otra pulsación. No es un error.
        }
        catch (Exception ex)
        {
            AppLogger.E("SeleccionCuotasViewModel",
                "Excepcion inesperada calculando limite de meses", ex);
            // QA M-16: no se muestra ex.Message al cajero.
            ErrorMessage = "No pudimos calcular los plazos disponibles. Intenta de nuevo.";
            LimitStatus = EstadoLimite.Error;
        }
    }

    /// <summary>
    /// Si el plazo elegido quedó fuera de los permitidos para este monto, se
    /// selecciona el máximo disponible, así los chips siempre muestran una opción
    /// válida marcada.
    /// </summary>
    private void AjustarPlazoSiQuedoFueraDeRango()
    {
        var disponibles = PlazosDisponibles;

        // Sin plazos confirmados no puede quedar ninguno seleccionado. Antes esta
        // funcion salia sin tocar nada, asi que un plazo elegido con el limite
        // anterior sobrevivia al cambio de monto y podia enviarse a la simulacion.
        if (disponibles.Count == 0)
        {
            if (MesesSeleccionados != 0) MesesSeleccionados = 0;
            return;
        }

        // QA B-7 (CA1826): indexación directa en vez de LINQ sobre una lista
        // indexable.
        var yaEsValido = false;
        for (var i = 0; i < disponibles.Count; i++)
        {
            if (disponibles[i] != MesesSeleccionados) continue;
            yaEsValido = true;
            break;
        }

        if (!yaEsValido)
            MesesSeleccionados = disponibles[^1];
    }
}
