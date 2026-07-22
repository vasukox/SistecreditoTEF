using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Abstraccion sobre [Android.App.Activity.SetResult + Finish].
///
/// POR QUE EXISTE (V8 / SOLID-DIP):
///   Los ViewModels no pueden tocar Android.App.Activity directamente:
///   - Tests xUnit no cargan el Android SDK.
///   - MAUI Shell no expone la Activity que origino el Intent de HioPos.
///
///   La impl Android (AndroidTransactionResultHandler) resuelve
///   [Platform.CurrentActivity] y llama SetResult/Finish.
///   En tests se inyecta un fake que captura el HioposResponse.
/// </summary>
public interface ITransactionResultHandler
{
    /// <summary>
    /// Entrega el resultado al POS (HioPosCloud) y cierra la Activity actual.
    /// Llamar UNA sola vez al final del flujo exitoso.
    /// </summary>
    void FinishWithResult(HioposResponse response);
}
