using System.Xml.Linq;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Parseo PURO del XML de <c>Parameters</c> que HioPosCloud entrega en el
/// INITIALIZE:
///
/// <code>
///   &lt;Configuration&gt;&lt;Parameters&gt;
///     &lt;Param Key="API_BASE_URL"&gt;https://api.credinet.co/posprod/&lt;/Param&gt;
///     &lt;Param Key="SUBSCRIPTION_KEY"&gt;...&lt;/Param&gt;
///   &lt;/Parameters&gt;&lt;/Configuration&gt;
/// </code>
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QA C-3 — EL BUG QUE CORRIGE (era el hallazgo más peligroso del módulo)
/// ─────────────────────────────────────────────────────────────────────────────
/// La versión anterior hacía:
/// <code>
///   foreach (var p in doc.Descendants("Param"))
/// </code>
/// <c>XName</c> implícito ⇒ namespace VACÍO. Si ICG entrega el XML con un
/// namespace por defecto (<c>&lt;Configuration xmlns="..."&gt;</c>),
/// <c>Descendants("Param")</c> devuelve CERO elementos. Y como el fallback es
/// <c>appsettings.json</c> —que trae <c>SubscriptionKey="__SANDBOX__"</c> y la
/// URL de sandbox— una terminal de PRODUCCIÓN arrancaba silenciosamente contra
/// el ambiente de pruebas con la key pública del manual. El log lo reportaba
/// como éxito (<c>Info: "Parámetros Cloud guardados: 0"</c>).
///
/// Ahora se compara por <c>LocalName</c>, igual que ya hacía
/// [XmlDocumentReader] con el documento de venta: la lección estaba aprendida
/// en el repo, solo faltaba aplicarla aquí.
///
/// También se acepta el atributo <c>Key</c> en cualquier capitalización y con
/// namespace, porque el contrato de ICG no lo especifica.
/// </summary>
public static class CloudConfigParser
{
    /// <summary>
    /// Devuelve los parámetros encontrados. Diccionario vacío si el XML es nulo,
    /// vacío o malformado (nunca lanza: el llamador decide qué hacer con la
    /// ausencia de parámetros).
    /// </summary>
    public static Dictionary<string, string> Parse(string? xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(xml)) return result;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return result;
        }

        if (doc.Root is null) return result;

        // Agnóstico al namespace: se compara el LocalName del elemento.
        foreach (var element in doc.Root.DescendantsAndSelf())
        {
            if (!string.Equals(element.Name.LocalName, "Param", StringComparison.OrdinalIgnoreCase))
                continue;

            var key = ReadKeyAttribute(element);
            if (string.IsNullOrWhiteSpace(key)) continue;

            var value = element.Value?.Trim() ?? string.Empty;
            result[key.Trim()] = value;
        }

        return result;
    }

    /// <summary>
    /// Lee el atributo <c>Key</c> tolerando capitalización y namespace
    /// distintos ("Key", "key", "KEY").
    /// </summary>
    private static string? ReadKeyAttribute(XElement element)
    {
        foreach (var attr in element.Attributes())
        {
            if (string.Equals(attr.Name.LocalName, "Key", StringComparison.OrdinalIgnoreCase))
                return attr.Value;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════
    // DIAGNÓSTICO DE ESTRUCTURA
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Describe la FORMA del payload de configuración, **sin exponer ningún
    /// valor**, para poder adaptar el parseo al formato real que entrega ICG.
    ///
    /// REGLA DE ESTA FUNCIÓN: nunca incluir contenido. Uno de los parámetros es
    /// la credencial de Azure APIM de producción, y este diagnóstico se imprime en
    /// logcat, que en un POS con adb habilitado puede leer cualquiera con acceso
    /// USB. Solo se reportan:
    ///   - nombres de elementos y de atributos (no sus valores),
    ///   - namespaces,
    ///   - jerarquía y conteos,
    ///   - longitudes y perfil de caracteres delimitadores.
    ///
    /// Con eso alcanza para saber si el formato es <c>&lt;Param Key="X"&gt;valor&lt;/Param&gt;</c>,
    /// <c>&lt;Parameter name="X" value="Y"/&gt;</c>, JSON, o pares clave=valor.
    /// </summary>
    public static string DescribeStructure(string? xml)
    {
        if (xml is null) return "payload=null";
        if (xml.Length == 0) return "payload=vacio (0 chars)";
        if (string.IsNullOrWhiteSpace(xml)) return $"payload=solo espacios ({xml.Length} chars)";

        var sb = new System.Text.StringBuilder();
        sb.Append("len=").Append(xml.Length);
        sb.Append(" perfil=[").Append(CharProfile(xml)).Append(']');

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            // No es XML válido: el perfil de caracteres indica qué formato es.
            sb.Append(" NO-ES-XML(").Append(ex.Message).Append(')');
            return sb.ToString();
        }

        if (doc.Root is null) return sb.Append(" sin-root").ToString();

        sb.Append(" root=").Append(Describe(doc.Root.Name));

        // Jerarquía: nombre de elemento por nivel, con cuántos hay de cada uno.
        var porNivel = new SortedDictionary<int, Dictionary<string, int>>();
        Walk(doc.Root, 0, porNivel);

        foreach (var (nivel, nombres) in porNivel)
        {
            sb.Append(" | n").Append(nivel).Append('=');
            sb.Append(string.Join(",", nombres.Select(kv =>
                kv.Value > 1 ? $"{kv.Key}x{kv.Value}" : kv.Key)));
        }

        // Atributos presentes en todo el documento (solo los NOMBRES).
        var atributos = doc.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => !a.IsNamespaceDeclaration)
            .Select(a => Describe(a.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();
        sb.Append(" | attrs=[").Append(atributos.Count == 0 ? "ninguno" : string.Join(",", atributos)).Append(']');

        // Elementos hoja: son los candidatos a "parámetro". Se reporta cuántos hay
        // y la longitud de su contenido, NO el contenido.
        var hojas = doc.Descendants().Where(e => !e.HasElements).ToList();
        sb.Append(" | hojas=").Append(hojas.Count);
        if (hojas.Count > 0)
        {
            var conTexto = hojas.Count(h => !string.IsNullOrWhiteSpace(h.Value));
            sb.Append(" (conTexto=").Append(conTexto);
            sb.Append(", lenMax=").Append(hojas.Max(h => h.Value?.Length ?? 0)).Append(')');
        }

        return sb.ToString();
    }

    private static void Walk(XElement element, int nivel,
        SortedDictionary<int, Dictionary<string, int>> acumulador)
    {
        if (!acumulador.TryGetValue(nivel, out var nombres))
        {
            nombres = new Dictionary<string, int>(StringComparer.Ordinal);
            acumulador[nivel] = nombres;
        }

        var nombre = Describe(element.Name);
        nombres[nombre] = nombres.TryGetValue(nombre, out var n) ? n + 1 : 1;

        // Tope de profundidad: evita un log gigante con un XML inesperadamente anidado.
        if (nivel >= 6) return;
        foreach (var hijo in element.Elements())
            Walk(hijo, nivel + 1, acumulador);
    }

    /// <summary>Nombre local, con el namespace marcado si existe (sin la URI completa).</summary>
    private static string Describe(XName name) =>
        string.IsNullOrEmpty(name.NamespaceName)
            ? name.LocalName
            : name.LocalName + "@ns";

    /// <summary>
    /// Conteo de caracteres delimitadores. Permite identificar el formato cuando
    /// el payload no es XML, sin revelar contenido.
    /// </summary>
    private static string CharProfile(string s)
    {
        int lt = 0, gt = 0, brace = 0, eq = 0, semi = 0, nl = 0, pipe = 0, comma = 0;
        foreach (var c in s)
        {
            switch (c)
            {
                case '<': lt++; break;
                case '>': gt++; break;
                case '{': case '}': brace++; break;
                case '=': eq++; break;
                case ';': semi++; break;
                case '\n': nl++; break;
                case '|': pipe++; break;
                case ',': comma++; break;
            }
        }
        return $"<:{lt} >:{gt} {{}}:{brace} =:{eq} ;:{semi} nl:{nl} |:{pipe} ,:{comma}";
    }
}
