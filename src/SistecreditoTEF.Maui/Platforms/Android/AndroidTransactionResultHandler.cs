using Android.App;
using Android.Content;
using Android.Util;
using Microsoft.Maui.ApplicationModel;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;
using Hiopos = SistecreditoTEF.Maui.Services.Hiopos.Hiopos;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// Implementacion Android de [ITransactionResultHandler].
///
/// B1: este es el componente que reemplaza el
/// <c>Application.Current?.Quit()</c> de las Pages. En lugar de matar
/// la app, entrega el Intent al POS con SetResult y llama Finish para
/// devolver control al HioPosCloud, que es lo que el doc §4 exige.
/// </summary>
public class AndroidTransactionResultHandler(ITransactionStateStore state) : ITransactionResultHandler
{
    public void FinishWithResult(HioposResponse response)
    {
        // HU8-973: al devolver el resultado a HioPos, la factura deja de estar
        // "viva". Bajamos la bandera de liveness para que un recaudo standalone
        // posterior (ícono de Abonos) no quede bloqueado por el guard.
        state.HioposTransactionActive = false;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            AppLogger.E("ITransactionResultHandler",
                "Platform.CurrentActivity es null; no se puede devolver SetResult.");
            return;
        }

        var intent = new Intent(response.Action);

        if (response.StringExtras is not null)
            foreach (var (key, value) in response.StringExtras)
                if (value is not null)
                    intent.PutExtra(key, value);

        if (response.BinaryExtras is not null)
            foreach (var (key, bytes) in response.BinaryExtras)
                intent.PutExtra(key, bytes);

        // Extras ENTEROS. Imprescindible para "Version": HioPos lo lee con
        // getIntExtra y, al recibirlo como String, se quedaba con el default (-1)
        // y pedia reinstalar el modulo en cada arranque. Ver [HioposResponse].
        if (response.IntExtras is not null)
            foreach (var (key, value) in response.IntExtras)
                intent.PutExtra(key, value);

        // Extras BOOLEANOS. Imprescindible para las capacidades de GET_BEHAVIOR:
        // HioPos las lee con getBooleanExtra y, como cadena, se quedaba con el
        // default de cada una. Ver [HioposResponse.BoolExtras].
        if (response.BoolExtras is not null)
            foreach (var (key, value) in response.BoolExtras)
                intent.PutExtra(key, value);

        var resultCode = response.ResultCode switch
        {
            Hiopos.Result.CanceledValue => Result.Canceled,
            _                    => Result.Ok
        };

        // ─────────────────────────────────────────────────────────────────────────
        // TRAZA DEL CONTRATO
        // ─────────────────────────────────────────────────────────────────────────
        // Los dos campos que deciden si HioPos muestra algo o deja la pantalla en
        // blanco son el ResultCode y TransactionResult, y no habia forma de
        // verificarlos en el terminal: se veia "SetResult called" y nada mas. Cuando
        // una nota de credito no mostraba mensaje, no se podia distinguir "no
        // respondimos" de "respondimos con el campo equivocado".
        //
        // Regla al leer esto en logcat: tiene que decir TransactionResult=...
        // Si apareciera un extra llamado "Result", el campo esta mal —ese nombre no
        // existe en la API TEF 4.0— y el POS descarta el ErrorMessage.
        //
        // Se loguean los NOMBRES de los extras y solo el valor de los dos campos de
        // control. Los comprobantes son XML de miles de caracteres y CardHolder es
        // el nombre del cliente: ninguno tiene por que quedar en el log.
        AppLogger.I("ITransactionResultHandler",
            $"setResult({(resultCode == Result.Ok ? "RESULT_OK" : "RESULT_CANCELED")}) " +
            $"action={response.Action} " +
            $"{HioposExtras.TransactionResult}={ValorDeControl(response, HioposExtras.TransactionResult)} " +
            $"{HioposExtras.TransactionType}={ValorDeControl(response, HioposExtras.TransactionType)} " +
            $"extras=[{string.Join(",", NombresDeExtras(response))}]");

        activity.SetResult(resultCode, intent);
        activity.Finish();
    }

    /// <summary>Valor de un extra de control, o "(ausente)" si no viaja.</summary>
    private static string ValorDeControl(HioposResponse response, string clave) =>
        response.StringExtras is not null
        && response.StringExtras.TryGetValue(clave, out var valor)
        && valor is not null
            ? valor
            : "(ausente)";

    /// <summary>
    /// Nombres de todos los extras que se devuelven. Solo nombres: alcanza para
    /// verificar la forma de la respuesta sin exponer contenido.
    /// </summary>
    private static IEnumerable<string> NombresDeExtras(HioposResponse response)
    {
        var claves = new List<string>();

        if (response.StringExtras is not null)
            claves.AddRange(response.StringExtras.Where(p => p.Value is not null).Select(p => p.Key));
        if (response.BinaryExtras is not null)
            claves.AddRange(response.BinaryExtras.Keys);
        if (response.IntExtras is not null)
            claves.AddRange(response.IntExtras.Keys);
        if (response.BoolExtras is not null)
            claves.AddRange(response.BoolExtras.Select(p => $"{p.Key}={(p.Value ? "true" : "false")}"));

        return claves;
    }
}
