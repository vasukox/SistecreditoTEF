using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Certificate pinning por SPKI contra la API de Credinet.
///
/// Se pinea la CLAVE PUBLICA (SubjectPublicKeyInfo), no el certificado completo:
/// sobrevive a la rotacion del certificado mientras no cambie la llave, lo que
/// importa porque api.credinet.co esta detras de CDN.
///
/// Los certificados de prueba se generan en memoria, asi que el test no depende
/// de ningun archivo ni de la red.
/// </summary>
public class CertificatePinningTests
{
    private static X509Certificate2 NewSelfSigned(string cn = "CN=test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
    }

    // ------------------------------------------------------------------
    // La cadena TLS tiene que ser valida SIEMPRE
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    public void Un_certificado_invalido_se_rechaza_aunque_el_pin_coincida(SslPolicyErrors error)
    {
        // Regla de oro: el pinning ENDURECE la validacion TLS, no la reemplaza.
        // Aceptar una cadena invalida porque "el pin coincide" abriria la puerta a
        // un certificado vencido o con nombre equivocado.
        using var cert = NewSelfSigned();
        var pin = CertificatePinning.SpkiSha256(cert);

        Assert.False(CertificatePinning.Validate(cert, chain: null, error, [pin]));
    }

    [Fact]
    public void Un_certificado_invalido_se_rechaza_tambien_sin_pines()
    {
        using var cert = NewSelfSigned();

        Assert.False(CertificatePinning.Validate(
            cert, chain: null, SslPolicyErrors.RemoteCertificateChainErrors, []));
    }

    // ------------------------------------------------------------------
    // Sin pines configurados: validacion TLS estandar
    // ------------------------------------------------------------------

    [Fact]
    public void Sin_pines_configurados_se_acepta_la_validacion_estandar()
    {
        // Comportamiento "seguro por defecto": mientras Sistecredito no entregue
        // los pines, el modulo hace TLS estandar y no rompe la operacion.
        using var cert = NewSelfSigned();

        Assert.True(CertificatePinning.Validate(cert, chain: null, SslPolicyErrors.None, []));
    }

    [Fact]
    public void Una_lista_de_pines_nula_equivale_a_no_tener_pines()
    {
        using var cert = NewSelfSigned();

        Assert.True(CertificatePinning.Validate(cert, chain: null, SslPolicyErrors.None, null!));
    }

    // ------------------------------------------------------------------
    // Con pines configurados
    // ------------------------------------------------------------------

    [Fact]
    public void Con_el_pin_correcto_se_acepta()
    {
        using var cert = NewSelfSigned();
        var pin = CertificatePinning.SpkiSha256(cert);

        Assert.True(CertificatePinning.Validate(cert, chain: null, SslPolicyErrors.None, [pin]));
    }

    [Fact]
    public void Con_un_pin_que_no_corresponde_se_RECHAZA()
    {
        // Este es el caso que el control existe para bloquear: un MITM con un
        // certificado tecnicamente valido (emitido por un CA que el POS confia,
        // p.ej. un proxy de inspeccion corporativo) pero con OTRA llave publica.
        using var real = NewSelfSigned("CN=api.credinet.co");
        using var atacante = NewSelfSigned("CN=api.credinet.co");
        var pinDelReal = CertificatePinning.SpkiSha256(real);

        Assert.False(CertificatePinning.Validate(
            atacante, chain: null, SslPolicyErrors.None, [pinDelReal]));
    }

    [Fact]
    public void Se_acepta_si_coincide_cualquiera_de_los_pines_de_la_lista()
    {
        // Permite pinear el certificado actual y uno de respaldo, que es la
        // practica correcta para poder rotar sin dejar los POS afuera.
        using var actual = NewSelfSigned("CN=actual");
        using var respaldo = NewSelfSigned("CN=respaldo");

        var pines = new[]
        {
            CertificatePinning.SpkiSha256(actual),
            CertificatePinning.SpkiSha256(respaldo)
        };

        Assert.True(CertificatePinning.Validate(actual, null, SslPolicyErrors.None, pines));
        Assert.True(CertificatePinning.Validate(respaldo, null, SslPolicyErrors.None, pines));
    }

    [Fact]
    public void Se_puede_pinear_un_elemento_de_la_cadena_no_solo_el_leaf()
    {
        // Pinear una CA intermedia es mas robusto frente a la rotacion del leaf.
        using var intermedia = NewSelfSigned("CN=CA intermedia");
        using var leaf = NewSelfSigned("CN=api.credinet.co");
        var pinIntermedia = CertificatePinning.SpkiSha256(intermedia);

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
        chain.ChainPolicy.ExtraStore.Add(intermedia);
        chain.Build(intermedia);

        Assert.True(CertificatePinning.Validate(
            leaf, chain, SslPolicyErrors.None, [pinIntermedia]));
    }

    // ------------------------------------------------------------------
    // Estabilidad del calculo del pin
    // ------------------------------------------------------------------

    [Fact]
    public void El_pin_es_estable_y_distinto_por_llave()
    {
        using var a = NewSelfSigned("CN=a");
        using var b = NewSelfSigned("CN=b");

        Assert.Equal(CertificatePinning.SpkiSha256(a), CertificatePinning.SpkiSha256(a));
        Assert.NotEqual(CertificatePinning.SpkiSha256(a), CertificatePinning.SpkiSha256(b));
    }

    [Fact]
    public void El_pin_es_un_SHA256_en_base64()
    {
        // Formato que se entrega en CloudLicense: SHA-256 del SPKI, en base64.
        using var cert = NewSelfSigned();

        var pin = CertificatePinning.SpkiSha256(cert);

        var bytes = Convert.FromBase64String(pin);
        Assert.Equal(32, bytes.Length);
    }
}
