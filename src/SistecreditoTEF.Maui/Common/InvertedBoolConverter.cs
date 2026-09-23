using System.Globalization;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Niega un booleano para bindings de <c>IsVisible</c> / <c>IsEnabled</c>.
///
/// Existe para no tener que agregar al ViewModel una propiedad espejo por cada
/// bandera que la vista necesita al reves (<c>Desbloqueado</c> /
/// <c>NoDesbloqueado</c>), que es como se acumulan pares que despues quedan
/// desincronizados.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;
}
