using System.Text;
using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Platform;

/// <summary>
/// Generación del flujo ESC/POS del comprobante de abono.
///
/// La impresora del POS es una térmica USB de clase 07 que habla ESC/POS, no PDF.
/// Confirmado en el terminal: `SOL801V Printer`, VID 0x0483 / PID 0x5720, bulk OUT
/// 0x01, paquetes de 64 bytes; y HioPos le escribe por USB Host.
/// </summary>
public class EscPosTests
{
    private const byte ESC = 0x1B;
    private const byte GS  = 0x1D;
    private const byte LF  = 0x0A;

    private static StandaloneReceipt SampleReceipt() => new(
        Tienda: "KOAJ Unicentro",
        Fecha: new DateTime(2026, 7, 29, 15, 42, 0),
        Cajero: "Maria Gomez",
        PaymentNumber: "9912",
        CreditNumber: "000123",
        Cliente: "Juan Perez",
        ClienteDocumento: "1026260942",
        CapitalPagado: 200_000m,
        SaldoRestante: 1_300_000m,
        ProximoPago: new DateTime(2026, 8, 29),
        ProximoMinimo: 145_000m);

    private static byte[] BuildSample() =>
        EscPos.Build(ReceiptTextBuilder.BuildLines(SampleReceipt()));

    private static bool Contains(byte[] haystack, params byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                ok = false; break;
            }
            if (ok) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Estructura del flujo
    // ------------------------------------------------------------------

    [Fact]
    public void Arranca_inicializando_la_impresora()
    {
        // ESC @ limpia cualquier formato que dejó el trabajo anterior. Sin esto, si
        // HioPos dejó la impresora en negrita o doble alto, el comprobante sale mal.
        var bytes = BuildSample();

        Assert.Equal(ESC, bytes[0]);
        Assert.Equal(0x40, bytes[1]);
    }

    [Fact]
    public void Selecciona_la_tabla_de_caracteres_multilingue()
    {
        // ESC t 2 = PC850, la que trae acentos y ñ.
        Assert.True(Contains(BuildSample(), ESC, 0x74, 0x02));
    }

    [Fact]
    public void Termina_avanzando_papel_y_cortando()
    {
        // Sin el avance previo, el corte parte el comprobante sobre la última línea.
        var bytes = BuildSample();

        Assert.True(Contains(bytes, ESC, 0x64), "Falta el avance de papel (ESC d n).");

        // GS V 66 0 = corte parcial, y debe ser lo último.
        var final = bytes[^4..];
        Assert.Equal(new byte[] { GS, 0x56, 0x42, 0x00 }, final);
    }

    [Fact]
    public void Cada_linea_termina_en_salto_de_linea()
    {
        var lines = ReceiptTextBuilder.BuildLines(SampleReceipt());
        var bytes = EscPos.Build(lines);

        // Un LF por línea del comprobante.
        var saltos = bytes.Count(b => b == LF);
        Assert.Equal(lines.Count, saltos);
    }

    [Fact]
    public void El_contenido_del_comprobante_viaja_en_el_flujo()
    {
        var texto = EscPos.DecodeForDiagnostics(BuildSample());

        Assert.Contains("COMPROBANTE DE PAGO", texto, StringComparison.Ordinal);
        Assert.Contains("9912", texto, StringComparison.Ordinal);
        Assert.Contains("000123", texto, StringComparison.Ordinal);
        Assert.Contains("Juan Perez", texto, StringComparison.Ordinal);
        Assert.Contains("KOAJ Unicentro", texto, StringComparison.Ordinal);
        Assert.Contains("200.000", texto, StringComparison.Ordinal);
        Assert.Contains("GRACIAS POR SU PAGO", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void La_cedula_completa_del_titular_llega_a_la_impresora()
    {
        // El comprobante es del titular: lleva su documento completo para que pueda
        // verificar que el abono se aplicó a su crédito.
        var texto = EscPos.DecodeForDiagnostics(BuildSample());

        Assert.Contains("1026260942", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void No_lanza_con_una_lista_vacia()
    {
        var bytes = EscPos.Build([]);

        // Aun sin contenido debe inicializar y cortar, para no dejar la impresora
        // en un estado raro.
        Assert.Equal(ESC, bytes[0]);
        Assert.Equal(new byte[] { GS, 0x56, 0x42, 0x00 }, bytes[^4..]);
    }

    // ------------------------------------------------------------------
    // Codificación de caracteres
    // ------------------------------------------------------------------

    [Fact]
    public void El_ASCII_pasa_sin_cambios()
    {
        var bytes = EscPos.EncodeCp850("ABC 123 $.,");
        Assert.Equal(Encoding.ASCII.GetBytes("ABC 123 $.,"), bytes);
    }

    [Theory]
    [InlineData('á', 0xA0)]
    [InlineData('é', 0x82)]
    [InlineData('í', 0xA1)]
    [InlineData('ó', 0xA2)]
    [InlineData('ú', 0xA3)]
    [InlineData('ñ', 0xA4)]
    [InlineData('Ñ', 0xA5)]
    [InlineData('ü', 0x81)]
    public void Los_acentos_y_la_enie_usan_su_byte_de_PC850(char c, byte esperado)
    {
        var bytes = EscPos.EncodeCp850(c.ToString());
        Assert.Single(bytes);
        Assert.Equal(esperado, bytes[0]);
    }

    [Fact]
    public void Un_nombre_con_acentos_se_codifica_completo()
    {
        // Caso real: un cliente llamado José Muñoz.
        var bytes = EscPos.EncodeCp850("Jose Munoz");
        var conAcentos = EscPos.EncodeCp850("José Muñoz");

        Assert.Equal(bytes.Length, conAcentos.Length);
        Assert.Equal(0x82, conAcentos[3]);   // é
        Assert.Equal(0xA4, conAcentos[7]);   // ñ
    }

    [Theory]
    // Caracteres fuera del mapa: se reducen a ASCII en vez de imprimir basura.
    [InlineData('à', (byte)'a')]
    [InlineData('ç', (byte)'c')]
    [InlineData('Ô', (byte)'O')]
    [InlineData('—', (byte)'-')]
    [InlineData('“', (byte)'"')]
    public void Los_caracteres_desconocidos_se_reducen_a_ASCII(char c, byte esperado)
    {
        Assert.Equal(esperado, EscPos.EncodeCp850(c.ToString())[0]);
    }

    [Fact]
    public void Un_caracter_sin_equivalente_se_vuelve_interrogacion()
    {
        // Un emoji o un ideograma no debe romper el flujo ni desalinear el papel:
        // un byte por carácter, siempre.
        var bytes = EscPos.EncodeCp850("漢");
        Assert.Single(bytes);
        Assert.Equal((byte)'?', bytes[0]);
    }

    [Fact]
    public void La_codificacion_conserva_un_byte_por_caracter()
    {
        // Es lo que garantiza que el layout de 42 columnas se mantenga en el papel:
        // si un carácter ocupara dos bytes, la línea se desalinearía.
        const string texto = "José Muñoz — ÁÉÍÓÚ ¿?";
        Assert.Equal(texto.Length, EscPos.EncodeCp850(texto).Length);
    }

    [Fact]
    public void Ninguna_linea_supera_las_42_columnas_en_el_papel()
    {
        var lines = ReceiptTextBuilder.BuildLines(SampleReceipt());

        Assert.All(lines, l =>
            Assert.True(EscPos.EncodeCp850(l).Length <= ReceiptTextBuilder.NumCols,
                $"Linea de {l.Length} columnas: '{l}'"));
    }
}
