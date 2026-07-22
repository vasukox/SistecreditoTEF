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
public class AndroidTransactionResultHandler : ITransactionResultHandler
{
    public void FinishWithResult(HioposResponse response)
    {
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

        var resultCode = response.ResultCode switch
        {
            Hiopos.Result.CanceledValue => Result.Canceled,
            _                    => Result.Ok
        };

        activity.SetResult(resultCode, intent);
        activity.Finish();
    }
}
