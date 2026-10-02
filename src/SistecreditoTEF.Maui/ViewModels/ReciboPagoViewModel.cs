using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Hiopos.Models;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.UseCases;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Pantalla 8: comprobante del abono + cierre del flujo.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LOS DOS MODOS CONVIVEN AQUÍ (facturación y abono comparten el mismo APK)
/// ─────────────────────────────────────────────────────────────────────────────
/// El comprobante del abono sale por caminos distintos según cómo se entró, y es
/// importante que sea EXACTAMENTE uno de los dos para no imprimir doble:
///
///   • Modo HI-POS (el cajero llegó desde una venta abierta):
///     HioPos es el que imprime. Se le devuelven <c>MerchantReceipt</c> y
///     <c>CustomerReceipt</c> en el <c>setResult</c> y el POS los saca por su
///     impresora fiscal. NO se imprime localmente: sería un duplicado.
///
///   • Modo standalone (el cajero abrió la app desde el ícono del launcher):
///     no hay HioPos esperando, así que la app imprime por su cuenta vía
///     [IReceiptPrinter]. Aquí es donde estaba el problema real de impresión.
///
/// QA: antes, si la impresión local fallaba, se logueaba y la Activity se cerraba
/// igual — el cajero se quedaba sin comprobante y sin enterarse. Ahora el fallo
/// se le muestra y puede reintentar antes de cerrar.
/// </summary>
public partial class ReciboPagoViewModel(
    ITransactionStateStore state,
    ITransactionResultHandler resultHandler,
    ReceiptBuilder receiptBuilder,
    ModifyDocumentResultBuilder modifyDocBuilder,
    IStandaloneModeTracker standalone,
    IReceiptPrinter printer,
    ISesionCajero sesion,
    ApiConfig config) : ObservableObject
{
    // Los ids de medio de pago ya NO son constantes: salen de [ApiConfig] y se
    // pueden corregir por HioPosCloud sin recompilar. Un recaudo va al medio
    // EFECTIVO (PaymentMeanIdRecaudo) y una venta al medio sobre el que
    // consolida Sistecredito (PaymentMeanIdVenta).

    [ObservableProperty]
    private Payment? pago;

    /// <summary>Se fija una sola vez al abrir la pantalla (QA B-10).</summary>
    [ObservableProperty]
    private DateTime fechaComprobante = DateTime.Now;

    [ObservableProperty]
    private bool imprimiendo;

    [ObservableProperty]
    private string? mensajeImpresion;

    /// <summary>
    /// Identificador del abono para el cajero.
    ///
    /// Credinet puede devolver <c>PaymentNumber = 0</c> (se observó en el
    /// terminal: "Imprimiendo comprobante del pago #0"). Mostrar "#0" no le sirve a
    /// nadie y parece un error, así que en ese caso se usa el PaymentId abreviado,
    /// que sí identifica la operación ante Sistecrédito.
    /// </summary>
    public string NumeroPago
    {
        get
        {
            if (Pago is null) return "-";
            if (Pago.PaymentNumber > 0) return $"#{Pago.PaymentNumber}";
            return string.IsNullOrWhiteSpace(Pago.PaymentId)
                ? "-"
                : Pago.PaymentId[..Math.Min(8, Pago.PaymentId.Length)].ToUpperInvariant();
        }
    }
    /// <summary>
    /// Lo que efectivamente pagó el cliente: TODO lo que Credinet reporta como
    /// aplicado, no solo el capital.
    ///
    /// La pantalla destacaba <see cref="CapitalPagado"/> como cifra principal, y
    /// ése es el valor aplicado a capital: siempre menor o igual al cobrado,
    /// porque Credinet reparte el abono entre capital, intereses, mora, aval y
    /// cargos. El cajero cobraba $100.000 y el comprobante encabezaba con otro
    /// número, sin decir en ningún lado cuánto se había pagado.
    ///
    /// Es el mismo total que se le devuelve al POS como importe de la operación.
    /// </summary>
    public double TotalPagadoValor =>
        Pago is null
            ? 0
            : Pago.CreditValuePaid + Pago.InterestValuePaid + Pago.ArrearsValuePaid
              + Pago.AssuranceValuePaid + Pago.ChargeValuePaid;

    public string TotalPagado => TotalPagadoValor.ToColombianCurrency();

    public string CapitalPagado => Pago?.CreditValuePaid.ToColombianCurrency() ?? "$ 0";

    // Desglose. Cada concepto se muestra solo si tiene valor: "Mora $ 0" en un
    // abono sin mora no informa y hace dudar de si hay mora.
    public string InteresesPagados => Pago?.InterestValuePaid.ToColombianCurrency() ?? "$ 0";
    public string MoraPagada       => Pago?.ArrearsValuePaid.ToColombianCurrency() ?? "$ 0";
    public string AvalPagado       => Pago?.AssuranceValuePaid.ToColombianCurrency() ?? "$ 0";
    public string OtrosCargos      => Pago?.ChargeValuePaid.ToColombianCurrency() ?? "$ 0";

    public bool TieneIntereses   => Pago is { InterestValuePaid:  > 0 };
    public bool TieneMora        => Pago is { ArrearsValuePaid:   > 0 };
    public bool TieneAval        => Pago is { AssuranceValuePaid: > 0 };
    public bool TieneOtrosCargos => Pago is { ChargeValuePaid:    > 0 };

    public string SaldoRestante => Pago?.Balance.ToColombianCurrency() ?? "$ 0";
    public string ProximoPago   => Pago?.NextDueDate ?? "-";
    public string ProximoMinimo => Pago?.NextMinimumPayment.ToColombianCurrency() ?? "$ 0";

    /// <summary>
    /// QA B-10: antes era <c>=> DateTime.Now.ToString(...)</c>, recalculado en
    /// cada acceso, así que la misma pantalla podía mostrar horas distintas y el
    /// comprobante impreso no coincidía con lo mostrado.
    /// </summary>
    public string FechaPago =>
        FechaComprobante.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Nombre de la tienda para el comprobante: LA DE ESTA CAJA.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// ANTES SALIA LA TIENDA EQUIVOCADA, Y EN UN PAPEL QUE SE LLEVA EL CLIENTE
    /// ─────────────────────────────────────────────────────────────────────────
    /// Esto tomaba <c>storeName</c> del crédito seleccionado
    /// (<c>getactivecredits</c>), con el comentario de que era "el nombre real de
    /// la tienda donde está el crédito". No lo es: se reportó desde una tienda de
    /// Suba un crédito abierto ALLÍ que llegaba con "037 Tienda Koaj Cll 18
    /// Montevideo". Ese campo no describe al crédito.
    ///
    /// Pero aunque lo describiera, seguiría siendo el dato incorrecto para ESTE
    /// documento: un comprobante de pago dice dónde se recibió la plata, no dónde
    /// nació la deuda. Un abono cobrado en Suba imprimiéndose como Calle 18 es
    /// plata atribuida a la tienda que no la recibió, y solo se descubre al
    /// conciliar.
    ///
    /// Ahora sale de la configuración de la caja (<c>STORE_NAME</c> de
    /// CloudLicense, que ICG provisiona por terminal). Si no llegara, queda el
    /// genérico "Permoda": impreciso, pero nunca falso.
    /// </summary>
    private string Tienda => config.StoreName;

    /// <summary>
    /// Nombre del cliente. Lo carga [CreditosActivosViewModel] en modo best-effort;
    /// si no se pudo obtener, queda vacío y la fila se omite del comprobante.
    /// </summary>
    private string Cliente => state.ValidatedClient?.FullName ?? string.Empty;

    /// <summary>
    /// Documento del cliente. Siempre disponible: si la consulta del nombre falló,
    /// se toma del propio crédito seleccionado, que sí trae <c>idDocument</c>.
    /// </summary>
    private string ClienteDocumento
    {
        get
        {
            var deCliente = state.ValidatedClient?.DocumentId;
            if (!string.IsNullOrWhiteSpace(deCliente)) return deCliente;
            return state.SelectedCredit?.IdDocument ?? string.Empty;
        }
    }

    /// <summary>
    /// Quien cobro, para el comprobante impreso.
    ///
    /// MISMO orden de preferencia que el userName que se le manda a Credinet en
    /// [PagoViewModel.PagarAsync]: primero el cajero identificado en la app, despues
    /// el vendedor de HioPos, y el rotulo generico al final. Antes esta propiedad
    /// ignoraba la sesion, asi que en un abono standalone el comprobante decia
    /// "Cajero Permoda" mientras Credinet registraba el nombre real: dos papeles del
    /// mismo cobro con dos responsables distintos.
    ///
    /// Se filtra por blanco y no por null porque el nombre del cajero es opcional al
    /// darlo de alta (por eso [Cajero.NombreVisible], que cae al usuario).
    /// </summary>
    private string Cajero
    {
        get
        {
            var delCajero = sesion.Actual?.NombreVisible;
            if (!string.IsNullOrWhiteSpace(delCajero)) return delCajero.Trim();

            var deHiopos = SistecreditoService.ExtractSellerName(state.ActiveTransaction?.SellerData);
            if (!string.IsNullOrWhiteSpace(deHiopos)) return deHiopos.Trim();

            return "Cajero Permoda";
        }
    }

    partial void OnPagoChanged(Payment? value)
    {
        OnPropertyChanged(nameof(NumeroPago));
        OnPropertyChanged(nameof(TotalPagado));
        OnPropertyChanged(nameof(TotalPagadoValor));
        OnPropertyChanged(nameof(CapitalPagado));
        OnPropertyChanged(nameof(InteresesPagados));
        OnPropertyChanged(nameof(MoraPagada));
        OnPropertyChanged(nameof(AvalPagado));
        OnPropertyChanged(nameof(OtrosCargos));
        OnPropertyChanged(nameof(TieneIntereses));
        OnPropertyChanged(nameof(TieneMora));
        OnPropertyChanged(nameof(TieneAval));
        OnPropertyChanged(nameof(TieneOtrosCargos));
        OnPropertyChanged(nameof(SaldoRestante));
        OnPropertyChanged(nameof(ProximoPago));
        OnPropertyChanged(nameof(ProximoMinimo));
    }

    /// <summary>
    /// true en cuanto el comprobante salió por la impresora. Evita el duplicado
    /// entre la impresión automática y el botón "Finalizar".
    /// </summary>
    private bool _yaImpreso;

    public void Inicializar()
    {
        // ─────────────────────────────────────────────────────────────────────
        // POR QUÉ ESTO ES CONDICIONAL
        // ─────────────────────────────────────────────────────────────────────
        // Antes era una asignación seca: Pago = state.LastPayment. Y como
        // OnAppearing corre en CADA aparición de la página, un LastPayment nulo
        // BORRABA el pago que había llegado por parámetro de navegación.
        //
        // Pasaba de verdad: el estado vive solo en memoria, así que si Android
        // recreaba la Activity —tras un crash, o por presión de memoria— la
        // pantalla se restauraba con LastPayment en null. El cajero tocaba
        // "Finalizar" y caía en la rama "sin pago no hay nada que imprimir": la
        // app se cerraba en silencio, sin comprobante, sobre un abono YA COBRADO.
        //
        // El pago que llegó por navegación es igual de válido y nunca se degrada.
        if (state.LastPayment is not null)
            Pago = state.LastPayment;

        FechaComprobante = DateTime.Now;

        // ─────────────────────────────────────────────────────────────────────
        // SE IMPRIME ACÁ, NO EN "FINALIZAR"
        // ─────────────────────────────────────────────────────────────────────
        // El comprobante es el respaldo del cajero sobre un cobro que ya ocurrió.
        // Dejarlo detrás de un botón significaba que cualquier cosa que pasara
        // entremedio —un cierre, un crash, el cajero saliendo de la pantalla— le
        // costaba el comprobante de un abono real.
        //
        // Se imprime en cuanto se sabe que el pago salió bien. "Finalizar" ya no
        // imprime si esto funcionó (ver [_yaImpreso]), así que no hay duplicado.
        // Ya NO se imprime al abrir la pantalla.
        //
        // Se habia puesto aca para que un cierre o un crash no le costara el
        // comprobante al cajero, pero el efecto era que el comprobante salia
        // SIEMPRE. En un recaudo no siempre hace falta, y cada impresion gasta
        // rollo. Ahora lo decide el boton que se toca al cerrar:
        // [ImprimirYFinalizarAsync] o [FinalizarSinImprimirAsync].
        //
        // El comprobante queda en pantalla mientras tanto, y el boton de
        // reimprimir sigue disponible.
    }

    /// <summary>
    /// Impresión al abrir la pantalla. Un fallo acá NO interrumpe al cajero: la
    /// pantalla queda con el comprobante a la vista y el botón de reimprimir, y
    /// "Finalizar" vuelve a intentarlo con el diálogo de reintento.
    /// </summary>
    private async Task ImprimirAutomaticamenteAsync()
    {
        try
        {
            await ImprimirAsync();
        }
        catch (Exception ex)
        {
            // Defensivo: es fire-and-forget, así que una excepción que se escape
            // sube al contexto de sincronización y mata el proceso.
            AppLogger.E("ReciboPagoViewModel",
                "Error en la impresion automatica del comprobante", ex);
        }
    }

    /// <summary>
    /// Si el cajero pidio imprimir el comprobante de ESTE abono.
    ///
    /// Antes el comprobante salia siempre. Ahora se decide con el boton que se
    /// toca al cerrar: en un recaudo el comprobante no siempre hace falta, y un
    /// POS gasta rollo en cada impresion.
    ///
    /// Aplica SOLO a los abonos. La facturacion normal ([FinalizarConHiopos]) no
    /// pasa por aca: ese comprobante es fiscal y lo imprime HioPos siempre.
    /// </summary>
    private bool _imprimirComprobante;

    /// <summary>Cierra el abono e imprime el comprobante.</summary>
    [RelayCommand]
    private async Task ImprimirYFinalizarAsync()
    {
        _imprimirComprobante = true;
        await FinalizarAsync();
    }

    /// <summary>Cierra el abono sin imprimir nada.</summary>
    [RelayCommand]
    private async Task FinalizarSinImprimirAsync()
    {
        _imprimirComprobante = false;
        await FinalizarAsync();
    }

    private async Task FinalizarAsync()
    {
        // HU8-973 (Fase 2): tres modos de cierre, en este orden:
        //   1. REFUND desde HioPos: imprime localmente + devuelve setResult con
        //      TransactionType=REFUND al POS (NO FinishAffinity, porque HioPos
        //      espera el resultado del pago para registrarlo).
        //   2. Standalone del launcher: imprime localmente + FinishAffinity
        //      (no hay nadie esperando el resultado).
        //   3. HioPos SALE (facturacion normal): el POS se encarga de imprimir
        //      el comprobante fiscal a partir del XML que le devolvemos, y
        //      devuelve control al cajero al cerrar la venta.
        if (standalone.IsRefundFromHioPos)
        {
            await FinalizarRefundFromHioPosAsync();
            return;
        }

        if (standalone.IsStandalone)
        {
            await FinalizarStandaloneAsync();
            return;
        }

        FinalizarConHiopos();
    }

    // ------------------------------------------------------------------
    // Modo standalone: la app imprime
    // ------------------------------------------------------------------

    private async Task FinalizarStandaloneAsync()
    {
        if (Pago is null)
        {
            // No se cierra en silencio. Llegar acá significa que se perdió la
            // referencia al pago (Activity recreada, estado en memoria vaciado),
            // y el abono pudo haberse cobrado igual. Queda en el log y el cajero
            // ve el motivo en pantalla antes de que se cierre nada.
            AppLogger.W("ReciboPagoViewModel",
                "Finalizar sin pago en memoria: no hay comprobante que imprimir. " +
                "Si el abono se cobro, hay que verificarlo en Sistecredito.");
            MensajeImpresion =
                "No se encontro el comprobante en memoria. Si el abono se cobro, " +
                "verificalo en Sistecredito antes de reintentar.";
            return;
        }

        // Sin pedido de impresion, o ya impreso, se cierra sin sacar nada.
        if (!_imprimirComprobante || _yaImpreso)
        {
            state.Clear();
            standalone.Reset();
            CloseActivity();
            return;
        }

        var impreso = await ImprimirAsync();

        if (!impreso)
        {
            // QA: NO cerrar la app en silencio. El cajero decide.
            var reintentar = await PreguntarReintentoAsync();
            if (reintentar)
            {
                impreso = await ImprimirAsync();
                if (!impreso)
                {
                    MensajeImpresion =
                        "No se pudo imprimir el comprobante. El abono SI quedo registrado " +
                        $"(pago #{Pago.PaymentNumber}).";
                    return;   // se deja la pantalla abierta con el comprobante en pantalla
                }
            }
            else
            {
                // El cajero eligio continuar sin imprimir: queda auditado.
                AppLogger.W("ReciboPagoViewModel",
                    $"El cajero continuo sin imprimir el comprobante del pago #{Pago.PaymentNumber}.");
            }
        }

        state.Clear();
        standalone.Reset();
        CloseActivity();
    }

    /// <summary>
    /// Imprime el comprobante por la cadena de printers. Devuelve el resultado
    /// REAL (antes los printers devolvían true sin haber impreso).
    /// </summary>
    private async Task<bool> ImprimirAsync()
    {
        if (Pago is null) return false;

        Imprimiendo = true;
        MensajeImpresion = "Imprimiendo comprobante...";
        try
        {
            var receipt = BuildStandaloneReceipt(Pago);
            AppLogger.I("ReciboPagoViewModel",
                $"Imprimiendo comprobante del pago #{Pago.PaymentNumber} via {printer.Name}...");

            var ok = await printer.PrintAsync(receipt);

            if (ok) _yaImpreso = true;

            MensajeImpresion = ok ? "Comprobante enviado a la impresora." : null;
            AppLogger.I("ReciboPagoViewModel",
                $"Comprobante del pago #{Pago.PaymentNumber}: {(ok ? "OK" : "FALLO")}.");
            return ok;
        }
        catch (Exception ex)
        {
            AppLogger.E("ReciboPagoViewModel", "Error imprimiendo el comprobante del abono", ex);
            return false;
        }
        finally
        {
            Imprimiendo = false;
        }
    }

    /// <summary>Reintento explícito desde la UI (botón "Reimprimir").</summary>
    [RelayCommand]
    private async Task ReimprimirAsync() => await ImprimirAsync();

    /// <summary>
    /// El comprobante que se le manda a HioPos para que lo imprima, o null si el POS
    /// no es quien tiene que imprimirlo. Ver el bloque de MerchantReceipt en
    /// [DevolverRefundAHiopos].
    /// </summary>
    private string? ComprobanteParaHiopos(string comprobante, bool esEntradaDeCaja) =>
        _imprimirComprobante && !esEntradaDeCaja ? comprobante : null;

    private static async Task<bool> PreguntarReintentoAsync()
    {
        try
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return false;

            return await page.DisplayAlertAsync(
                "No se pudo imprimir",
                "El abono quedo registrado, pero no se pudo imprimir el comprobante. " +
                "Queres reintentar?",
                "Reintentar",
                "Continuar sin imprimir");
        }
        catch (Exception ex)
        {
            AppLogger.W("ReciboPagoViewModel", $"No se pudo mostrar el aviso de impresion: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Importe que HioPos pidió cobrar, en centavos, o null si no vino.
    /// </summary>
    private long? ImportePedidoPorElPos()
    {
        var crudo = state.ActiveTransaction?.AmountCents;
        if (string.IsNullOrWhiteSpace(crudo)) return null;

        return long.TryParse(crudo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HISTORIA DE ESTE BLOQUE: TRES INTENTOS Y UNA LINEA DE MANUAL
    // ══════════════════════════════════════════════════════════════════════════
    // Se pidio que al volver del modulo la entrada de caja quedara con el medio de
    // pago en EFECTIVO y el importe en el valor del abono. Costo tres intentos y
    // vale dejar escrito por que, porque el error es facil de repetir.
    //
    // INTENTO 1 — FixedPaymentMeanId + FixedPaymentMeanAmount como extras, JUNTO
    // con el Amount normal. No funciono: HioPos ignoro el medio fijado y tomo el
    // Amount como plata entregada, generando un vuelto de $99.899 sobre una
    // entrada de $1.
    //
    // INTENTO 2 — se concluyo (mal) que el mecanismo no aplicaba a este modulo,
    // porque esos dos campos aparecen como <HeaderField> de <ModifyDocumentResult>
    // en el manual de la API DOCUMENTO (HPCL.025), y ademas ninguno de los puntos
    // de llamada de esa API cubre una entrada de caja. Las dos observaciones son
    // ciertas pero la conclusion era falsa: los campos EXISTEN TAMBIEN como extras
    // de la respuesta de TRANSACTION en el manual de Cobro Electronico.
    //
    // INTENTO 3 — el que funciona. El manual de Cobro Electronico dice, sobre
    // FixedPaymentMeanAmount: "si se paga todo el importe usando este medio de
    // pago, NO HAY QUE REGRESAR EL IMPORTE NORMAL, ya que se usara este en su
    // lugar". El problema nunca fue el canal: era que mandabamos los DOS
    // esquemas de importe a la vez. Quitando el Amount, el medio fijado manda.
    //
    // LA LECCION: el intento 2 se apoyo en deducir de un manual que no era el de
    // esta API, y antes el informe de QA del 24/07 (hallazgo A-9) habia afirmado
    // que estos campos "existen justamente para esto" sin verificarlo. Las tres
    // veces el error fue el mismo: concluir sin leer el documento correcto.
    //
    // PENDIENTE, no alcanzado todavia: la DESCRIPCION de la entrada de caja. La
    // columna existe (CashIn.Description, leida del esquema de HioPos) y se
    // probaron las claves Description, Comment y Concept sin efecto. ICG quedo
    // consultandolo. Ver [DescripcionDelRecaudo] cuando haya respuesta.

    /// <summary>
    /// Campos personalizados del medio de pago que HioPos guarda con la operación.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// LOS NOMBRES TIENEN QUE COINCIDIR EXACTAMENTE CON HioPosCloud
    /// ─────────────────────────────────────────────────────────────────────────
    /// Los campos libres se declaran en HioPosCloud (dimensión "Entrada Caja"),
    /// y HioPos los cruza POR NOMBRE. Un nombre que no coincide no da error: el
    /// campo queda vacío, y eso solo se descubre al conciliar.
    ///
    /// Antes se mandaban 6 con nombres propios (<c>CapitalPaid</c>,
    /// <c>InterestPaid</c>, <c>ArrearsPaid</c>…) mientras HioPosCloud declaraba
    /// 16 con los nombres de la respuesta de Credinet (<c>creditValuePaid</c>,
    /// <c>interestValuePaid</c>…). Resultado: 3 con nombre distinto, 3 que
    /// diferían solo en la mayúscula inicial y 10 que nunca llegaban.
    ///
    /// Ahora se usan los nombres TAL COMO los declara HioPosCloud, en su misma
    /// capitalización. Si allá se agrega o renombra un campo, hay que reflejarlo
    /// acá: son dos listas que deben moverse juntas.
    ///
    /// <c>errorCode</c> y <c>message</c> van vacíos a propósito: este código solo
    /// corre cuando el abono fue exitoso. Si hubiera fallado, no habría pago que
    /// registrar en la caja.
    /// </summary>
    private (string Key, string Value)[] CamposPersonalizadosDelAbono()
    {
        if (Pago is null) return [];

        return
        [
            ("errorCode",          string.Empty),
            ("message",            string.Empty),
            ("country",            "CO"),
            ("paymentId",          Pago.PaymentId),
            ("paymentNumber",      Pago.PaymentNumber.ToString(CultureInfo.InvariantCulture)),
            ("creditId",           Pago.CreditId),
            ("typeDocument",       Pago.TypeDocument),
            ("idDocument",         Pago.IdDocument),
            ("creditValuePaid",    Money.ToFiscalAmount(Pago.CreditValuePaid)),
            ("interestValuePaid",  Money.ToFiscalAmount(Pago.InterestValuePaid)),
            ("arrearsValuePaid",   Money.ToFiscalAmount(Pago.ArrearsValuePaid)),
            ("assuranceValuePaid", Money.ToFiscalAmount(Pago.AssuranceValuePaid)),
            ("chargeValuePaid",    Money.ToFiscalAmount(Pago.ChargeValuePaid)),
            ("balance",            Money.ToFiscalAmount(Pago.Balance)),
            ("nextDueDate",        Pago.NextDueDate),
            ("nextMinimumPayment", Money.ToFiscalAmount(Pago.NextMinimumPayment))
        ];
    }

    private StandaloneReceipt BuildStandaloneReceipt(Payment pago) => new(
        Tienda: Tienda,
        Fecha: FechaComprobante,
        Cajero: Cajero,
        // Mismo criterio que [NumeroPago]: si Credinet no dio consecutivo, se
        // imprime el PaymentId abreviado en vez de un "0" que no identifica nada.
        PaymentNumber: NumeroPago.TrimStart('#'),
        CreditNumber: state.SelectedCredit?.CreditNumber.ToString(CultureInfo.InvariantCulture)
                      ?? pago.CreditId,
        Cliente: Cliente,
        ClienteDocumento: ClienteDocumento,
        CapitalPagado: Money.ToDecimal(pago.CreditValuePaid),
        SaldoRestante: Money.ToDecimal(pago.Balance),
        ProximoPago: DateTime.TryParse(pago.NextDueDate, CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var d)
                     ? d
                     : FechaComprobante.AddMonths(1),
        ProximoMinimo: Money.ToDecimal(pago.NextMinimumPayment),
        // Los otros cuatro componentes del pago. Sin ellos el comprobante solo
        // podia mostrar el capital, que NO es lo que pago el cliente: Credinet
        // reparte el abono entre capital, intereses, mora, aval y cargos.
        // [StandaloneReceipt.TotalPagado] los suma.
        InteresesPagados: Money.ToDecimal(pago.InterestValuePaid),
        MoraPagada: Money.ToDecimal(pago.ArrearsValuePaid),
        AvalPagado: Money.ToDecimal(pago.AssuranceValuePaid),
        OtrosCargos: Money.ToDecimal(pago.ChargeValuePaid));

    // ------------------------------------------------------------------
    // Modo HI-POS SALE: el POS imprime a partir del XML que le devolvemos
    // ------------------------------------------------------------------
    //
    // Este camino es para ventas (TransactionType=SALE). El REFUND desde
    // HioPos va por [FinalizarRefundFromHioPosAsync] arriba.

    /// <summary>
    /// HU8-973 (Fase 2): REFUND originado por HI-POS.
    ///
    /// La terminal dispara un TRANSACTION con TransactionType=REFUND y espera
    /// un setResult con TransactionType=REFUND. Como no hay venta abierta en
    /// el POS, el comprobante del recaudo se imprime localmente (igual que
    /// en standalone), pero el control vuelve al POS (igual que en SALE) en
    /// vez de cerrar toda la app con FinishAffinity.
    ///
    /// Diferencias con [FinalizarConHiopos] (SALE):
    ///   - Se imprime localmente via [IReceiptPrinter]; en SALE el POS imprime
    ///     el comprobante fiscal a partir del XML.
    ///   - El response NO lleva MerchantReceipt/CustomerReceipt: si los
    ///     mandamos, HioPos intentaria imprimirlos y no tiene venta para
    ///     anexar el comprobante, lo cual es ruido en la factura del POS.
    ///   - TransactionType=REFUND (en SALE va SALE).
    /// </summary>
    private async Task FinalizarRefundFromHioPosAsync()
    {
        if (Pago is null)
        {
            // Mismo criterio que FinalizarStandaloneAsync: NO cerrar en
            // silencio. Si llegamos aca sin pago en memoria, el abono pudo
            // haberse cobrado igual y hay que avisarle al cajero.
            AppLogger.W("ReciboPagoViewModel",
                "Finalizar REFUND desde HioPos sin pago en memoria: no hay comprobante " +
                "que imprimir. Si el abono se cobro, hay que verificarlo en Sistecredito.");
            MensajeImpresion =
                "No se encontro el comprobante en memoria. Si el abono se cobro, " +
                "verificalo en Sistecredito antes de reintentar.";
            return;
        }

        // ─────────────────────────────────────────────────────────────────────
        // QUIEN IMPRIME DEPENDE DE SI HIOPOS TIENE UN DOCUMENTO
        // ─────────────────────────────────────────────────────────────────────
        // Hay dos formas de llegar a un abono desde el POS, y solo en una HioPos
        // puede imprimir:
        //
        //   • ENTRADA DE CAJA (Caja > Entradas de caja): no hay venta abierta, asi
        //     que HioPos no tiene documento al que anexar el comprobante y descarta
        //     los MerchantReceipt/CustomerReceipt que le mandemos. IMPRIMIMOS NOSOTROS.
        //
        //   • VENTA ABANDONADA para hacer un abono: hay ActiveDocument, HioPos si
        //     puede anexar e imprimir. Le mandamos los recibos y no imprimimos.
        //
        // Un rato se delego SIEMPRE en HioPos, sobre la premisa de que "la termica no
        // aparece en el bus USB de este terminal". Esa premisa quedo vieja: era cierta
        // cuando [UsbEscPosPrinter] exigia interfaz de clase 07, y la Epson TM-T88V de
        // la caja declara su unica interfaz como clase 0xFF. Verificado en el terminal:
        //
        //     /sys/bus/usb/devices/2-1.3.1   EPSON TM-T88V
        //       2-1.3.1:1.0  class=ff  bulk OUT ep_01     <- alcanzable
        //     enabled_print_services = null               <- Android Print no sirve
        //
        // Con la deteccion arreglada la termica SI es alcanzable, y en entrada de caja
        // es el unico que puede sacar el comprobante: delegar ahi significaba que no
        // imprimia nadie.
        //
        // Lo que NO se recupera de aquella version: que un fallo de impresion bloquee
        // el setResult. Se imprime, se informe lo que pase, y se le contesta a HioPos
        // IGUAL. El abono ya esta cobrado; dejar al POS esperando por un problema de
        // papel seria cambiar un comprobante faltante por una caja trabada.
        var esEntradaDeCaja = state.ActiveDocument is null;

        if (esEntradaDeCaja && _imprimirComprobante)
        {
            var impreso = await ImprimirAsync();
            MensajeImpresion = impreso
                ? "Comprobante enviado a la impresora."
                : $"No se pudo imprimir el comprobante. El abono SI quedo registrado " +
                  $"(pago #{Pago.PaymentNumber}).";
        }
        else
        {
            MensajeImpresion = _imprimirComprobante
                ? "El comprobante se envio al POS para imprimir."
                : null;
        }

        DevolverRefundAHiopos();
    }

    /// <summary>
    /// Construye el HioposResponse del REFUND y lo entrega al POS via
    /// [ITransactionResultHandler.FinishWithResult], que cierra la Activity
    /// (no la app) y devuelve control a HioPos.
    /// </summary>
    private void DevolverRefundAHiopos()
    {
        if (Pago is null)
        {
            resultHandler.FinishWithResult(BuildFailed("No hay pago para devolver al POS."));
            return;
        }

        try
        {
            // Mismo calculo de total que en SALE: el monto devuelto al POS
            // es lo REALMENTE pagado (capital + interes + mora + aval + cargos).
            var paidTotal = Pago.CreditValuePaid + Pago.InterestValuePaid + Pago.ArrearsValuePaid
                          + Pago.AssuranceValuePaid + Pago.ChargeValuePaid;
            var amountCents = Money.ToCents(paidTotal);

            var authorizationId = DianFieldSanitizer.AuthorizationId(Pago.PaymentId, Pago.PaymentNumber);

            // ─────────────────────────────────────────────────────────────────
            // EL COMPROBANTE LO IMPRIME HIOPOS
            // ─────────────────────────────────────────────────────────────────
            // Antes esta respuesta NO llevaba recibos, y el modulo intentaba
            // imprimir por su cuenta via USB. En este terminal la termica no
            // aparece en el bus USB —el printer reporta "no disponible" y ni
            // siquiera intenta—, asi que el abono terminaba sin comprobante.
            //
            // La prueba de que este es el camino correcto la dio el propio
            // terminal: una FACTURA cobrada con Sistecredito si se imprime, y en
            // ese flujo lo unico que hacemos es devolver MerchantReceipt y
            // CustomerReceipt. La impresora funciona; le faltaba el contenido.
            var comprobante = receiptBuilder.BuildPaymentReceipt(
                tienda: Tienda,
                fecha: FechaComprobante,
                cajero: Cajero,
                paymentNumber: NumeroPago.TrimStart('#'),
                creditNumber: state.SelectedCredit?.CreditNumber.ToString(CultureInfo.InvariantCulture)
                              ?? Pago.CreditId,
                capitalPagado: Pago.CreditValuePaid,
                saldoRestante: Pago.Balance,
                proximoPago: Pago.NextDueDate,
                proximoMinimo: Pago.NextMinimumPayment,
                cliente: Cliente,
                // El desglose completo: sin esto el voucher solo podia mostrar el
                // capital, que no es lo que pago el cliente. Es el mismo reparto
                // que arriba suma en [paidTotal] para el importe que ve el POS.
                interesesPagados: Pago.InterestValuePaid,
                moraPagada: Pago.ArrearsValuePaid,
                avalPagado: Pago.AssuranceValuePaid,
                otrosCargos: Pago.ChargeValuePaid);

            // ─────────────────────────────────────────────────────────────────
            // ENTRADA DE CAJA: SE FIJA EL MEDIO DE PAGO Y SU IMPORTE
            // ─────────────────────────────────────────────────────────────────
            // El cajero llega por Caja > Entradas de caja, selecciona Sistecredito
            // y escribe "1" en importe solo para habilitar el boton (HioPos exige
            // importe > 0). El monto real se determina DENTRO del modulo, y la
            // plata entra en EFECTIVO al cajon.
            //
            // El mecanismo es [FixedPaymentMeanId] + [FixedPaymentMeanAmount], y
            // el manual de Cobro Electronico los define asi:
            //
            //   FixedPaymentMeanId: "En caso de recibir este valor, su importe
            //   modificara el medio de pago en la pantalla de total y pasara a
            //   usar el indicado (incluso si no esta visible en la pantalla de
            //   total). Este valor debe pasarse siempre como String."
            //
            //   FixedPaymentMeanAmount: "Importe pagado en el medio de pago
            //   fijado; tener en cuenta que, si se paga todo el importe usando
            //   este medio de pago, NO HAY QUE REGRESAR EL IMPORTE NORMAL, ya que
            //   se usara este en su lugar. Este valor debe pasarse siempre como
            //   String."
            //
            // LA CLAVE, Y POR QUE ANTES NO FUNCIONABA: se mandaban los dos, el
            // [Amount] normal Y el FixedPaymentMeanAmount. El manual dice
            // explicitamente que no hay que regresar el importe normal. Medido en
            // terminal con los dos presentes: HioPos ignoraba el fijado y tomaba
            // el Amount como plata ENTREGADA por el cliente, con este resultado
            // sobre una entrada de $1 y un abono de $99.900:
            //
            //     TEF SISTECREDITO      $ 1
            //     Entregado:            $ 99.900
            //     Cambio:               $ 99.899   <- vuelto fantasma
            //
            // Ese vuelto lo iba a esperar el arqueo. Por eso aca el Amount se
            // QUITA del diccionario: con el medio fijado, el importe es el del
            // medio fijado.
            //
            // NO se hace en el otro caso REFUND-from-HioPos (el cajero abandona
            // una venta para hacer un abono): alla hay [ActiveDocument], el
            // importe es de la venta y pisarlo descuadra la factura.
            var esEntradaDeCaja = state.ActiveDocument is null;

            var stringExtras = new Dictionary<string, string?>
                {
                    [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),

                    // Se devuelve el MISMO tipo que mando el POS.
                    //
                    // Antes iba "REFUND" fijo. Desde "entrada de caja" HioPos manda
                    // TransactionType=SALE, asi que le contestabamos algo distinto de
                    // lo que pregunto. Capturado en terminal: al recibir esa
                    // respuesta relanzaba otro TRANSACTION con TransactionId nuevo
                    // (4000614 -> 4000615) en vez de cerrarla, y el abono no quedaba
                    // registrado como movimiento de caja.
                    [HioposExtras.TransactionType]   =
                        state.ActiveTransaction?.TransactionType ?? "SALE",

                    [HioposExtras.Amount]            = amountCents.ToString(CultureInfo.InvariantCulture),
                    [HioposExtras.TipAmount]         = "0",
                    [HioposExtras.TaxAmount]         = "0",
                    [HioposExtras.SurchargeAmount]   = "0",
                    [HioposExtras.TransactionData]   =
                        $"{{\"p\":\"{authorizationId}\",\"n\":{Pago.PaymentNumber.ToString(CultureInfo.InvariantCulture)}}}",
                    [HioposExtras.AuthorizationId]   = authorizationId,
                    [HioposExtras.CardType]          = "Sistecredito",
                    [HioposExtras.CardHolder]        = state.ValidatedClient?.FullName,

                    // Los recibos son lo que HioPos imprime, y solo se mandan si el
                    // POS puede realmente sacarlos:
                    //
                    //   • El cajero tiene que haber pedido imprimir.
                    //   • Y NO puede ser una entrada de caja: ahi no hay documento al
                    //     que anexarlos, HioPos los descarta, y ya los imprimimos
                    //     nosotros por la termica (ver [FinalizarRefundFromHioPosAsync]).
                    //     Mandarlos igual seria pedirle al POS una impresion que no
                    //     puede hacer, o duplicar la que ya salio.
                    //
                    // El abono queda registrado en los dos casos.
                    [HioposExtras.MerchantReceipt]   = ComprobanteParaHiopos(comprobante, esEntradaDeCaja),
                    [HioposExtras.CustomerReceipt]   = ComprobanteParaHiopos(comprobante, esEntradaDeCaja)
                };

            if (esEntradaDeCaja)
            {
                // El contrato REAL de este flujo —medido en el APK de HioPos, no en
                // el manual— vive en [HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja],
                // que es codigo cubierto por la suite: TransactionType=CASH_IN mas
                // el Amount del abono. Con CASH_IN, HioPos aplica el importe
                // (setNetAmount); con SALE solo lo registra como plata entregada y
                // deja un vuelto que el arqueo espera del cajon.
                HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(stringExtras, amountCents);

                AppLogger.I("ReciboPagoViewModel",
                    $"Entrada de caja: se responde TransactionType=CASH_IN con importe " +
                    $"{amountCents} centavos (el POS pidio " +
                    $"{ImportePedidoPorElPos()?.ToString(CultureInfo.InvariantCulture) ?? "(ausente)"}). " +
                    "Es la unica respuesta del modulo que NO hace eco del tipo recibido: " +
                    "CashTransactionActivity.onExternalModuleResult solo aplica el importe " +
                    "con CASH_IN o CASH_OUT.");
            }

            var response = new HioposResponse(
                Action: HioposActions.Transaction,
                StringExtras: stringExtras,
                ResultCode: Hiopos.Result.OkValue);

            // El nombre del metodo dice REFUND por historia (ver [IStandaloneModeTracker]),
            // pero lo que se devuelve es CASH_IN en un recaudo y el eco del tipo en el
            // resto. El log dice el tipo REAL: leer "REFUND" sobre una respuesta CASH_IN
            // costo mas de un minuto de confusion en el terminal.
            AppLogger.I("ReciboPagoViewModel",
                $"Respuesta a HioPos ({stringExtras[HioposExtras.TransactionType]}): " +
                $"payment #{Pago.PaymentNumber}, " +
                $"amountCents={amountCents.ToString(CultureInfo.InvariantCulture)}, " +
                $"authorizationId={authorizationId}, " +
                $"esEntradaDeCaja={esEntradaDeCaja}" +
                (esEntradaDeCaja
                    ? $", importePedidoPorElPos={ImportePedidoPorElPos()?.ToString(CultureInfo.InvariantCulture) ?? "(ausente)"}"
                    : string.Empty) + ".");

            state.Clear();
            standalone.Reset();
            // Finish (no FinishAffinity): cierra solo esta Activity y
            // devuelve control a HioPos, que registra el resultado y sigue.
            resultHandler.FinishWithResult(response);
        }
        catch (Exception ex)
        {
            // Igual que FinalizarConHiopos: si armar el response falla, NO
            // dejamos a HioPos esperando para siempre.
            AppLogger.E("ReciboPagoViewModel",
                "Excepcion inesperada armando la respuesta del REFUND", ex);
            resultHandler.FinishWithResult(BuildFailed(
                $"El abono SI quedo registrado (pago #{Pago?.PaymentNumber}), pero no se pudo " +
                "devolver el resultado al POS. Verificalo en Sistecredito."));
        }
    }

    private void FinalizarConHiopos()
    {
        if (Pago is null)
        {
            resultHandler.FinishWithResult(BuildFailed("No hay pago para devolver al POS."));
            return;
        }

        try
        {
            var merchantReceipt = receiptBuilder.BuildPaymentReceipt(
                tienda: Tienda,
                fecha: FechaComprobante,
                cajero: Cajero,
                paymentNumber: Pago.PaymentNumber.ToString(CultureInfo.InvariantCulture),
                // Nº de credito LEGIBLE (el int), no el GUID. Fallback al CreditId
                // si no hay SelectedCredit.
                creditNumber: state.SelectedCredit?.CreditNumber.ToString(CultureInfo.InvariantCulture)
                              ?? Pago.CreditId,
                capitalPagado: Pago.CreditValuePaid,
                saldoRestante: Pago.Balance,
                proximoPago: Pago.NextDueDate,
                proximoMinimo: Pago.NextMinimumPayment,
                cliente: Cliente,
                // Igual que en el otro camino: el voucher tiene que mostrar el
                // TOTAL pagado, no solo el capital. Es el mismo reparto que unas
                // lineas mas abajo suma en [paidTotal].
                interesesPagados: Pago.InterestValuePaid,
                moraPagada: Pago.ArrearsValuePaid,
                avalPagado: Pago.AssuranceValuePaid,
                otrosCargos: Pago.ChargeValuePaid);

            // El monto devuelto al POS es lo REALMENTE pagado, no el Amount del
            // Intent (que es el total de la factura). Un abono es self-contained:
            // no hereda tip/tax/tipo de la venta.
            var paidTotal = Pago.CreditValuePaid + Pago.InterestValuePaid + Pago.ArrearsValuePaid
                          + Pago.AssuranceValuePaid + Pago.ChargeValuePaid;
            var amountCents = Money.ToCents(paidTotal);

            var authorizationId = DianFieldSanitizer.AuthorizationId(Pago.PaymentId, Pago.PaymentNumber);

            var modifyResult = modifyDocBuilder.Build(
                paymentMeanId:    config.PaymentMeanIdVenta,
                type:             "0",
                lineNumber:       "1",
                amount:           amountCents.ToString(CultureInfo.InvariantCulture),
                authorizationId:  authorizationId,
                transactionId:    null,
                customFields: CamposPersonalizadosDelAbono());

            var response = new HioposResponse(
                Action: HioposActions.Transaction,
                StringExtras: new Dictionary<string, string?>
                {
                    [HioposExtras.TransactionResult] = TransactionResult.Accepted.ToWire(),
                    [HioposExtras.TransactionType]   = "SALE",
                    [HioposExtras.Amount]            = amountCents.ToString(CultureInfo.InvariantCulture),
                    [HioposExtras.TipAmount]         = "0",
                    [HioposExtras.TaxAmount]         = "0",
                    [HioposExtras.SurchargeAmount]   = "0",
                    [HioposExtras.TransactionData]   =
                        $"{{\"p\":\"{authorizationId}\",\"n\":{Pago.PaymentNumber.ToString(CultureInfo.InvariantCulture)}}}",
                    [HioposExtras.ModifyDocumentResult] = modifyResult,
                    [HioposExtras.MerchantReceipt]   = merchantReceipt,
                    [HioposExtras.CustomerReceipt]   = merchantReceipt,
                    [HioposExtras.AuthorizationId]   = authorizationId,
                    [HioposExtras.CardType]          = "Sistecredito",
                    [HioposExtras.CardHolder]        = state.ValidatedClient?.FullName
                },
                ResultCode: Hiopos.Result.OkValue);

            state.Clear();
            resultHandler.FinishWithResult(response);
        }
        catch (Exception ex)
        {
            // Si armar el recibo o la respuesta falla, NO dejamos a HI-POS
            // esperando para siempre: Failed explicito.
            AppLogger.E("ReciboPagoViewModel",
                "Excepcion inesperada armando la respuesta del abono", ex);
            resultHandler.FinishWithResult(BuildFailed(
                $"El abono SI quedo registrado (pago #{Pago?.PaymentNumber}), pero no se pudo " +
                "armar el comprobante. Verificalo en Sistecredito."));
        }
    }

    private static HioposResponse BuildFailed(string message) =>
        new(HioposActions.Transaction,
            new Dictionary<string, string?>
            {
                [HioposExtras.TransactionResult] = TransactionResult.Failed.ToWire(),
                [HioposExtras.ErrorMessage] = message,
                [HioposExtras.ErrorMessageTitle] = "No se pudo completar el abono"
            },
            ResultCode: Hiopos.Result.OkValue);

    private static void CloseActivity()
    {
        try
        {
            // FinishAffinity limpia el back stack y saca la app de "recientes".
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.FinishAffinity();
        }
        catch (Exception ex)
        {
            AppLogger.E("ReciboPagoViewModel",
                "Error cerrando activity en modo standalone", ex);
        }
    }
}
