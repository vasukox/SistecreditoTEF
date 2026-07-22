using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// HU8-973: true si el string tiene contenido. Para mostrar/ocultar labels
/// (eyebrow, marca) en el header del scaffold sin escribir code-behind por página.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
