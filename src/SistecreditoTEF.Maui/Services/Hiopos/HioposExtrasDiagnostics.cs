using System.Globalization;
using System.Text;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Describe los extras de un Intent de HioPos en UNA linea, apta para logcat.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// PARA QUE EXISTE
/// ─────────────────────────────────────────────────────────────────────────────
/// HioPos usa la MISMA accion (TRANSACTION) para una venta y para un cobro
/// iniciado desde la terminal, asi que por la accion sola no se pueden distinguir.
/// La diferencia viaja en los extras —se sospecha de <c>IsAdvancedPayment</c>,
/// <c>TransactionType</c> u <c>OverPaymentType</c>— pero el modulo nunca los
/// loguea, asi que una captura de logcat mostraba que el TRANSACTION habia
/// llegado y nada sobre su naturaleza.
///
/// Sin este volcado no hay forma de saber que campo distingue un abono de una
/// venta sin adivinar. Con el, una sola captura en terminal lo resuelve.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE NO SE VUELCA EL BUNDLE CRUDO
/// ─────────────────────────────────────────────────────────────────────────────
/// Porque los extras llevan datos del cliente. <c>DocumentData</c> es el XML de la
/// venta completo (nombre y cedula), <c>Token</c> es la credencial de sesion, y
/// cualquiera con acceso USB al POS puede leer logcat. Un volcado crudo convertiria
/// una herramienta de diagnostico en una fuga de PII permanente.
///
/// El criterio es: los campos que deciden el RUTEO se loguean tal cual (son
/// enumerados y montos, no identifican a nadie); los voluminosos o sensibles solo
/// reportan que llegaron y su tamano; y los desconocidos —que son justamente los
/// candidatos a explicar el abono— reportan nombre y un valor recortado con las
/// rachas largas de digitos enmascaradas.
/// </summary>
public static class HioposExtrasDiagnostics
{
    /// <summary>
    /// Campos que deciden como rutear la operacion. Se loguean con su valor real:
    /// son enumerados, banderas y montos, no identifican a una persona.
    /// </summary>
    private static readonly string[] DeRuteo =
    [
        HioposExtras.TransactionType,
        HioposExtras.TenderType,
        HioposExtras.IsAdvancedPayment,
        HioposExtras.OverPaymentType,
        HioposExtras.Amount,
        HioposExtras.CurrencyIso,
        HioposExtras.TipAmount,
        HioposExtras.TaxAmount,
        HioposExtras.SurchargeAmount,
        HioposExtras.TransactionId,
        HioposExtras.ReceiptPrinterColumns,
        HioposExtras.IsReadOnly,
        HioposExtras.LanguageIso
    ];

    /// <summary>
    /// Campos que NO se vuelcan: o son voluminosos, o llevan datos del cliente, o
    /// son credenciales. Solo se reporta presencia y tamano.
    /// </summary>
    private static readonly string[] SoloTamano =
    [
        HioposExtras.DocumentData,
        HioposExtras.DocumentPath,
        HioposExtras.ShopData,
        HioposExtras.SellerData,
        HioposExtras.TaxDetail,
        HioposExtras.TransactionData,
        HioposExtras.Token,
        HioposExtras.Parameters
    ];

    /// <summary>Largo maximo del valor de un extra desconocido.</summary>
    private const int MaxLargoDesconocido = 40;

    /// <summary>
    /// Arma la linea de diagnostico. Nunca lanza: es codigo de log y no debe poder
    /// tumbar una venta en curso.
    /// </summary>
    public static string Describe(IReadOnlyDictionary<string, string?>? extras)
    {
        if (extras is null) return "EXTRAS: (sin bundle)";
        if (extras.Count == 0) return "EXTRAS: (bundle vacio)";

        try
        {
            var sb = new StringBuilder("EXTRAS:");

            // Primero los de ruteo, en orden fijo, para poder comparar dos capturas
            // (una venta y un abono) linea contra linea.
            foreach (var clave in DeRuteo)
            {
                if (!extras.TryGetValue(clave, out var valor)) continue;
                sb.Append(' ').Append(clave).Append('=')
                  .Append(string.IsNullOrEmpty(valor) ? "(vacio)" : valor);
            }

            foreach (var clave in SoloTamano)
            {
                if (!extras.TryGetValue(clave, out var valor)) continue;
                var largo = valor?.Length ?? 0;
                sb.Append(' ').Append(clave).Append('=')
                  .Append(largo > 0
                      ? "(presente, " + largo.ToString(CultureInfo.InvariantCulture) + " chars)"
                      : "(vacio)");
            }

            // Los desconocidos van al final. Son los mas interesantes: si HioPos
            // marca el abono con un extra que no esta en el contrato que conocemos,
            // aparece aca.
            var conocidos = new HashSet<string>(DeRuteo, StringComparer.Ordinal);
            conocidos.UnionWith(SoloTamano);

            var desconocidos = extras.Keys
                .Where(k => !conocidos.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            if (desconocidos.Count > 0)
            {
                sb.Append(" | NO_RECONOCIDOS:");
                foreach (var clave in desconocidos)
                {
                    sb.Append(' ').Append(clave).Append('=')
                      .Append(Recortar(extras[clave]));
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"EXTRAS: (no se pudieron describir: {ex.GetType().Name})";
        }
    }

    /// <summary>
    /// Recorta y enmascara el valor de un extra desconocido: revela la forma del
    /// dato sin publicar cedulas ni telefonos.
    /// </summary>
    private static string Recortar(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return "(vacio)";

        var enmascarado = Common.PiiMask.LongDigitRuns(valor);

        return enmascarado.Length <= MaxLargoDesconocido
            ? enmascarado
            : enmascarado[..MaxLargoDesconocido] + "...(" +
              valor.Length.ToString(CultureInfo.InvariantCulture) + " chars)";
    }
}
