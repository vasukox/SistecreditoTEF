namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// HU8-973: construye el valor del extra <c>TransactionData</c> que HioPosCloud
/// persiste (máx 250 chars) para vincular la venta con la operación.
///
/// GARANTIZA JSON VÁLIDO: si el objeto completo excede 250 chars, degrada a un
/// objeto mínimo con el primer campo (el id principal) en vez de cortar a mitad
/// de string — cortar dejaría un JSON inválido en el Intent.
/// </summary>
public static class HioposTransactionData
{
    private const int MaxLen = 250;

    public static string Build(params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
            dict[k] = v;

        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        if (json.Length <= MaxLen)
            return json;

        // Degradar conservando solo el primer campo (id principal) → JSON válido.
        if (fields.Length > 0)
        {
            var min = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, object?> { [fields[0].Key] = fields[0].Value });
            if (min.Length <= MaxLen)
                return min;
        }
        return "{}";
    }
}
