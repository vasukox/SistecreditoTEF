namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Constantes para [HioposResponse.ResultCode]. Evita usar magic numbers
/// en comparisons (V9 - OCP). Anidada en una clase estatic [Hiopos]
/// para evitar conflicto con [Android.App.Result].
///
/// Valores tomados 1:1 de Android.App.Activity.Result.
/// </summary>
public static class Hiopos
{
    public static class Result
    {
        public const int OkValue = -1;       // Android's Activity.Result.Ok
        public const int CanceledValue = 0;  // Android's Activity.Result.Canceled
    }
}
