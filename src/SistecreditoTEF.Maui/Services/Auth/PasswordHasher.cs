using System.Security.Cryptography;

namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Derivacion y verificacion de las claves de los cajeros y del PIN de
/// administrador.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE NO SE GUARDA LA CLAVE, NI SIQUIERA CIFRADA
/// ─────────────────────────────────────────────────────────────────────────────
/// Una clave guardada de forma reversible se puede recuperar: basta con la llave.
/// En un POS con adb habilitado para soporte, y con la base de datos accesible al
/// que tenga acceso fisico al terminal, eso significa que las claves de todos los
/// cajeros son extraibles. Y como las personas reutilizan claves, el daño sale del
/// alcance de esta aplicacion.
///
/// Asi que no se guarda la clave: se guarda una DERIVACION que no se puede
/// revertir. Para verificar se deriva de nuevo lo que el cajero escribio y se
/// comparan las derivaciones.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// DECISIONES Y POR QUE
/// ─────────────────────────────────────────────────────────────────────────────
///   • PBKDF2-HMAC-SHA256. Viene en la plataforma, sin dependencias nuevas
///     —importante porque el stack esta restringido— y es adecuado para esto.
///   • Salt ALEATORIO POR CLAVE, de 16 bytes. Sin salt, dos cajeros con la misma
///     clave producen la misma derivacion: se ve en la base quien comparte clave,
///     y una sola tabla precalculada sirve para todos.
///   • 210.000 iteraciones. Encarece el intento por fuerza bruta. En un POS de
///     estos el calculo tarda decenas de milisegundos, imperceptible al validar
///     una clave, y esta lejos de ser gratis repetido millones de veces.
///   • Comparacion en tiempo CONSTANTE ([CryptographicOperations.FixedTimeEquals]).
///     Un `==` sobre los bytes corta en el primer byte distinto, y ese tiempo
///     filtra informacion sobre la derivacion correcta.
///   • El numero de iteraciones y la version van GUARDADOS en el hash. Permite
///     subirlas mas adelante sin invalidar las claves existentes.
///
/// Formato del hash: <c>v1$iteraciones$saltBase64$derivacionBase64</c>
/// </summary>
public static class PasswordHasher
{
    private const string Version = "v1";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;
    private const char Separator = '$';

    /// <summary>Largo minimo de una clave o PIN. Regla de producto, no criptografica.</summary>
    public const int MinLength = 4;

    /// <summary>
    /// Deriva la clave y devuelve la cadena a guardar. Nunca devuelve la clave.
    /// </summary>
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("La clave no puede estar vacia.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, DefaultIterations);

        return string.Join(Separator,
            Version,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// ¿La clave corresponde al hash guardado?
    ///
    /// Devuelve false ante cualquier hash ilegible en vez de lanzar: un registro
    /// corrupto tiene que impedir el acceso, no tumbar la aplicacion en la
    /// pantalla de ingreso.
    /// </summary>
    public static bool Verify(string? password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split(Separator);
        if (parts.Length != 4 || parts[0] != Version) return false;

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0) return false;

        var actual = Derive(password, salt, iterations, expected.Length);

        // Tiempo constante: un `==` filtraria cuantos bytes coinciden.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);

    // ------------------------------------------------------------------
    // Versiones asincronicas
    // ------------------------------------------------------------------

    /// <summary>
    /// <see cref="Hash"/> fuera del hilo de UI.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE IMPORTA EN UN POS
    /// ─────────────────────────────────────────────────────────────────────────
    /// 210.000 iteraciones de PBKDF2 en el ARM de un terminal cuestan cientos de
    /// milisegundos. Corriendo en el hilo principal —como estaba— la pantalla se
    /// CONGELA en cada ingreso de cajero: el indicador de actividad no gira, los
    /// toques se acumulan en la cola y el cajero cree que la app se colgo y vuelve
    /// a tocar.
    ///
    /// El costo es deliberado (es lo que encarece el ataque por fuerza bruta), asi
    /// que no se baja: se mueve de hilo.
    /// </summary>
    public static Task<string> HashAsync(string password) =>
        Task.Run(() => Hash(password));

    /// <summary><see cref="Verify"/> fuera del hilo de UI. Ver [HashAsync].</summary>
    public static Task<bool> VerifyAsync(string? password, string? storedHash) =>
        Task.Run(() => Verify(password, storedHash));
}
