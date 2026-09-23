using System.Reflection;

namespace SistecreditoTEF.Maui.Resources.Images;

/// <summary>
/// Logo KOAJ (Permoda) que el modulo entrega en <c>GET_CUSTOM_PARAMS</c>. Es el logo
/// que HioPosCloud muestra junto al medio de pago Sistecredito.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// UNA SOLA FUENTE: EL ARCHIVO
/// ─────────────────────────────────────────────────────────────────────────────────
/// Antes esto era una cadena base64 pegada en el codigo, o sea una SEGUNDA COPIA de
/// <c>Resources\AppIcon\koaj_logo.png</c>. Se comprobo que eran byte por byte
/// identicos (2017 bytes, 320x320), pero nada lo garantizaba: reemplazar el PNG del
/// icono dejaba a HioPos mostrando el logo anterior, sin ningun error ni aviso.
///
/// Ahora se lee el MISMO archivo que usa el icono del launcher, embebido como
/// EmbeddedResource (ver el csproj). Cambiar el PNG cambia los dos lugares.
///
/// Por que EmbeddedResource y no MauiAsset: el logo se pide en
/// <c>GET_CUSTOM_PARAMS</c>, que HioPos puede disparar durante el arranque, cuando el
/// IFileSystem y el AssetManager de Android todavia no estan disponibles. Es el mismo
/// motivo por el que <c>appsettings.json</c> va embebido.
/// </summary>
public static class SistecreditoLogo
{
    /// <summary>
    /// Bytes del PNG. Se cargan una sola vez; [Lazy] evita hacerlo en el arranque de
    /// la app cuando quizas nadie va a pedir el logo.
    /// </summary>
    public static byte[] PngBytes => _bytes.Value;

    private static readonly Lazy<byte[]> _bytes = new(Cargar);

    /// <summary>Nombre logico del recurso, fijado en el csproj.</summary>
    public const string ResourceName = "koaj_logo.png";

    private static byte[] Cargar()
    {
        try
        {
            var assembly = typeof(SistecreditoLogo).Assembly;

            // Se busca por SUFIJO y no por nombre exacto, por el mismo motivo que en
            // [LoadAppSettingsFromAsset]: el nombre real del recurso depende del
            // LogicalName y del RootNamespace, y buscar por sufijo sobrevive a los dos.
            var nombre = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase));

            if (nombre is null)
            {
                Common.AppLogger.E("SistecreditoLogo",
                    $"El recurso '{ResourceName}' no esta embebido en el assembly. " +
                    "HioPos va a mostrar un icono generico junto al medio de pago.");
                return [];
            }

            using var stream = assembly.GetManifestResourceStream(nombre);
            if (stream is null)
            {
                Common.AppLogger.E("SistecreditoLogo",
                    $"No se pudo abrir el recurso '{nombre}'.");
                return [];
            }

            using var memoria = new MemoryStream();
            stream.CopyTo(memoria);
            var bytes = memoria.ToArray();

            Common.AppLogger.I("SistecreditoLogo",
                $"Logo cargado desde EmbeddedResource ({nombre}, {bytes.Length} bytes).");
            return bytes;
        }
        catch (Exception ex)
        {
            // Un logo que no carga NO puede tumbar el handshake con HioPos: sin logo
            // el POS muestra un icono generico, que es una molestia estetica. Una
            // excepcion aca dejaria el medio de pago inutilizable.
            Common.AppLogger.E("SistecreditoLogo", "Error cargando el logo del modulo.", ex);
            return [];
        }
    }
}
