using System.Reflection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Enums;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.Services.PantallaCliente;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Tests.Services.PantallaCliente;

/// <summary>
/// QUE VE —Y QUE NO VE— EL CLIENTE EN LA PANTALLA DE 11 PULGADAS.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTO SE PRUEBA Y NO SE MIRA EN LA TERMINAL
/// ─────────────────────────────────────────────────────────────────────────────
/// El fallo que importa en esta pantalla no se ve: es MOSTRAR DE MAS. Una cedula
/// completa, un telefono o el codigo OTP proyectados a un salon de ventas no
/// rompen nada, no dan error y nadie los reporta. Simplemente quedan ahi, a la
/// vista de quien pase, en 1.536 terminales.
///
/// Por eso el barrido de [NingunaPantalla_MuestraDatosSensibles] recorre TODAS las
/// rutas registradas —no las que uno se acuerda— con un estado completamente
/// poblado, y falla si alguno de esos datos aparece en cualquier campo.
/// </summary>
public class GuionDeLaVitrinaTests
{
    // Datos que NO pueden salir a la pantalla publica, con valores bien
    // reconocibles para que el barrido los encuentre sin ambigüedad.
    private const string Cedula   = "1037654321";
    private const string Telefono = "3001234567";
    private const string Correo   = "marcela.restrepo@correo.com";
    private const string Apellido = "Restrepo";
    private const string Codigo   = "884213";

    // ------------------------------------------------------------------
    // La prueba que justifica el archivo
    // ------------------------------------------------------------------

    [Fact]
    public void NingunaPantalla_MuestraDatosSensibles()
    {
        var estado = EstadoCompleto();

        foreach (var ruta in TodasLasRutas())
        {
            var vitrina = GuionDeLaVitrina.Para(ruta, estado);
            var texto = Todo(vitrina);

            Assert.DoesNotContain(Cedula, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Telefono, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Correo, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Codigo, texto, StringComparison.OrdinalIgnoreCase);

            // Del nombre va SOLO el de pila: el apellido identifica a la persona
            // ante cualquiera que este mirando.
            Assert.DoesNotContain(Apellido, texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Las pantallas de operacion interna no se le muestran al cliente. La de
    /// replicacion es la mas grave: lleva el codigo de emparejamiento entre cajas
    /// en letra grande, y proyectarlo al salon de ventas seria repartirlo.
    /// </summary>
    [Theory]
    [InlineData(AppRoutes.Splash)]
    [InlineData(AppRoutes.Menu)]
    [InlineData(AppRoutes.ConfigurarAdmin)]
    [InlineData(AppRoutes.IngresoCajero)]
    [InlineData(AppRoutes.AdminCajeros)]
    [InlineData(AppRoutes.Replicacion)]
    [InlineData("una-ruta-que-todavia-no-existe")]
    [InlineData("")]
    [InlineData(null)]
    public void PantallasDeOperacion_MuestranLaMarca(string? ruta)
    {
        var vitrina = GuionDeLaVitrina.Para(ruta, EstadoCompleto());

        Assert.Equal(VitrinaCliente.Reposo, vitrina);
        Assert.Equal(PasoDeLaVitrina.Reposo, vitrina.Paso);
        Assert.False(vitrina.TieneMonto);
    }

    // ------------------------------------------------------------------
    // Compra a credito
    // ------------------------------------------------------------------

    [Fact]
    public void MientrasSeConsulta_NoSeSabeQuienEs_YSeMuestraElTotalDeLaCompra()
    {
        var estado = new TransactionStateStore();
        estado.SetActiveDocument(DocumentoDe(240_000m));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.CapturaCedula, estado);

        Assert.Equal(PasoDeLaVitrina.Identificando, vitrina.Paso);
        Assert.Equal(240_000m.ToColombianCurrency(), vitrina.Monto);
    }

    [Fact]
    public void AlValidar_SeSaludaConElNombreDePila_EnMayusculaYMinuscula()
    {
        var estado = EstadoCompleto();

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.ValidacionCliente, estado);

        // Credinet devuelve "MARCELA". Un saludo en mayusculas no saluda.
        Assert.Equal("Hola, Marcela", vitrina.Titulo);
    }

    [Fact]
    public void SinClienteValidado_SeSaludaSinNombre()
    {
        var vitrina = GuionDeLaVitrina.Para(AppRoutes.ValidacionCliente, new TransactionStateStore());

        Assert.Equal("Hola", vitrina.Titulo);
    }

    /// <summary>
    /// El monto elegido para financiar MANDA sobre el total del documento: es el
    /// que el asesor acaba de fijar en pantalla y el que se va a cobrar.
    /// </summary>
    [Fact]
    public void ElegidoElMonto_EseGanaSobreElTotalDelDocumento()
    {
        var estado = new TransactionStateStore();
        estado.SetActiveDocument(DocumentoDe(240_000m));
        estado.CreditValue = 180_000m;

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.SeleccionCuotas, estado);

        Assert.Equal(180_000m.ToColombianCurrency(), vitrina.Monto);
    }

    [Fact]
    public void EnElOtp_NoSeMuestraElCodigo_YSeDiceAQuienDictarlo()
    {
        var estado = EstadoCompleto();
        estado.CreditValue = 180_000m;
        estado.Months = 4;

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.Otp, estado);

        Assert.Equal(PasoDeLaVitrina.Codigo, vitrina.Paso);
        Assert.DoesNotContain(Codigo, Todo(vitrina), StringComparison.Ordinal);
        Assert.Contains("asesor", vitrina.Subtitulo!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(180_000m.ToColombianCurrency(), vitrina.Monto);
        Assert.Equal("A 4 cuotas", vitrina.Aviso);
    }

    /// <summary>
    /// Lo que quedo pactado en Credinet manda sobre lo que se eligio en pantalla.
    /// Si difieren, el numero bueno es el de la respuesta.
    /// </summary>
    [Fact]
    public void AlAprobar_MandaLoQueRespondioCredinet()
    {
        var estado = new TransactionStateStore();
        estado.CreditValue = 180_000m;
        estado.Months = 4;
        estado.SetCreatedCredit(CreditoCreado(valor: 175_000, cuotas: 3));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.Confirmacion, estado);

        Assert.Equal(PasoDeLaVitrina.Cierre, vitrina.Paso);
        Assert.Equal(175_000d.ToColombianCurrency(), vitrina.Monto);
        Assert.Equal("A 3 cuotas", vitrina.Aviso);
    }

    [Fact]
    public void UnaSolaCuota_SeDiceEnSingular()
    {
        var estado = new TransactionStateStore();
        estado.SetCreatedCredit(CreditoCreado(valor: 90_000, cuotas: 1));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.Confirmacion, estado);

        Assert.Equal("A 1 cuota", vitrina.Aviso);
    }

    // ------------------------------------------------------------------
    // Abonos (el proceso manual, por el icono)
    // ------------------------------------------------------------------

    /// <summary>
    /// En la pantalla de cobro se muestra el SALDO del credito, no el importe que
    /// el asesor esta digitando: ese cambia mientras se teclea, y un numero que
    /// baila delante del cliente confunde mas de lo que informa.
    /// </summary>
    [Fact]
    public void AlCobrarLaCuota_SeMuestraElSaldo_NoUnImporteEnCurso()
    {
        var estado = new TransactionStateStore();
        estado.SetSelectedCredit(CreditoActivo(saldo: 320_000));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.Pago, estado);

        Assert.Equal("Saldo del crédito", vitrina.EtiquetaDelMonto);
        Assert.Equal(320_000d.ToColombianCurrency(), vitrina.Monto);
    }

    /// <summary>
    /// El total pagado incluye TODO lo que Credinet aplico, no solo el capital.
    /// Es el mismo criterio que encabeza el comprobante impreso: si aca fuera solo
    /// capital, el cliente leeria un numero menor al que acaba de entregar.
    /// </summary>
    [Fact]
    public void AlRegistrarElPago_ElTotalEsLoQueSeEntrego_NoSoloElCapital()
    {
        var estado = new TransactionStateStore();
        estado.SetLastPayment(PagoDe(
            capital: 80_000, intereses: 12_000, mora: 5_000,
            aval: 2_500, cargos: 500, saldo: 150_000));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.ReciboPago, estado);

        Assert.Equal(PasoDeLaVitrina.Cierre, vitrina.Paso);
        Assert.Equal(100_000d.ToColombianCurrency(), vitrina.Monto);
        Assert.Contains(150_000d.ToColombianCurrency(), vitrina.Aviso!, StringComparison.Ordinal);
    }

    [Fact]
    public void CreditoSaldado_SeDiceQueQuedaEnCeros()
    {
        var estado = new TransactionStateStore();
        estado.SetLastPayment(PagoDe(
            capital: 80_000, intereses: 0, mora: 0, aval: 0, cargos: 0, saldo: 0));

        var vitrina = GuionDeLaVitrina.Para(AppRoutes.ReciboPago, estado);

        Assert.Equal("Este crédito queda en ceros", vitrina.Aviso);
    }

    // ------------------------------------------------------------------
    // La ruta que entrega el Shell
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("//splash/capturaCedula", "capturaCedula")]
    [InlineData("/otp", "otp")]
    [InlineData("replicacion?soloCajeros=True", "replicacion")]
    [InlineData("//splash", "splash")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LaUbicacionDelShell_SeReduceAlUltimoTramo(string? ubicacion, string esperado)
        => Assert.Equal(esperado, GuionDeLaVitrina.Normalizar(ubicacion));

    /// <summary>
    /// El Shell no garantiza devolver la ruta con el mismo casing con el que se
    /// registro, asi que el guion no puede depender de eso.
    /// </summary>
    [Fact]
    public void LaRuta_SeReconoceSinImportarMayusculas()
    {
        var conMayusculas = GuionDeLaVitrina.Para("//splash/CAPTURACEDULA", new TransactionStateStore());

        Assert.Equal(PasoDeLaVitrina.Identificando, conMayusculas.Paso);
    }

    // ------------------------------------------------------------------
    // Andamiaje
    // ------------------------------------------------------------------

    /// <summary>
    /// Todas las rutas declaradas en [AppRoutes], por reflexion. Barrer la lista
    /// real y no una copia es lo que hace que una ruta NUEVA entre sola a la
    /// prueba de datos sensibles.
    /// </summary>
    private static IEnumerable<string> TodasLasRutas() =>
        typeof(AppRoutes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static string Todo(VitrinaCliente v) =>
        string.Join(" | ", new[]
        {
            v.Titulo, v.Subtitulo, v.EtiquetaDelMonto, v.Monto, v.Aviso
        }.Where(s => s is not null));

    /// <summary>
    /// Un estado con TODO poblado, incluidos los datos que no pueden salir a la
    /// pantalla. Es el peor caso a proposito: si el guion filtra algo, filtra aca.
    /// </summary>
    private static TransactionStateStore EstadoCompleto()
    {
        var estado = new TransactionStateStore();

        estado.SetValidatedClient(new Client(
            DocumentType.CedulaCiudadania,
            Cedula,
            CreditLimit: 2_000_000,
            AvailableCreditLimit: 1_500_000,
            ValidatedMail: true,
            NewCreditButtonEnabled: true,
            Email: Correo,
            Mobile: Telefono,
            FullName: $"MARCELA {Apellido.ToUpperInvariant()} GOMEZ",
            Defaulter: false,
            CreditLimitIncrease: false,
            IsAvailableCreditLimit: true,
            IsActive: true,
            Status: 1,
            StatusName: "ACTIVO",
            FirstName: "MARCELA",
            SecondName: string.Empty));

        estado.SetActiveDocument(DocumentoDe(240_000m));
        estado.SetSelectedCredit(CreditoActivo(saldo: 320_000));
        estado.SetCreatedCredit(CreditoCreado(valor: 175_000, cuotas: 3));
        estado.SetLastPayment(PagoDe(
            capital: 80_000, intereses: 12_000, mora: 5_000,
            aval: 2_500, cargos: 500, saldo: 150_000));
        estado.CreditValue = 180_000m;
        estado.Months = 4;

        return estado;
    }

    private static SaleDocument DocumentoDe(decimal total) => new()
    {
        Header = new DocumentHeader
        {
            Fields =
            [
                new DocumentField { Key = "NetAmount", Value = total.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new DocumentField { Key = "TaxesAmount", Value = "0" },
                new DocumentField { Key = "SaleId", Value = "4000999" }
            ]
        }
    };

    private static Credit CreditoCreado(double valor, int cuotas) => new(
        TypeDocument: "CC", IdDocument: Cedula, CreditId: "CR-1", CreditNumber: 1,
        EffectiveAnnualRate: 0, DownPayment: 0, TotalFeeValue: 0, CreditValue: valor,
        Fees: cuotas, AssuranceValue: 0, InterestRate: 0, TotalInterestValue: 0,
        TotalDownPayment: 0, FeeCreditValue: 0, AssuranceFeeValue: 0,
        AssuranceTotalValue: 0, AssuranceTaxFeeValue: 0);

    private static ActiveCredit CreditoActivo(double saldo) => new(
        TypeDocument: "CC", IdDocument: Cedula, CreditId: "CR-1", CreditNumber: 1,
        CreateDate: "2026-01-10T00:00:00", CreditValue: 500_000, ArrearsDays: 0,
        MinimumPayment: 90_000, TotalPayment: saldo, FeeValue: 90_000,
        StoreName: "Punto Calle 18", Balance: saldo, DueDate: "2026-10-10T00:00:00");

    private static Payment PagoDe(
        double capital, double intereses, double mora,
        double aval, double cargos, double saldo) => new(
        TypeDocument: "CC", IdDocument: Cedula, CreditId: "CR-1", PaymentId: "PG-1",
        PaymentNumber: 1, CreditValuePaid: capital, InterestValuePaid: intereses,
        ArrearsValuePaid: mora, AssuranceValuePaid: aval, ChargeValuePaid: cargos,
        Balance: saldo, NextDueDate: "2026-11-10T00:00:00", NextMinimumPayment: 90_000);
}
