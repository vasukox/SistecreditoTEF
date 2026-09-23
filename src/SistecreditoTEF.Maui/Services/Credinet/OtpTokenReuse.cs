namespace SistecreditoTEF.Maui.Services.Credinet;

/// <summary>
/// Detecta cuándo Credinet devuelve el MISMO código OTP en lugar de generar uno
/// nuevo.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL COMPORTAMIENTO OBSERVADO
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>getCreditToken</c> responde <c>tokenGenerated: true</c> incluso cuando
/// reutiliza un token vigente para la misma cédula. Capturado en el terminal, en
/// cuatro solicitudes SEPARADAS:
///
///   09:45:53  TokenGenerated=True, RemainingSeconds=249
///   09:47:04  TokenGenerated=True, RemainingSeconds=178
///   09:48:10  TokenGenerated=True, RemainingSeconds=112
///   09:48:44  TokenGenerated=True, RemainingSeconds=77
///
/// El tiempo restante DESCIENDE entre solicitudes: es el mismo token descontando
/// su vida, no cuatro tokens nuevos (uno nuevo reiniciaría el contador).
///
/// Consecuencia práctica: si ese código ya se usó para crear un crédito, pedir
/// otro devuelve el mismo y el <c>create</c> falla con <c>errorCode 230
/// TokenAlreadyUsed</c>. "Reenviar código" no puede resolverlo, porque manda el
/// mismo. Hay que esperar a que expire.
///
/// Por eso conviene avisarle al cajero en vez de dejarlo reintentando: el módulo
/// no puede arreglar el comportamiento del proveedor, pero sí puede explicarlo.
///
/// La detección es por COMPORTAMIENTO (el contador bajó), no por ambiente, así que
/// sirve igual si en producción se comporta distinto.
/// </summary>
public static class OtpTokenReuse
{
    /// <summary>
    /// ¿La respuesta corresponde al mismo token de antes?
    /// </summary>
    /// <param name="remainingSecondsAnterior">
    /// Tiempo restante que informó la solicitud previa, o null si es la primera.
    /// </param>
    /// <param name="remainingSecondsActual">Tiempo restante de la respuesta actual.</param>
    /// <returns>
    /// true si Credinet reutilizó el token. Un token nuevo reinicia el contador, así
    /// que un valor MENOR que el anterior solo puede venir del mismo token.
    /// </returns>
    public static bool EsElMismoToken(int? remainingSecondsAnterior, int remainingSecondsActual)
    {
        if (remainingSecondsAnterior is null) return false;   // primera solicitud
        if (remainingSecondsActual <= 0) return false;        // sin vigencia: no concluyente
        return remainingSecondsActual < remainingSecondsAnterior.Value;
    }

    /// <summary>
    /// Aviso para el cajero cuando el proveedor reutilizó el código, o cadena vacía
    /// si el token es nuevo.
    /// </summary>
    public static string Aviso(bool esElMismoToken, int remainingSeconds)
    {
        if (!esElMismoToken) return string.Empty;

        var minutos = remainingSeconds / 60;
        var segundos = remainingSeconds % 60;
        var tiempo = minutos > 0 ? $"{minutos} min {segundos} s" : $"{segundos} s";

        return "Sistecredito reenvio el MISMO codigo que ya habia enviado " +
               $"(vence en {tiempo}). Si ese codigo ya se uso, hay que esperar a que " +
               "expire para poder generar uno nuevo.";
    }
}
