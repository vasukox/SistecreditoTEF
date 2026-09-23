namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// QA A-1 / A-2: enmascarado de datos personales para logs, auditoría y
/// comprobantes.
///
/// EL PROBLEMA QUE RESUELVE: el proyecto documentaba el enmascarado de la cédula
/// como control de seguridad y [HttpLoggingHandler] sí lo aplicaba a la URL, pero
/// la cédula completa se filtraba por otros cuatro caminos:
///
///   • <c>CapturaCedulaViewModel</c>: <c>Log.Info($"ENTER doc={NumeroDocumento}")</c>
///     y el log de excepción con la cédula cruda. La clase tenía su propio
///     <c>Mask()</c> y lo usaba en una línea… pero no en esas dos.
///   • <c>CredinetApiClient</c>: cuatro <c>AppLogger.E</c> que volcaban
///     <c>request.RequestUri</c> tal cual, con <c>idDocument=</c> en la query.
///   • <c>SistecreditoService</c>: <c>_audit.Log($"cedula={cliente.DocumentId}")</c>,
///     que además viaja al POS por broadcast, fuera del sandbox de la app.
///
/// En terminales POS con adb habilitado (habitual para soporte), cualquiera con
/// acceso USB extraía el histórico de cédulas atendidas. Habeas data,
/// Ley 1581 de 2012.
///
/// Al centralizarlo aquí, el enmascarado deja de depender de que cada punto de
/// log se acuerde de hacerlo.
/// </summary>
public static class PiiMask
{
    /// <summary>
    /// Documento de identidad: deja solo los últimos 4 caracteres.
    /// Documentos de 4 o menos se enmascaran por completo (QA B-4: antes se
    /// devolvían intactos).
    /// </summary>
    public static string Document(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value.Trim();
        return v.Length <= 4
            ? new string('*', v.Length)
            : new string('*', v.Length - 4) + v[^4..];
    }

    /// <summary>Teléfono: deja los últimos 4 dígitos.</summary>
    public static string Phone(string? value) => Document(value);

    /// <summary>
    /// Nombre de persona: iniciales de cada palabra. Suficiente para conciliar
    /// en un log sin exponer la identidad.
    /// </summary>
    public static string Name(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => p.Length == 0 ? p : $"{p[0]}."));
    }

    /// <summary>
    /// Enmascara toda racha de 6 o más dígitos seguidos, dejando los últimos 4.
    ///
    /// Se usa para volcar valores de estructura DESCONOCIDA —extras de un Intent que
    /// no están en el contrato conocido— donde no se sabe qué campo es qué. No se
    /// puede aplicar <see cref="Document"/> porque no hay un nombre de campo que
    /// diga "acá viene una cédula": lo único que se sabe es que una racha larga de
    /// dígitos es sospechosa de ser cédula, teléfono o número de tarjeta.
    ///
    /// El umbral de 6 deja pasar los valores que sí importan para diagnosticar
    /// (montos en centavos cortos, códigos, banderas, años) y atrapa los
    /// identificadores personales, que en Colombia tienen 7 a 10 dígitos.
    /// </summary>
    public static string LongDigitRuns(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\d{6,}",
            m => Document(m.Value));
    }

    /// <summary>
    /// Enmascara el valor de <c>idDocument</c> en una URL o query string, para
    /// poder loguear la petición sin exponer la cédula.
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "?";
        return System.Text.RegularExpressions.Regex.Replace(
            url,
            @"(idDocument=)([^&\s]+)",
            m => m.Groups[1].Value + Document(m.Groups[2].Value),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
