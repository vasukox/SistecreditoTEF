using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.PantallaCliente;

/// <summary>
/// QUE MOSTRARLE AL CLIENTE EN CADA MOMENTO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ES UNA FUNCION PURA Y NO UNA LLAMADA DESDE CADA PANTALLA
/// ─────────────────────────────────────────────────────────────────────────────
/// La alternativa era que cada ViewModel avisara "ahora mostra esto". Serian diez
/// puntos de modificacion en el camino por donde pasa una venta real, y cada uno
/// una oportunidad de romper algo que hoy funciona en 512 tiendas — para una
/// pantalla que es informativa.
///
/// Aca la entrada es la RUTA en la que esta el Shell mas el estado que la app ya
/// lleva, y la salida es el contenido. Ningun ViewModel cambia, el mapeo entero
/// se puede probar sin Android y sin segunda pantalla, y si manana aparece una
/// ruta nueva sin contemplar, cae en [VitrinaCliente.Reposo]: la marca. Nunca una
/// pantalla en blanco ni un dato de mas.
///
/// El precio de esta decision, dicho: el contenido cambia cuando cambia la
/// PANTALLA, no cuando cambia un dato dentro de ella. Mientras el asesor mueve el
/// monto en la seleccion de cuotas, el cliente ve el texto del paso, no el numero
/// moviendose. Es aceptable para lo que se pidio; si algun dia se quiere en vivo,
/// el lugar donde engancharlo es [VitrinaCoordinador], no esta tabla.
/// </summary>
public static class GuionDeLaVitrina
{
    /// <summary>
    /// Traduce "en que pantalla estamos" + "que sabemos" a "que ve el cliente".
    /// </summary>
    /// <param name="ruta">
    /// Ubicacion del Shell (<c>Shell.CurrentState.Location</c>). Se acepta con
    /// barras y con query; ver [Normalizar].
    /// </param>
    /// <param name="estado">Estado del tramite en curso.</param>
    public static VitrinaCliente Para(string? ruta, ITransactionStateStore estado)
    {
        ArgumentNullException.ThrowIfNull(estado);

        // Lo que NO esta en la tabla cae en la marca. Splash, menu, ingreso del
        // cajero, administracion y replicacion quedan afuera A PROPOSITO: son
        // pantallas de operacion interna. En la de replicacion, ademas, hay un
        // codigo de emparejamiento en letra grande que no puede terminar
        // proyectado al salon de ventas.
        return Momentos.TryGetValue(Normalizar(ruta), out var momento)
            ? momento(estado)
            : VitrinaCliente.Reposo;
    }

    /// <summary>
    /// Ruta → que se muestra.
    ///
    /// Las claves salen de [AppRoutes], no de literales sueltos: el dia que una
    /// ruta se renombre, esto se renombra con ella en vez de quedarse mostrando la
    /// marca en silencio. El comparador es ORDINAL SIN MAYUSCULAS porque la
    /// ubicacion que entrega el Shell no garantiza conservar el mismo casing con
    /// el que la ruta se registro.
    /// </summary>
    private static readonly Dictionary<string, Func<ITransactionStateStore, VitrinaCliente>> Momentos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Compra a crédito (entra por HioPos) ──────────────────────────
            [AppRoutes.CapturaCedula]     = EnConsulta,
            [AppRoutes.ValidacionCliente] = Saludando,
            [AppRoutes.SeleccionCuotas]   = EligiendoPlan,
            [AppRoutes.Otp]               = PidiendoCodigo,
            [AppRoutes.Confirmacion]      = CompraAprobada,

            // ── Abonos (entra por el ícono, proceso manual) ──────────────────
            [AppRoutes.CreditosActivos]   = EligiendoCredito,
            [AppRoutes.Pago]              = CobrandoCuota,
            [AppRoutes.ReciboPago]        = PagoRegistrado
        };

    // ------------------------------------------------------------------
    // Los ocho momentos
    // ------------------------------------------------------------------

    private static VitrinaCliente EnConsulta(ITransactionStateStore e) => new(
        PasoDeLaVitrina.Identificando,
        "Un momento, por favor",
        "Estamos consultando su cupo",
        EtiquetaDelMonto: "Valor de la compra",
        Monto: MontoDeLaCompra(e));

    private static VitrinaCliente Saludando(ITransactionStateStore e) => new(
        PasoDeLaVitrina.Saludo,
        Saludo(e),
        "Revisamos su cupo disponible",
        EtiquetaDelMonto: "Valor de la compra",
        Monto: MontoDeLaCompra(e));

    private static VitrinaCliente EligiendoPlan(ITransactionStateStore e) => new(
        PasoDeLaVitrina.Plan,
        "Elija su plan de pago",
        "Su asesor le muestra las opciones de cuotas",
        EtiquetaDelMonto: "Valor de la compra",
        Monto: MontoDeLaCompra(e));

    /// <summary>
    /// El paso delicado. El codigo NO se muestra —es la firma del credito— y la
    /// pantalla dice explicitamente a quien va dictado, porque es el momento en
    /// el que un tercero podria pedirlo haciendose pasar por Sistecredito.
    /// </summary>
    private static VitrinaCliente PidiendoCodigo(ITransactionStateStore e) => new(
        PasoDeLaVitrina.Codigo,
        "Le enviamos un código por WhatsApp",
        "Dígaselo únicamente a su asesor. Nadie más debe pedírselo.",
        EtiquetaDelMonto: "Valor de la compra",
        Monto: MontoDeLaCompra(e),
        Aviso: Plazo(e.Months));

    private static VitrinaCliente CompraAprobada(ITransactionStateStore e)
    {
        var credito = e.CreatedCredit;

        var monto = credito is { CreditValue: > 0 }
            ? credito.CreditValue.ToColombianCurrency()
            : MontoDeLaCompra(e);

        // El plazo de la respuesta de Credinet manda sobre el elegido en pantalla:
        // es el que quedo efectivamente pactado.
        var cuotas = credito is { Fees: > 0 } ? credito.Fees : e.Months;

        return new VitrinaCliente(
            PasoDeLaVitrina.Cierre,
            "¡Compra aprobada!",
            "Gracias por comprar con Sistecrédito",
            EtiquetaDelMonto: "Crédito aprobado",
            Monto: monto,
            Aviso: Plazo(cuotas));
    }

    private static VitrinaCliente EligiendoCredito(ITransactionStateStore e) => new(
        PasoDeLaVitrina.Saludo,
        Saludo(e),
        "Vamos a registrar el pago de su cuota");

    /// <summary>
    /// Se muestra el SALDO del credito, no el importe que se esta digitando.
    ///
    /// El importe todavia se esta tecleando y puede cambiar dos veces antes de
    /// cobrarse; un numero que baila en la pantalla del cliente confunde mas de lo
    /// que informa, y si se congelara el primero que se ve, mentiria. El saldo, en
    /// cambio, es un hecho que ya viene de Credinet.
    /// </summary>
    private static VitrinaCliente CobrandoCuota(ITransactionStateStore e)
    {
        var saldo = e.SelectedCredit?.Balance ?? 0;

        return new VitrinaCliente(
            PasoDeLaVitrina.Plan,
            "Pago de su cuota",
            "Su asesor le confirma el valor antes de cobrar",
            EtiquetaDelMonto: saldo > 0 ? "Saldo del crédito" : null,
            Monto: saldo > 0 ? saldo.ToColombianCurrency() : null);
    }

    private static VitrinaCliente PagoRegistrado(ITransactionStateStore e)
    {
        var pago = e.LastPayment;
        if (pago is null)
        {
            return new VitrinaCliente(
                PasoDeLaVitrina.Cierre, "¡Pago registrado!", "Gracias por su pago");
        }

        // El MISMO total que encabeza el comprobante y que se le devuelve al POS
        // (ver [ReciboPagoViewModel.TotalPagadoValor]): capital mas todo lo que
        // Credinet aplico. Si aca se mostrara solo el capital, el cliente leeria
        // un numero menor al que acaba de entregar.
        var total = pago.CreditValuePaid + pago.InterestValuePaid + pago.ArrearsValuePaid
                    + pago.AssuranceValuePaid + pago.ChargeValuePaid;

        return new VitrinaCliente(
            PasoDeLaVitrina.Cierre,
            "¡Pago registrado!",
            "Gracias por su pago",
            EtiquetaDelMonto: "Valor pagado",
            Monto: total.ToColombianCurrency(),
            Aviso: pago.Balance > 0
                ? $"Saldo pendiente: {pago.Balance.ToColombianCurrency()}"
                : "Este crédito queda en ceros");
    }

    // ------------------------------------------------------------------
    // Piezas
    // ------------------------------------------------------------------

    /// <summary>
    /// Deja la ubicacion del Shell en un solo segmento comparable.
    ///
    /// El Shell entrega cosas como <c>//splash/capturaCedula</c> y, cuando hay
    /// parametros, <c>replicacion?soloCajeros=True</c>. Interesa el ultimo tramo
    /// y nada mas.
    /// </summary>
    public static string Normalizar(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return string.Empty;

        var sinQuery = ruta.Split('?', 2)[0];
        var tramos = sinQuery.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return tramos.Length == 0 ? string.Empty : tramos[^1];
    }

    /// <summary>
    /// "Hola, Marcela" cuando se sabe quien es; "Hola" a secas cuando todavia no.
    ///
    /// Va el nombre DE PILA solamente. El completo y la cedula identifican a la
    /// persona ante cualquiera que este mirando la pantalla; el de pila es lo que
    /// el asesor ya dice en voz alta al atenderla.
    /// </summary>
    private static string Saludo(ITransactionStateStore e)
    {
        var nombre = PrimerNombre(e.ValidatedClient);
        return nombre is null ? "Hola" : $"Hola, {nombre}";
    }

    private static string? PrimerNombre(Client? cliente)
    {
        if (cliente is null) return null;

        var nombre = cliente.FirstName?.Trim();

        if (string.IsNullOrEmpty(nombre))
        {
            nombre = cliente.FullName?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(nombre)) return null;

        // Credinet devuelve los nombres en mayusculas. "MARCELA" gritado en letra
        // de 60 puntos no saluda a nadie.
        return nombre.Length == 1
            ? nombre.ToUpperInvariant()
            : char.ToUpperInvariant(nombre[0]) + nombre[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Valor de la compra. Se prefiere el monto a financiar ya fijado; mientras no
    /// exista, sirve el total del documento que abrio HioPos, que es el mismo
    /// numero que el cliente tiene delante en el mostrador.
    /// </summary>
    private static string? MontoDeLaCompra(ITransactionStateStore e)
    {
        if (e.CreditValue > 0) return e.CreditValue.ToColombianCurrency();

        var total = e.ActiveDocument?.Total ?? 0m;
        return total > 0 ? total.ToColombianCurrency() : null;
    }

    private static string? Plazo(int meses) => meses switch
    {
        <= 0 => null,
        1    => "A 1 cuota",
        _    => $"A {meses} cuotas"
    };
}
