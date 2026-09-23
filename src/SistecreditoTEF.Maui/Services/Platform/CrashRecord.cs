using System.Globalization;
using System.Text;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>De donde salio el fallo. Cambia lo que se puede hacer con el.</summary>
public enum CrashOrigin
{
    /// <summary>Excepcion no atrapada en el hilo de Android. Se puede sobrevivir.</summary>
    Android,

    /// <summary>Excepcion no atrapada del runtime .NET. El proceso ya se esta yendo.</summary>
    Runtime,

    /// <summary>Task que fallo y nadie observo. Se marca observada y se sigue.</summary>
    TaskNoObservada
}

/// <summary>
/// Un fallo, listo para guardarse y para leerse despues en una tienda.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// NO LLEVA DATOS DEL CLIENTE
/// ─────────────────────────────────────────────────────────────────────────────
/// Solo tipo de excepcion, mensaje, pila y contexto del terminal. El mensaje de
/// una excepcion puede traer datos si alguien los interpola —por eso se recorta y
/// por eso este archivo se guarda en el almacenamiento privado de la app, no en la
/// tarjeta— pero deliberadamente no se agrega cedula, nombre ni credito.
/// </summary>
public sealed record CrashRecord(
    DateTimeOffset Cuando,
    CrashOrigin Origen,
    string Tipo,
    string Mensaje,
    string? Pila,
    string? Pantalla,
    string? Build)
{
    /// <summary>
    /// Tope de la pila. Una pila de .NET en Android puede pasar los 20 KB, y lo que
    /// resuelve un caso son los primeros marcos: guardar todo solo llena el disco
    /// del terminal.
    /// </summary>
    public const int MaxPila = 4000;

    public static CrashRecord De(
        Exception ex, CrashOrigin origen, string? pantalla = null, string? build = null,
        DateTimeOffset? cuando = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var pila = ex.ToString();
        if (pila.Length > MaxPila) pila = pila[..MaxPila] + "\n… (recortado)";

        return new CrashRecord(
            cuando ?? DateTimeOffset.Now,
            origen,
            ex.GetType().FullName ?? ex.GetType().Name,
            ex.Message ?? string.Empty,
            pila,
            pantalla,
            build);
    }

    /// <summary>Texto plano, para leerlo con <c>adb shell cat</c> desde una tienda.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append("Cuando  : ").AppendLine(Cuando.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        sb.Append("Origen  : ").AppendLine(Origen.ToString());
        sb.Append("Build   : ").AppendLine(Build ?? "(desconocido)");
        sb.Append("Pantalla: ").AppendLine(Pantalla ?? "(desconocida)");
        sb.Append("Tipo    : ").AppendLine(Tipo);
        sb.Append("Mensaje : ").AppendLine(Mensaje);
        sb.AppendLine("Pila    :");
        sb.AppendLine(Pila ?? "(sin pila)");
        return sb.ToString();
    }

    /// <summary>
    /// Una linea para el log y para el resumen. Sin saltos: en logcat un mensaje
    /// multilinea se parte y se mezcla con los de otros procesos.
    /// </summary>
    public string ToSummary() =>
        $"[{Origen}] {Tipo}: {Recortar(Mensaje, 160)} " +
        $"(pantalla={Pantalla ?? "?"}, build={Build ?? "?"})";

    private static string Recortar(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty :
        s.Length <= max ? s.Replace('\n', ' ').Replace('\r', ' ') :
        s[..max].Replace('\n', ' ').Replace('\r', ' ') + "…";

    /// <summary>
    /// Nombre de archivo estable y ordenable. Lleva milisegundos porque dos fallos
    /// encadenados —lo normal cuando algo se rompe— caen en el mismo segundo y uno
    /// pisaria al otro.
    /// </summary>
    public string NombreDeArchivo() =>
        $"crash-{Cuando:yyyyMMdd-HHmmss-fff}.txt";
}
