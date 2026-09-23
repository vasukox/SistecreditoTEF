using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Donde quedan los fallos para poder leerlos despues.
///
/// Es una interfaz para que el guardado se pueda probar sin Android: la
/// implementacion real escribe en el almacenamiento privado del terminal.
/// </summary>
public interface ICrashStore
{
    /// <summary>Guarda el fallo. NUNCA lanza: esto corre mientras algo ya se rompio.</summary>
    void Guardar(CrashRecord fallo);

    /// <summary>Los fallos guardados, del mas reciente al mas viejo.</summary>
    IReadOnlyList<string> Listar();

    /// <summary>Contenido de uno, o null si no esta.</summary>
    string? Leer(string nombre);

    void Limpiar();
}

/// <summary>
/// Guarda cada fallo como un archivo de texto en una carpeta, conservando solo los
/// ultimos.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ARCHIVOS Y NO LA BD CIFRADA
/// ─────────────────────────────────────────────────────────────────────────────
/// Porque esto tiene que funcionar JUSTO CUANDO ALGO SE ROMPIO, y una de las cosas
/// que se puede haber roto es SQLCipher o el Keystore. Un store que depende de la
/// BD no sirve para registrar que la BD fallo.
///
/// Son archivos planos, en el almacenamiento privado de la app, y se leen desde una
/// tienda con:
///
///     adb shell run-as com.permoda.sistecreditotef ls files/fallos
///
/// ─────────────────────────────────────────────────────────────────────────────
/// TOPE, PORQUE UN BUCLE DE FALLOS LLENA EL DISCO
/// ─────────────────────────────────────────────────────────────────────────────
/// Si algo falla en cada arranque, esto escribe uno por arranque. Con un tope de 30
/// y un recorte de la pila, el peor caso son unos cientos de KB.
/// </summary>
public sealed class FileCrashStore(string carpeta, int maximo = 30) : ICrashStore
{
    public const string NombreCarpeta = "fallos";

    public void Guardar(CrashRecord fallo)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(Path.Combine(carpeta, fallo.NombreDeArchivo()), fallo.ToText());
            Podar();
        }
        catch (Exception ex)
        {
            // Si no se puede ni escribir el fallo, queda el log del sistema. Lo que
            // NO puede pasar es que el registro de un fallo provoque otro.
            AppLogger.W("ICrashStore", $"No se pudo guardar el fallo: {ex.Message}");
        }
    }

    public IReadOnlyList<string> Listar()
    {
        try
        {
            if (!Directory.Exists(carpeta)) return [];

            return Directory.GetFiles(carpeta, "crash-*.txt")
                .Select(Path.GetFileName)
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLogger.W("ICrashStore", $"No se pudo listar los fallos: {ex.Message}");
            return [];
        }
    }

    public string? Leer(string nombre)
    {
        try
        {
            // El nombre viene de Listar(), pero igual se acota a la carpeta: un
            // "..\..\algo" no puede terminar leyendo otra cosa del terminal.
            var limpio = Path.GetFileName(nombre);
            if (string.IsNullOrEmpty(limpio)) return null;

            var ruta = Path.Combine(carpeta, limpio);
            return File.Exists(ruta) ? File.ReadAllText(ruta) : null;
        }
        catch (Exception ex)
        {
            AppLogger.W("ICrashStore", $"No se pudo leer el fallo: {ex.Message}");
            return null;
        }
    }

    public void Limpiar()
    {
        try
        {
            if (!Directory.Exists(carpeta)) return;
            foreach (var f in Directory.GetFiles(carpeta, "crash-*.txt")) File.Delete(f);
        }
        catch (Exception ex)
        {
            AppLogger.W("ICrashStore", $"No se pudo limpiar los fallos: {ex.Message}");
        }
    }

    private void Podar()
    {
        var todos = Directory.GetFiles(carpeta, "crash-*.txt")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var viejo in todos.Skip(maximo))
        {
            try { File.Delete(viejo); } catch (IOException) { /* se borrara la proxima */ }
        }
    }
}
