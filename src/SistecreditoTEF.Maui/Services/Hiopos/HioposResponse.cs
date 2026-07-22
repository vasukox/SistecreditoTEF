namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// POCO inmutable que representa el Intent de respuesta a HioPosCloud.
/// Se construye con [HioposResultBuilder] y se materializa en
/// [Android.Content.Intent] dentro de [MainActivity].
///
/// Mantener este tipo libre de dependencias Android permite:
///   - Tests xUnit puros (sin emulador).
///   - Que la logica de "que responder" viva en C# normal.
/// </summary>
public sealed record HioposResponse(
    string Action,
    IReadOnlyDictionary<string, string?> StringExtras,
    IReadOnlyDictionary<string, byte[]>? BinaryExtras = null,
    int? ResultCode = null);
