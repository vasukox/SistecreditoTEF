using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// HU8-973: certificate pinning por SPKI (SubjectPublicKeyInfo, SHA-256 base64).
///
/// Se pinea la CLAVE PÚBLICA (no el cert completo), que sobrevive a la rotación
/// del certificado mientras no cambie la llave — más robusto para servicios tras
/// CDN (api.credinet.co está tras Cloudflare). Se puede pinear el leaf o una CA.
///
/// SEGURO POR DEFECTO: si la lista de pines está vacía, se comporta como la
/// validación TLS estándar (no rompe nada). Solo cuando hay pines configurados
/// se exige que la cadena, además de ser válida, coincida con algún pin.
/// </summary>
public static class CertificatePinning
{
    public static bool Validate(
        X509Certificate2? cert,
        X509Chain? chain,
        SslPolicyErrors errors,
        IReadOnlyList<string> pins)
    {
        // 1) La cadena TLS debe ser válida SIEMPRE (no se acepta cert inválido).
        if (errors != SslPolicyErrors.None)
            return false;

        // 2) Sin pines configurados → validación estándar (ya pasó) = OK.
        if (pins is null || pins.Count == 0)
            return true;

        // 3) Con pines: el SPKI del leaf o de algún elemento de la cadena
        //    (permite pinear a intermedia/raíz) debe estar en la lista.
        if (cert is not null && pins.Contains(SpkiSha256(cert)))
            return true;

        if (chain is not null)
        {
            foreach (var element in chain.ChainElements)
            {
                if (pins.Contains(SpkiSha256(element.Certificate)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>SHA-256(base64) del SubjectPublicKeyInfo del certificado.</summary>
    public static string SpkiSha256(X509Certificate2 cert)
    {
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        return Convert.ToBase64String(SHA256.HashData(spki));
    }
}
