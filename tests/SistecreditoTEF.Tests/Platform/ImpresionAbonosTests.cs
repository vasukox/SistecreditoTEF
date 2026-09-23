using System.Text;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Platform;

/// <summary>
/// QA — IMPRESIÓN DE ABONOS. Los tres printers reportaban éxito SIN imprimir:
///
///   • <c>SunmiPrinter</c> usaba un componente y una acción que no existen
///     (<c>com.sunmi.printerservice</c> + <c>"sunmi.print"</c>; el AIDL real es
///     <c>woyou.aidlservice.jiuv5</c>), vía <c>StartService</c> —que en Android 8+
///     falla contra un servicio de otra app— y devolvía <c>true</c>
///     incondicionalmente tras un <c>Task.Delay(1500)</c>.
///   • <c>AndroidPrintPrinter</c> escribía TEXTO PLANO en el descriptor del
///     trabajo de impresión declarando <c>PrintContentType.Document</c>: el
///     framework espera un PDF, así que salía inválido o en blanco. También
///     devolvía <c>true</c> de inmediato.
///   • <c>AndroidPrintPrinter.IsAvailable</c> es true en CUALQUIER Android, así
///     que <c>PdfReceiptPrinter</c> nunca se alcanzaba: era código muerto.
///
/// Estos tests cubren lo que se puede verificar sin hardware: el layout del
/// comprobante, la validez estructural del PDF y la cadena de fallback.
/// </summary>
public class ImpresionAbonosTests
{
    private static StandaloneReceipt SampleReceipt() => new(
        Tienda: "KOAJ Unicentro",
        Fecha: new DateTime(2026, 7, 28, 15, 42, 0),
        Cajero: "Maria Gomez",
        PaymentNumber: "9912",
        CreditNumber: "000123",
        Cliente: "Juan Perez",
        ClienteDocumento: "1026260942",
        CapitalPagado: 200_000m,
        SaldoRestante: 1_300_000m,
        ProximoPago: new DateTime(2026, 8, 28),
        ProximoMinimo: 145_000m,
        // Un abono realista: Credinet reparte el pago, no lo aplica todo a
        // capital. El cliente entrego 260.000 y solo 200.000 fueron a capital.
        InteresesPagados: 45_000m,
        MoraPagada: 10_000m,
        AvalPagado: 5_000m);

    // ------------------------------------------------------------------
    // Layout del comprobante
    // ------------------------------------------------------------------

    [Fact]
    public void El_comprobante_lleva_los_datos_del_abono()
    {
        var text = ReceiptTextBuilder.BuildText(SampleReceipt());

        Assert.Contains("COMPROBANTE DE PAGO", text);
        Assert.Contains("9912", text);
        Assert.Contains("000123", text);
        Assert.Contains("Juan Perez", text);
        Assert.Contains("KOAJ Unicentro", text);
        Assert.Contains("Maria Gomez", text);
        Assert.Contains("$ 200.000", text);
        Assert.Contains("$ 1.300.000", text);
        Assert.Contains("28/08/2026", text);
        Assert.Contains("GRACIAS POR SU PAGO", text);
    }

    [Fact]
    public void El_comprobante_lleva_la_cedula_COMPLETA_del_titular()
    {
        // Decisión de negocio: el comprobante se entrega al titular del crédito,
        // que necesita verificar que el abono se aplicó a SU documento. Es la
        // práctica habitual en un recibo de pago.
        //
        // El enmascarado sigue vigente donde el dato no cumple función: logs y
        // auditoría (ver PiiMaskTests y HttpLoggingRedaccionTests).
        var text = ReceiptTextBuilder.BuildText(SampleReceipt());

        Assert.Contains("1026260942", text);
    }

    [Fact]
    public void La_cedula_se_imprime_normalizada_sin_separadores()
    {
        // El campo puede venir formateado desde Credinet o desde HioPos.
        var receipt = SampleReceipt() with { ClienteDocumento = "1.026.260.942" };

        var text = ReceiptTextBuilder.BuildText(receipt);

        Assert.Contains("1026260942", text);
        Assert.DoesNotContain("1.026.260.942", text);
    }

    [Fact]
    public void El_comprobante_lleva_el_nombre_del_cliente_y_la_tienda()
    {
        // Los dos datos que el cajero pidió ver en el papel.
        var text = ReceiptTextBuilder.BuildText(SampleReceipt());

        Assert.Contains("Juan Perez", text);
        Assert.Contains("KOAJ Unicentro", text);
    }

    [Fact]
    public void Sin_nombre_de_cliente_el_comprobante_igual_sale_con_la_cedula()
    {
        // La consulta del nombre es best-effort: si falla, el abono no se detiene y
        // el comprobante debe seguir identificando al titular por su documento.
        var receipt = SampleReceipt() with { Cliente = "" };

        var lines = ReceiptTextBuilder.BuildLines(receipt);

        Assert.DoesNotContain(lines, l => l.StartsWith("Cliente", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("C.C.", StringComparison.Ordinal)
                                    && l.Contains("1026260942", StringComparison.Ordinal));
    }

    [Fact]
    public void Ninguna_linea_excede_las_42_columnas_de_la_impresora()
    {
        // Una línea más larga que el ancho del rollo se corta o se envuelve, y el
        // comprobante sale ilegible.
        var lines = ReceiptTextBuilder.BuildLines(SampleReceipt());

        Assert.All(lines, l => Assert.True(l.Length <= ReceiptTextBuilder.NumCols,
            $"Linea de {l.Length} columnas (max {ReceiptTextBuilder.NumCols}): '{l}'"));
    }

    [Fact]
    public void Los_valores_quedan_alineados_al_margen_derecho()
    {
        var lines = ReceiptTextBuilder.BuildLines(SampleReceipt());

        var fila = Assert.Single(lines, l => l.StartsWith("TOTAL PAGADO", StringComparison.Ordinal));
        Assert.Equal(ReceiptTextBuilder.NumCols, fila.Length);
        Assert.EndsWith("$ 260.000", fila, StringComparison.Ordinal);
    }

    /// <summary>
    /// EL DEFECTO QUE ESTO CUBRE.
    ///
    /// El comprobante mostraba una sola cifra, "Capital pagado", que es la parte
    /// aplicada a capital. El cliente entregaba $260.000 y el papel encabezaba con
    /// $200.000, sin decir en ningun lado cuanto se habia pagado. En caja se leia
    /// como un comprobante equivocado, y el cajero no tenia con que responder.
    /// </summary>
    [Fact]
    public void El_comprobante_encabeza_con_el_total_pagado_no_con_el_capital()
    {
        var r = SampleReceipt();
        var lines = ReceiptTextBuilder.BuildLines(r);

        // 200.000 capital + 45.000 intereses + 10.000 mora + 5.000 aval
        Assert.Equal(260_000m, r.TotalPagado);

        var total = Assert.Single(lines, l => l.StartsWith("TOTAL PAGADO", StringComparison.Ordinal));
        Assert.EndsWith("$ 260.000", total, StringComparison.Ordinal);

        // El total va ANTES del desglose: es el numero que se compara con la plata.
        var iTotal = lines.ToList().FindIndex(l => l.StartsWith("TOTAL PAGADO", StringComparison.Ordinal));
        var iCapital = lines.ToList().FindIndex(l => l.Contains("Abonado a capital", StringComparison.Ordinal));
        Assert.True(iTotal < iCapital, "El total tiene que ir arriba del desglose.");

        // Y el desglose tiene que estar, para que el total sea verificable.
        Assert.Contains(lines, l => l.Contains("Abonado a capital") && l.EndsWith("$ 200.000"));
        Assert.Contains(lines, l => l.Contains("Intereses") && l.EndsWith("$ 45.000"));
        Assert.Contains(lines, l => l.Contains("Mora") && l.EndsWith("$ 10.000"));
        Assert.Contains(lines, l => l.Contains("Aval") && l.EndsWith("$ 5.000"));
    }

    /// <summary>
    /// Un abono sin mora no debe imprimir "Mora $ 0": no informa nada y hace dudar
    /// de si el credito tiene mora.
    /// </summary>
    [Fact]
    public void Los_conceptos_en_cero_no_se_imprimen()
    {
        var r = SampleReceipt() with { MoraPagada = 0m, AvalPagado = 0m, OtrosCargos = 0m };

        var lines = ReceiptTextBuilder.BuildLines(r);

        Assert.DoesNotContain(lines, l => l.Contains("Mora", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Aval", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Otros cargos", StringComparison.Ordinal));

        // El total se ajusta solo: 200.000 + 45.000
        Assert.Equal(245_000m, r.TotalPagado);
        Assert.Contains(lines, l => l.StartsWith("TOTAL PAGADO") && l.EndsWith("$ 245.000"));
    }

    /// <summary>
    /// El total NO es un campo que se reciba: se calcula del desglose, asi que no
    /// puede quedar un total que no cuadre con los conceptos que lo componen.
    /// </summary>
    [Fact]
    public void El_total_siempre_cuadra_con_su_desglose()
    {
        var r = SampleReceipt();

        Assert.Equal(
            r.CapitalPagado + r.InteresesPagados + r.MoraPagada + r.AvalPagado + r.OtrosCargos,
            r.TotalPagado);
    }

    [Fact]
    public void Los_campos_vacios_se_omiten_en_vez_de_dejar_una_fila_hueca()
    {
        var receipt = SampleReceipt() with { Cliente = "", Cajero = "" };

        var lines = ReceiptTextBuilder.BuildLines(receipt);

        Assert.DoesNotContain(lines, l => l.StartsWith("Cliente", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("Cajero", StringComparison.Ordinal));
    }

    [Fact]
    public void El_formato_de_moneda_no_depende_del_locale_del_POS()
    {
        // El POS puede tener cualquier cultura; el comprobante debe verse igual.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            var enUs = ReceiptTextBuilder.BuildText(SampleReceipt());
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-CO");
            var esCo = ReceiptTextBuilder.BuildText(SampleReceipt());

            Assert.Equal(enUs, esCo);
            Assert.Contains("$ 200.000", enUs);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ------------------------------------------------------------------
    // PDF real (lo que antes era texto plano)
    // ------------------------------------------------------------------

    [Fact]
    public void El_PDF_generado_es_estructuralmente_valido()
    {
        var pdf = ThermalPdfWriter.Build(ReceiptTextBuilder.BuildLines(SampleReceipt()));
        var raw = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", raw, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", raw, StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog", raw);
        Assert.Contains("/Type /Pages", raw);
        Assert.Contains("/Type /Page", raw);
        Assert.Contains("/BaseFont /Courier", raw);
        Assert.Contains("/Encoding /WinAnsiEncoding", raw);
        Assert.Contains("stream", raw);
        Assert.Contains("endstream", raw);
        Assert.Contains("trailer", raw);
        Assert.Contains("startxref", raw);
    }

    [Fact]
    public void El_xref_apunta_a_offsets_reales_de_cada_objeto()
    {
        // Si los offsets se calculan sobre la longitud del string en vez de los
        // BYTES, un acento en el nombre del cliente corre la tabla y el PDF queda
        // corrupto. Se valida con un nombre acentuado a propósito.
        var receipt = SampleReceipt() with { Cliente = "José Muñoz Ñandú" };
        var pdf = ThermalPdfWriter.Build(ReceiptTextBuilder.BuildLines(receipt));
        var raw = Encoding.Latin1.GetString(pdf);

        var startxrefIndex = raw.LastIndexOf("startxref", StringComparison.Ordinal);
        var offsetText = raw[(startxrefIndex + "startxref".Length)..]
            .Trim().Split('\n')[0].Trim();
        var xrefOffset = int.Parse(offsetText);

        // En ese offset debe empezar literalmente la palabra "xref".
        Assert.Equal("xref", raw.Substring(xrefOffset, 4));

        // Y el offset del objeto 1 debe caer sobre "1 0 obj".
        var firstEntry = raw[(xrefOffset + "xref\n0 6\n0000000000 65535 f \n".Length)..][..10];
        var obj1Offset = int.Parse(firstEntry);
        Assert.Equal("1 0 obj", raw.Substring(obj1Offset, 7));
    }

    [Fact]
    public void El_PDF_escapa_los_parentesis_del_nombre_del_cliente()
    {
        // Un paréntesis sin escapar en un string literal PDF rompe el documento
        // entero: el visor no muestra nada.
        var receipt = SampleReceipt() with { Cliente = "Juan (Pepe) Perez \\ Gomez" };

        var pdf = ThermalPdfWriter.Build(ReceiptTextBuilder.BuildLines(receipt));
        var raw = Encoding.Latin1.GetString(pdf);

        Assert.Contains(@"Juan \(Pepe\) Perez \\ Gomez", raw);
    }

    [Fact]
    public void El_ancho_de_pagina_corresponde_a_un_rollo_de_80mm()
    {
        // 80 mm = 226,77 pt. Sin fijar el tamaño, el framework asume A4 y el
        // comprobante sale en una hoja enorme con el texto en una esquina.
        Assert.InRange(ThermalPdfWriter.PageWidthPoints, 226.0, 227.5);
    }

    [Fact]
    public void El_PDF_no_lanza_con_una_lista_vacia()
    {
        var pdf = ThermalPdfWriter.Build([]);
        Assert.StartsWith("%PDF", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Cadena de fallback (QA: antes no había fallback en runtime)
    // ------------------------------------------------------------------

    private sealed class FakePrinter : IReceiptPrinter
    {
        public FakePrinter(string name, bool available, bool succeeds, bool throws = false)
        {
            Name = name;
            IsAvailable = available;
            _succeeds = succeeds;
            _throws = throws;
        }

        private readonly bool _succeeds;
        private readonly bool _throws;

        public string Name { get; }
        public bool IsAvailable { get; }
        public int Calls { get; private set; }

        public Task<bool> PrintAsync(StandaloneReceipt receipt)
        {
            Calls++;
            if (_throws) throw new InvalidOperationException("printer roto");
            return Task.FromResult(_succeeds);
        }
    }

    [Fact]
    public async Task Si_el_primero_falla_se_intenta_el_siguiente()
    {
        var primero = new FakePrinter("uno", available: true, succeeds: false);
        var segundo = new FakePrinter("dos", available: true, succeeds: true);
        var composite = new CompositeReceiptPrinter([primero, segundo]);

        var ok = await composite.PrintAsync(SampleReceipt());

        Assert.True(ok);
        Assert.Equal(1, primero.Calls);
        Assert.Equal(1, segundo.Calls);
        Assert.Equal("dos", composite.LastUsedPrinter);
    }

    [Fact]
    public async Task Un_printer_que_lanza_no_corta_la_cadena()
    {
        var roto = new FakePrinter("roto", available: true, succeeds: false, throws: true);
        var bueno = new FakePrinter("bueno", available: true, succeeds: true);
        var composite = new CompositeReceiptPrinter([roto, bueno]);

        Assert.True(await composite.PrintAsync(SampleReceipt()));
        Assert.Equal(1, bueno.Calls);
    }

    [Fact]
    public async Task Los_printers_no_disponibles_se_saltan_sin_invocarse()
    {
        var noDisponible = new FakePrinter("apagado", available: false, succeeds: true);
        var bueno = new FakePrinter("bueno", available: true, succeeds: true);
        var composite = new CompositeReceiptPrinter([noDisponible, bueno]);

        Assert.True(await composite.PrintAsync(SampleReceipt()));
        Assert.Equal(0, noDisponible.Calls);
        Assert.Equal(1, bueno.Calls);
    }

    [Fact]
    public async Task Si_el_primero_funciona_no_se_imprime_por_duplicado()
    {
        var primero = new FakePrinter("uno", available: true, succeeds: true);
        var segundo = new FakePrinter("dos", available: true, succeeds: true);
        var composite = new CompositeReceiptPrinter([primero, segundo]);

        Assert.True(await composite.PrintAsync(SampleReceipt()));
        Assert.Equal(1, primero.Calls);
        Assert.Equal(0, segundo.Calls);   // NO debe salir dos veces
    }

    [Fact]
    public async Task Si_todos_fallan_se_reporta_false_no_un_exito_falso()
    {
        // Este es el corazón del arreglo: antes se devolvía true sin imprimir, así
        // que el cajero cerraba la venta creyendo que el cliente tenía su
        // comprobante.
        var composite = new CompositeReceiptPrinter(
        [
            new FakePrinter("uno", available: true, succeeds: false),
            new FakePrinter("dos", available: true, succeeds: false)
        ]);

        Assert.False(await composite.PrintAsync(SampleReceipt()));
    }

    [Fact]
    public async Task Sin_printers_registrados_reporta_false()
    {
        var composite = new CompositeReceiptPrinter([]);

        Assert.False(composite.IsAvailable);
        Assert.False(await composite.PrintAsync(SampleReceipt()));
    }

    [Fact]
    public async Task Un_printer_deshabilitado_delega_sin_reportar_exito_falso()
    {
        // Es el contrato que ahora cumple [SunmiPrinter] mientras no exista el
        // binding AIDL oficial: declararse NO disponible y nunca devolver true, de
        // modo que la cadena siga al printer que sí funciona.
        //
        // [SunmiPrinter] y [AndroidPrintPrinter] dependen de Android y no se pueden
        // instanciar acá; los valida el build del APK. Lo que se verifica aquí es
        // la regla de la cadena, que es donde estaba el defecto.
        var deshabilitado = new FakePrinter("sunmi-off", available: false, succeeds: false);
        var real = new FakePrinter("android-print", available: true, succeeds: true);
        var composite = new CompositeReceiptPrinter([deshabilitado, real]);

        Assert.True(await composite.PrintAsync(SampleReceipt()));
        Assert.Equal(0, deshabilitado.Calls);
        Assert.Equal("android-print", composite.LastUsedPrinter);
    }
}
