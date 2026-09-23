using System.Reflection;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Que build es este APK, y contra que ambiente apunta.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE HACE FALTA
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>ApplicationVersion</c> esta fijo en 1 porque es un valor de CONTRATO con ICG
/// (ver [HioposActions.ModuleVersion]) y no puede moverse por release. Efecto
/// colateral: ninguna caja puede decir que build tiene. Hasta ahora se deducia por
/// la fecha del archivo .apk en la maquina de quien compilo, lo cual no sirve
/// cuando la pregunta llega desde una de 512 tiendas.
///
/// El sello lo inyecta el .csproj en cada compilacion y viaja dentro del APK. No
/// toca la version que ve HioPos.
///
/// Se lee de los atributos del ensamblado y se cachea: la reflexion es barata pero
/// esto se consulta en cada fallo y en cada arranque.
/// </summary>
public static class BuildInfo
{
    private static string? _descripcion;

    /// <summary>Marca de compilacion, p. ej. <c>20260923-1412</c>.</summary>
    public static string Sello => Leer("BuildStamp") ?? "sin-sello";

    /// <summary>Ambiente empaquetado: <c>production</c> o <c>sandbox</c>.</summary>
    public static string Ambiente => Leer("BuildAmbiente") ?? "desconocido";

    /// <summary>
    /// Una linea para el log y para la pantalla de configuracion. Lleva el ambiente
    /// porque los dos APK se llaman igual y se ven igual; la unica forma de que un
    /// terminal de pruebas con el APK productivo se note es que lo diga.
    /// </summary>
    public static string Descripcion => _descripcion ??= $"{Sello} · {Ambiente}";

    private static string? Leer(string clave)
    {
        try
        {
            return typeof(BuildInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, clave, StringComparison.Ordinal))
                ?.Value;
        }
        catch
        {
            return null;
        }
    }
}
