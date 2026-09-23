using Xunit;

namespace SistecreditoTEF.Maui.Tests.Resources;

/// <summary>
/// El icono del APK: el archivo que Android muestra en el cajon de aplicaciones.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POR QUE UN ARCHIVO DE IMAGEN MERECE PRUEBAS
/// ─────────────────────────────────────────────────────────────────────────────────
/// El archivo que llego para esto el 23/09/2026 se llamaba "icon.png" y por dentro
/// era WebP: empezaba en "RIFF", no en la firma de PNG. Windows lo abre igual —el
/// visor no mira la extension— asi que a simple vista no habia nada raro.
///
/// Ese error no avisa: no hay compilacion que falle ni excepcion que se registre. El
/// sintoma aparece al final del todo, con el APK ya firmado e instalado en una caja y
/// alguien mirando un icono en blanco. Entre que se mete el archivo equivocado y que
/// alguien lo nota pueden pasar semanas, y en el medio se distribuye.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LO QUE ESTO NO COMPRUEBA
/// ─────────────────────────────────────────────────────────────────────────────────
/// Que la marca quepa en la ZONA SEGURA del icono adaptativo —el circulo interior de
/// ~66% del lienzo, unico lugar que Android garantiza que no recorta— se cuida al
/// generar el archivo, no aca: verificarlo exige decodificar el PNG, y para eso habria
/// que meter un decodificador de imagenes en un proyecto de pruebas de logica pura.
///
/// La regla esta escrita donde se aplica: ver el comentario del MauiIcon en el csproj.
/// Si alguien reemplaza el icono por uno a sangre, estas pruebas pasan y el recorte se
/// va a notar en la caja.
/// </summary>
public class IconoDeLaAppTests
{
    /// <summary>Los ocho bytes con los que empieza todo PNG.</summary>
    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string RutaDelIcono()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SistecreditoTEF.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName,
            "src", "SistecreditoTEF.Maui", "Resources", "AppIcon", "sistecredito_icon.png");
    }

    [Fact]
    public void El_icono_existe_donde_lo_busca_el_csproj()
    {
        Assert.True(File.Exists(RutaDelIcono()),
            "El MauiIcon del csproj apunta a Resources\\AppIcon\\sistecredito_icon.png.");
    }

    /// <summary>
    /// LA PRUEBA QUE IMPORTA. Un WebP renombrado a .png pasa cualquier revision
    /// visual y no rompe ninguna compilacion.
    /// </summary>
    [Fact]
    public void El_icono_es_un_PNG_de_verdad_y_no_un_WebP_renombrado()
    {
        var leidos = new byte[8];
        using (var f = File.OpenRead(RutaDelIcono()))
        {
            Assert.Equal(8, f.Read(leidos, 0, 8));
        }

        // "RIFF" es el arranque de un WebP: el error concreto que ya paso una vez.
        Assert.False(
            leidos[0] == (byte)'R' && leidos[1] == (byte)'I' &&
            leidos[2] == (byte)'F' && leidos[3] == (byte)'F',
            "El icono es un RIFF (WebP) con extension .png. Convertilo a PNG de verdad.");

        Assert.Equal(FirmaPng, leidos);
    }

    /// <summary>
    /// Cuadrado y grande.
    ///
    /// Cuadrado porque el resizetizer lo encuadra: una fuente que no lo es llega
    /// deformada o recortada de un lado. Grande porque de esta unica fuente salen
    /// todas las densidades, y partir de algo chico da un icono borroso en xxxhdpi.
    /// </summary>
    [Fact]
    public void El_icono_es_cuadrado_y_bastante_grande()
    {
        var (ancho, alto) = MedirPng(RutaDelIcono());

        Assert.Equal(ancho, alto);
        Assert.True(ancho >= 512, $"El icono mide {ancho}px; se esperaban 512 o mas.");
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // El encabezado de un PNG son dos enteros en una posicion fija (IHDR, big-endian):
    // leerlos a mano es mas corto que justificar una dependencia de imagenes en un
    // proyecto de pruebas que corre en el agente de CI.

    private static (int Ancho, int Alto) MedirPng(string ruta)
    {
        var cabecera = new byte[24];
        using (var f = File.OpenRead(ruta))
        {
            Assert.Equal(24, f.Read(cabecera, 0, 24));
        }

        return (LeerBigEndian(cabecera, 16), LeerBigEndian(cabecera, 20));
    }

    private static int LeerBigEndian(byte[] b, int desde) =>
        (b[desde] << 24) | (b[desde + 1] << 16) | (b[desde + 2] << 8) | b[desde + 3];
}
