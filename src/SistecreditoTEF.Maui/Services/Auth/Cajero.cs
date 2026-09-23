namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Un cajero habilitado para hacer abonos en este terminal.
///
/// <c>ClaveHash</c> es una derivacion irreversible (ver [PasswordHasher]); la clave
/// en claro no existe en ningun lado.
/// </summary>
public sealed record Cajero(
    string Id,
    string Usuario,
    string Nombre,
    string ClaveHash,
    bool Activo,
    DateTime CreadoEn)
{
    /// <summary>
    /// Nombre a mostrar y a imprimir. Si no se cargo un nombre, se usa el usuario:
    /// es preferible que el comprobante diga "jperez" antes que quedar vacio.
    /// </summary>
    public string NombreVisible =>
        string.IsNullOrWhiteSpace(Nombre) ? Usuario : Nombre;

    /// <summary>
    /// Usuario normalizado para comparar. El login NO distingue mayusculas: un
    /// cajero que escribe "JPerez" a las 7 de la mañana tiene que entrar igual.
    /// </summary>
    public string UsuarioNormalizado => (Usuario ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>
/// Resultado de intentar identificar a un cajero. Se modela explicito en vez de
/// devolver un bool para que la pantalla pueda decir QUE paso sin inventarlo.
/// </summary>
public abstract record ResultadoIngreso
{
    /// <summary>Clave correcta: el cajero queda identificado para la operacion.</summary>
    public sealed record Ok(Cajero Cajero) : ResultadoIngreso;

    /// <summary>Clave incorrecta.</summary>
    public sealed record ClaveIncorrecta(int IntentosRestantes) : ResultadoIngreso;

    /// <summary>
    /// Demasiados intentos fallidos seguidos para este cajero. Es la barrera contra
    /// probar claves de a una en el terminal.
    /// </summary>
    public sealed record Bloqueado(int SegundosRestantes) : ResultadoIngreso;

    /// <summary>
    /// El cajero esta dado de baja.
    ///
    /// OJO: NO se usa para "el usuario no existe". Ver [AuthService.IngresarAsync]:
    /// un usuario inexistente devuelve ClaveIncorrecta a proposito, para no revelar
    /// que usuarios existen en el terminal.
    /// </summary>
    public sealed record NoHabilitado : ResultadoIngreso;
}
