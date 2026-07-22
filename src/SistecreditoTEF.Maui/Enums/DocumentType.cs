namespace SistecreditoTEF.Maui.Enums;

/// <summary>
/// Tipo de documento de identidad del cliente.
///
/// 1:1 con el enum de Kotlin. Code es el codigo que viaja en JSON
/// (CC, CE); DisplayName es el texto que la UI muestra.
///
/// OCP: si manana se anade PASAPORTE, solo se agrega aqui. El resto del
/// codigo que use DocumentType no necesita cambios.
/// </summary>
public enum DocumentType
{
    CedulaCiudadania,
    CedulaExtranjeria
}

public static class DocumentTypeExtensions
{
    public static string Code(this DocumentType t) => t switch
    {
        DocumentType.CedulaCiudadania    => "CC",
        DocumentType.CedulaExtranjeria   => "CE",
        _                                => throw new ArgumentOutOfRangeException(nameof(t))
    };

    public static string DisplayName(this DocumentType t) => t switch
    {
        DocumentType.CedulaCiudadania    => "Cédula de Ciudadanía",
        DocumentType.CedulaExtranjeria   => "Cédula de Extranjería",
        _                                => throw new ArgumentOutOfRangeException(nameof(t))
    };

    /// <summary>
    /// Convierte el codigo que llega en JSON al enum del dominio.
    /// Si llega un codigo desconocido, lanza excepcion clara.
    /// </summary>
    public static DocumentType FromCode(string code) =>
        code switch
        {
            "CC" => DocumentType.CedulaCiudadania,
            "CE" => DocumentType.CedulaExtranjeria,
            _    => throw new ArgumentException($"Tipo de documento no soportado: {code}")
        };

    /// <summary>
    /// Igual a FromCode pero devuelve null en vez de lanzar excepcion.
    /// Util cuando el caller quiere fail-soft (ej: mapper del DTO).
    /// </summary>
    public static DocumentType? FromCodeOrNull(string? code) =>
        code switch
        {
            "CC" => DocumentType.CedulaCiudadania,
            "CE" => DocumentType.CedulaExtranjeria,
            _    => null
        };
}
