using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class ConfiguracionInicioPage : ContentPage
{
    private readonly ConfiguracionInicioViewModel _vm;

    public ConfiguracionInicioPage(ConfiguracionInicioViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>
    /// Se recarga en cada aparicion: desde aqui se puede ir a cambiar la tienda y
    /// volver, y lo que se muestra tiene que ser la tienda de AHORA. Son
    /// propiedades calculadas y MAUI no las re-evalua por si sola — el mismo
    /// motivo por el que existen los <c>RefrescarTienda</c> de los otros
    /// ViewModels.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _vm.RefrescarTienda();
            Chrome.RefrescarEstado();
        }
        catch (Exception ex)
        {
            AppLogger.E("ConfiguracionInicioPage", "Error refrescando la tienda.", ex);
        }
    }
}
