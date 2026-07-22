using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Converter que bindea un enum a un RadioButton.IsChecked.
///
/// Uso en XAML:
///   IsChecked="{Binding TipoDocumento,
///                Converter={StaticResource EnumToBoolConverter},
///                ConverterParameter=CedulaCiudadania}"
///
/// Devuelve true cuando el valor del enum coincide con el ConverterParameter.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        var enumValue = value.ToString();
        var paramValue = parameter.ToString();
        return string.Equals(enumValue, paramValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null)
        {
            return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        }
        // MAUI: UnsetValue se representa como null para converters personalizados.
        return null!;
    }
}
