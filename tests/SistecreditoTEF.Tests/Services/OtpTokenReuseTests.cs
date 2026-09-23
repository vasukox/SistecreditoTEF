using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Detección de que Credinet devolvió el MISMO código OTP en vez de generar uno
/// nuevo.
///
/// Los valores de estos tests son los observados en el terminal, en cuatro
/// solicitudes SEPARADAS de <c>getCreditToken</c>, todas con
/// <c>tokenGenerated: true</c>:
///
///   09:45:53  RemainingSeconds=249
///   09:47:04  RemainingSeconds=178
///   09:48:10  RemainingSeconds=112
///   09:48:44  RemainingSeconds=77
///
/// El contador desciende: es el mismo token descontando su vida. Un token nuevo lo
/// reiniciaría.
/// </summary>
public class OtpTokenReuseTests
{
    [Fact]
    public void La_primera_solicitud_nunca_se_reporta_como_reutilizada()
    {
        // Sin referencia previa no hay nada que comparar.
        Assert.False(OtpTokenReuse.EsElMismoToken(null, 249));
    }

    [Theory]
    // La secuencia real del terminal.
    [InlineData(249, 178)]
    [InlineData(178, 112)]
    [InlineData(112, 77)]
    public void Un_contador_que_desciende_indica_el_MISMO_token(int anterior, int actual)
    {
        Assert.True(OtpTokenReuse.EsElMismoToken(anterior, actual));
    }

    [Theory]
    // Un token nuevo reinicia el contador: sube o queda igual.
    [InlineData(77, 300)]
    [InlineData(112, 249)]
    [InlineData(180, 180)]
    public void Un_contador_que_sube_o_se_mantiene_indica_un_token_NUEVO(int anterior, int actual)
    {
        Assert.False(OtpTokenReuse.EsElMismoToken(anterior, actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Sin_vigencia_no_se_concluye_reutilizacion(int actual)
    {
        // Un token sin tiempo restante no permite inferir nada: podría ser un token
        // nuevo que ya venció o un dato inconsistente. No se afirma reutilización.
        Assert.False(OtpTokenReuse.EsElMismoToken(249, actual));
    }

    // ------------------------------------------------------------------
    // Aviso al cajero
    // ------------------------------------------------------------------

    [Fact]
    public void Con_token_nuevo_no_hay_aviso()
    {
        Assert.Equal(string.Empty, OtpTokenReuse.Aviso(esElMismoToken: false, 300));
    }

    [Fact]
    public void El_aviso_explica_que_es_el_mismo_codigo_y_cuando_vence()
    {
        var aviso = OtpTokenReuse.Aviso(esElMismoToken: true, 178);

        // Lo que el cajero necesita saber: que NO va a llegar un WhatsApp nuevo.
        Assert.Contains("MISMO codigo", aviso, StringComparison.Ordinal);
        Assert.Contains("2 min 58 s", aviso, StringComparison.Ordinal);
        Assert.Contains("expire", aviso, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Con_menos_de_un_minuto_el_aviso_muestra_solo_segundos()
    {
        var aviso = OtpTokenReuse.Aviso(esElMismoToken: true, 45);

        Assert.Contains("45 s", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain("min", aviso, StringComparison.Ordinal);
    }
}
