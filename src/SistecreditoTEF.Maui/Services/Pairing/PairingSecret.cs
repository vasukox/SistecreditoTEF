using System.Security.Cryptography;
using System.Text;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// El codigo de seis digitos y la criptografia que lo respalda.
///
/// El codigo NO lleva la configuracion: la AUTORIZA. Los datos viajan por la red
/// local de la tienda; el codigo solo demuestra que quien pide estaba parado
/// frente a la caja que ya esta montada.
/// </summary>
public static class PairingSecret
{
    public const int CodeLength = 6;

    /// <summary>
    /// Intentos fallidos antes de quemar el codigo. Es lo que hace que adivinar uno
    /// de un millon no sea una opcion: quien cuenta los intentos es quien tiene el
    /// secreto, no quien lo adivina.
    /// </summary>
    public const int MaxAttempts = 3;

    private const int Iterations = 310_000;   // OWASP, PBKDF2-HMAC-SHA256
    private const int KeySizeBytes = 32;
    private const int ChallengeSizeBytes = 16;
    private const int GcmNonceSizeBytes = 12;
    private const int GcmTagSizeBytes = 16;

    /// <summary>
    /// Codigo de seis digitos.
    ///
    /// <see cref="RandomNumberGenerator"/> y NO <c>Random</c>: este numero es lo
    /// unico que separa el padron de cajeros de la tienda de cualquiera que este en
    /// la misma red. Un generador predecible lo vuelve adivinable sin necesidad de
    /// fuerza bruta.
    /// </summary>
    public static string GenerateCode()
    {
        var max = (int)Math.Pow(10, CodeLength);
        return RandomNumberGenerator.GetInt32(max).ToString($"D{CodeLength}");
    }

    public static byte[] GenerateChallenge() =>
        RandomNumberGenerator.GetBytes(ChallengeSizeBytes);

    /// <summary>
    /// Del codigo salen DOS llaves: una para probar que se lo conoce y otra para
    /// cifrar el sobre.
    ///
    /// Usar la misma para las dos cosas significaria que la prueba —que viaja en
    /// claro por la red— se calcula con la llave que protege el padron.
    ///
    /// El reto hace de sal, asi que dos ventanas con el mismo codigo derivan llaves
    /// distintas.
    /// </summary>
    private static (byte[] Proof, byte[] Encryption) DeriveKeys(string code, byte[] challenge)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(code.Trim()),
            challenge,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes * 2);

        return (material[..KeySizeBytes], material[KeySizeBytes..]);
    }

    public static byte[] ComputeProof(string code, byte[] challenge)
    {
        var (proofKey, _) = DeriveKeys(code, challenge);
        return HMACSHA256.HashData(proofKey, challenge);
    }

    /// <summary>
    /// Verifica la prueba en TIEMPO FIJO.
    ///
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> y no una comparacion
    /// normal: una que corta en el primer byte distinto tarda distinto segun
    /// cuantos acerto, y eso permite deducir la prueba byte a byte midiendo el
    /// tiempo de respuesta.
    /// </summary>
    public static bool VerifyProof(string code, byte[] challenge, byte[] candidate)
    {
        if (candidate.Length == 0) return false;
        var expected = ComputeProof(code, challenge);
        return CryptographicOperations.FixedTimeEquals(expected, candidate);
    }

    /// <summary>
    /// Cifra el sobre con AES-GCM, que ademas AUTENTICA: si algo en la red altera
    /// un byte, el descifrado falla en vez de entregar un padron corrupto que la
    /// caja guardaria como bueno.
    ///
    /// Sale un solo bloque <c>nonce | tag | cifrado</c>, para que el transporte
    /// mande una cosa y no tres campos que alguien pueda reordenar.
    /// </summary>
    public static byte[] Encrypt(string code, byte[] challenge, string plaintext)
    {
        var (_, key) = DeriveKeys(code, challenge);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSizeBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[GcmTagSizeBytes];

        using (var aes = new AesGcm(key, GcmTagSizeBytes))
            aes.Encrypt(nonce, plain, cipher, tag);

        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        cipher.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    /// <summary>
    /// Devuelve <c>null</c> en los tres casos malos —codigo equivocado, paquete
    /// alterado, paquete truncado— porque para el que esta instalando terminan
    /// igual y ninguno puede tumbar la app.
    /// </summary>
    public static string? Decrypt(string code, byte[] challenge, byte[] payload)
    {
        if (payload.Length < GcmNonceSizeBytes + GcmTagSizeBytes) return null;

        try
        {
            var (_, key) = DeriveKeys(code, challenge);

            var nonce = payload[..GcmNonceSizeBytes];
            var tag = payload[GcmNonceSizeBytes..(GcmNonceSizeBytes + GcmTagSizeBytes)];
            var cipher = payload[(GcmNonceSizeBytes + GcmTagSizeBytes)..];
            var plain = new byte[cipher.Length];

            using (var aes = new AesGcm(key, GcmTagSizeBytes))
                aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Longitudes imposibles en un paquete manipulado.
            return null;
        }
    }
}
