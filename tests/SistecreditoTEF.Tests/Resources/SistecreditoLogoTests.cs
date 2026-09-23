using SistecreditoTEF.Maui.Resources.Images;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Resources;

/// <summary>
/// El logo que el modulo le entrega a HioPos en <c>GET_CUSTOM_PARAMS</c> tiene que ser
/// EL MISMO archivo que <c>Resources\AppIcon\koaj_logo.png</c>.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LA DERIVA QUE ESTO ATRAPA
/// ─────────────────────────────────────────────────────────────────────────────────
/// El logo vivia como una cadena base64 dentro del codigo: una segunda copia de ese
/// PNG. Eran identicos, pero nada lo garantizaba. Reemplazar el PNG dejaba a HioPos
/// mostrando el logo viejo, sin error ni aviso, y solo se descubria mirando la
/// pantalla de medios de pago del POS.
///
/// Ahora hay una sola fuente y este test verifica que sigue habiendo una sola.
///
/// OJO: este logo YA NO es el icono del launcher. Lo fue hasta el 23/09/2026, cuando
/// el icono del APK paso a ser la marca Sistecredito ([IconoDeLaAppTests]). Son dos
/// archivos con dos destinos distintos: este lo muestra el POS junto al medio de
/// pago; el otro lo muestra Android en el cajon de aplicaciones.
/// </summary>
public class SistecreditoLogoTests
{
    /// <summary>Ruta del PNG en el repo, relativa a la raiz del proyecto de tests.</summary>
    private static string RutaDelPng()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        // Se sube hasta encontrar la raiz del repo (donde vive el .sln) para no
        // depender de la profundidad de bin/Debug/netX.
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SistecreditoTEF.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName,
            "src", "SistecreditoTEF.Maui", "Resources", "AppIcon", "koaj_logo.png");
    }

    [Fact]
    public void El_logo_se_carga_desde_el_recurso_embebido()
    {
        var bytes = SistecreditoLogo.PngBytes;

        Assert.NotEmpty(bytes);
    }

    /// <summary>
    /// Firma PNG: 89 50 4E 47 0D 0A 1A 0A. Si esto falla, lo que se le esta mandando
    /// a HioPos no es una imagen y el POS no va a poder dibujarla.
    /// </summary>
    [Fact]
    public void Lo_que_se_manda_es_un_PNG_valido()
    {
        var bytes = SistecreditoLogo.PngBytes;

        Assert.True(bytes.Length > 8, "El logo es demasiado corto para ser un PNG.");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                     bytes.Take(8).ToArray());
    }

    /// <summary>
    /// LA PRUEBA QUE IMPORTA: el logo embebido es exactamente el archivo del repo.
    /// Si alguien cambia koaj_logo.png y el embebido no lo sigue, esto falla.
    /// </summary>
    [Fact]
    public void El_logo_embebido_es_el_MISMO_archivo_del_icono()
    {
        var ruta = RutaDelPng();
        Assert.True(File.Exists(ruta), $"No se encontro el PNG del icono en {ruta}.");

        var enDisco = File.ReadAllBytes(ruta);
        var embebido = SistecreditoLogo.PngBytes;

        Assert.Equal(enDisco.Length, embebido.Length);
        Assert.Equal(enDisco, embebido);
    }

    /// <summary>
    /// El logo viaja a HioPos como extra binario de un Intent. Los extras tienen un
    /// techo de tamano (TransactionTooLargeException alrededor de 1 MB para toda la
    /// transaccion), y en la respuesta tambien van los comprobantes. Un logo de mas
    /// de 256 KB es una bomba de tiempo para el setResult.
    /// </summary>
    [Fact]
    public void El_logo_no_es_tan_grande_como_para_romper_el_Intent()
    {
        Assert.InRange(SistecreditoLogo.PngBytes.Length, 1, 256 * 1024);
    }

    /// <summary>
    /// Se cachea: [GET_CUSTOM_PARAMS] puede llegar varias veces y no tiene sentido
    /// releer el recurso en cada una.
    /// </summary>
    [Fact]
    public void El_logo_se_lee_una_sola_vez()
    {
        Assert.Same(SistecreditoLogo.PngBytes, SistecreditoLogo.PngBytes);
    }
}
