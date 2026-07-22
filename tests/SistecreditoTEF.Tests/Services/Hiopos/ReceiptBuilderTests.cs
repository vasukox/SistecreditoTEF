using System;
using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Hiopos;

/// <summary>
/// HU-134: el comprobante de credito debe ser un VOUCHER, no una factura.
/// Un header tipo empresa ("PERMODA - SISTECREDITO") hace que HioPosCloud lo
/// trate como documento nuevo y lo reenvie a DIAN; un CUT_PAPER en la mitad
/// parte el voucher en dos tiras. Ademas los datos van alineados a 42 cols.
/// </summary>
public class ReceiptBuilderTests
{
    private const int NumCols = 42;

    private static string BuildSample()
    {
        var sut = new ReceiptBuilder();
        return sut.BuildMerchantReceipt(
            fecha: new DateTime(2026, 7, 14, 10, 30, 0),
            creditNumber: "000123",
            cliente: "Juan Perez",
            documento: "1026260942",
            valorFinanciado: "$ 1.500.000",
            cuotaInicial: "$ 100.000",
            cuotaMensual: "$ 145.000",
            plazoCuotas: 12,
            tasaEfectivaAnual: 28.32,
            primeraFechaPago: "14/08/2026",
            tienda: "Permoda");
    }

    [Fact]
    public void MerchantReceipt_usa_header_de_voucher_no_de_factura()
    {
        var xml = BuildSample();

        Assert.Contains("COMPROBANTE DE CREDITO", xml);
        Assert.DoesNotContain("SISTECREDITO", xml);
    }

    [Fact]
    public void MerchantReceipt_corta_papel_una_sola_vez_al_final()
    {
        var xml = BuildSample();

        var cortes = xml.Split("CUT_PAPER").Length - 1;
        Assert.Equal(1, cortes);
    }

    [Fact]
    public void MerchantReceipt_muestra_cedula_completa()
    {
        var xml = BuildSample();

        Assert.Contains("1026260942", xml);
    }

    [Fact]
    public void MerchantReceipt_incluye_datos_del_credito()
    {
        var xml = BuildSample();

        Assert.Contains("Numero de Credito", xml);
        Assert.Contains("000123", xml);
        Assert.Contains("$ 1.500.000", xml);
        Assert.Contains("$ 145.000", xml);
        Assert.Contains("Plazo", xml);
        Assert.Contains("12 cuotas", xml);
        Assert.Contains("28.32%", xml);
    }

    [Fact]
    public void MerchantReceipt_cada_ReceiptLine_va_en_una_sola_linea()
    {
        // El parser de recibos de HioPos es por-linea: si un <ReceiptLine>
        // se reparte en varias lineas fisicas, deja de imprimir el resto.
        var xml = BuildSample();

        var lineas = xml.Replace("\r\n", "\n").Split('\n');
        foreach (var linea in lineas)
        {
            if (!linea.Contains("<ReceiptLine")) continue;

            var esAutocerrado = linea.Contains("/>");
            var esCerradoEnLinea = linea.Contains("</ReceiptLine>");
            Assert.True(esAutocerrado || esCerradoEnLinea,
                $"ReceiptLine partido en varias lineas: '{linea.Trim()}'");
        }
    }

    [Fact]
    public void MerchantReceipt_toda_linea_de_texto_lleva_Formats()
    {
        // Manual HioPos pag. 25: una linea de texto sin <Formats> que
        // englobe su contenido se ignora en silencio (solo salian las BOLD).
        var xml = BuildSample();

        var lineas = xml.Replace("\r\n", "\n").Split('\n');
        foreach (var linea in lineas)
        {
            if (!linea.Contains("type=\"TEXT\"")) continue;
            Assert.True(linea.Contains("<Formats>"),
                $"ReceiptLine de texto sin <Formats>: '{linea.Trim()}'");
        }
    }

    [Fact]
    public void MerchantReceipt_alinea_valor_al_margen_derecho()
    {
        var xml = BuildSample();

        // "Valor financiado" (16) + relleno + "$ 1.500.000" (11) = 42 cols.
        const string label = "Valor financiado";
        const string value = "$ 1.500.000";
        var gap = new string(' ', NumCols - label.Length - value.Length);

        Assert.Contains(label + gap + value, xml);
    }
}
