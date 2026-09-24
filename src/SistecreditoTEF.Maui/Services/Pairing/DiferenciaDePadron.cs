using System.Globalization;
using SistecreditoTEF.Maui.Services.Auth;

namespace SistecreditoTEF.Maui.Services.Pairing;

/// <summary>
/// Que le va a pasar al padron de ESTA caja si se aplica el que llego de otra.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE HACE FALTA MOSTRARLO ANTES DE APLICAR
/// ─────────────────────────────────────────────────────────────────────────────
/// El padron se escribe COMPLETO, no se mezcla. Esa decision es correcta para una
/// caja que se esta montando —arrastrar cajeros de una instalacion anterior es como
/// se cuelan usuarios que nadie dio de alta ahi— pero convierte cada actualizacion
/// en un reemplazo.
///
/// Y un reemplazo tiene una consecuencia que NO se ve: si la caja de la que se copia
/// tiene el padron viejo, "actualizar" BORRA cajeros sin decir nada. El operador
/// creia estar sumando y estaba restando.
///
/// "7 cajeros" no alcanza para notarlo. "Se agregan 2 · se quitan 3" si: ahi el
/// operador cancela.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// SE COMPARA POR USUARIO, NO POR ID
/// ─────────────────────────────────────────────────────────────────────────────
/// El Id lo genera la caja que dio de alta al cajero. Dos cajas configuradas a mano
/// tienen Ids distintos para el MISMO usuario, y comparar por Id mostraria "se
/// agregan 7 · se quitan 7" en una actualizacion donde en realidad no cambia nadie.
///
/// El usuario es lo que el cajero teclea para entrar, asi que es la identidad que
/// importa. Se normaliza igual que en el ingreso ([Cajero.UsuarioNormalizado]):
/// el login no distingue mayusculas.
/// </summary>
public sealed record DiferenciaDePadron(
    IReadOnlyList<string> Agregados,
    IReadOnlyList<string> Reactivados,
    IReadOnlyList<string> Desactivados,
    IReadOnlyList<string> ClavesCambiadas,
    IReadOnlyList<string> Quitados)
{
    /// <summary>
    /// Hay bajas. Es el unico caso que merece un aviso aparte: agregar y cambiar
    /// claves se deshace dando de alta otra vez, pero un cajero quitado se lleva la
    /// posibilidad de que entre, y el operador no pidio quitar a nadie.
    /// </summary>
    public bool HayBajas => Quitados.Count > 0;

    public bool SinCambios =>
        Agregados.Count == 0 && Reactivados.Count == 0 && Desactivados.Count == 0
        && ClavesCambiadas.Count == 0 && Quitados.Count == 0;

    /// <summary>
    /// "Se agregan 2 · se desactiva 1 · se quitan 3". Solo lo que cambia: los ceros
    /// no se nombran, para que lo poco que hay resalte.
    /// </summary>
    public string Resumen
    {
        get
        {
            if (SinCambios) return "No hay cambios: el padron queda igual.";

            var partes = new List<string>(5);

            Sumar(partes, Agregados.Count, "se agrega", "se agregan");
            Sumar(partes, Reactivados.Count, "se reactiva", "se reactivan");
            Sumar(partes, Desactivados.Count, "se desactiva", "se desactivan");
            Sumar(partes, ClavesCambiadas.Count, "cambia la clave de", "cambian las claves de");
            Sumar(partes, Quitados.Count, "se quita", "se quitan");

            var texto = string.Join(" · ", partes);
            return char.ToUpper(texto[0], CultureInfo.InvariantCulture) + texto[1..];
        }
    }

    private static void Sumar(List<string> partes, int cuantos, string singular, string plural)
    {
        if (cuantos == 0) return;
        partes.Add($"{(cuantos == 1 ? singular : plural)} {cuantos.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// El detalle de las bajas, para nombrarlas. Un numero se lee y se olvida; ver
    /// "se quitan: jperez, mgomez" hace que el operador reconozca a quien conoce.
    /// </summary>
    public string DetalleDeBajas => string.Join(", ", Quitados);

    public static DiferenciaDePadron Entre(
        IReadOnlyList<Cajero> actuales,
        IReadOnlyList<ReplicatedCajero> entrantes)
    {
        ArgumentNullException.ThrowIfNull(actuales);
        ArgumentNullException.ThrowIfNull(entrantes);

        var aqui = actuales
            .GroupBy(Normalizar)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var alla = entrantes
            .GroupBy(Normalizar)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        List<string> agregados = [], reactivados = [], desactivados = [], claves = [], quitados = [];

        foreach (var (clave, entrante) in alla)
        {
            if (!aqui.TryGetValue(clave, out var actual))
            {
                agregados.Add(entrante.Usuario);
                continue;
            }

            if (!actual.Activo && entrante.Activo) reactivados.Add(entrante.Usuario);
            else if (actual.Activo && !entrante.Activo) desactivados.Add(entrante.Usuario);

            // Ordinal: es una derivacion, no un texto. Dos hashes distintos son dos
            // claves distintas aunque se parezcan.
            if (!string.Equals(actual.ClaveHash, entrante.ClaveHash, StringComparison.Ordinal))
                claves.Add(entrante.Usuario);
        }

        foreach (var (clave, actual) in aqui)
            if (!alla.ContainsKey(clave))
                quitados.Add(actual.Usuario);

        return new DiferenciaDePadron(
            Ordenar(agregados), Ordenar(reactivados), Ordenar(desactivados),
            Ordenar(claves), Ordenar(quitados));
    }

    private static string Normalizar(Cajero c) => c.UsuarioNormalizado;

    private static string Normalizar(ReplicatedCajero c) =>
        (c.Usuario ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Orden alfabetico estable. Sin esto el listado sale en el orden del
    /// diccionario, que cambia entre corridas: el operador que actualiza tres cajas
    /// veria la misma lista en tres ordenes distintos y desconfiaria con razon.
    /// </summary>
    private static IReadOnlyList<string> Ordenar(List<string> nombres)
    {
        nombres.Sort(StringComparer.OrdinalIgnoreCase);
        return nombres;
    }
}
