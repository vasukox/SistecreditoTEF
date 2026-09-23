using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class AdminCajerosPage : ContentPage
{
    private readonly AdminCajerosViewModel _vm;

    public AdminCajerosPage(AdminCajerosViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // CargarAsync no hace nada si todavia no se puso el PIN, asi que es
            // seguro llamarlo siempre.
            await _vm.CargarAsync();
        }
        catch (Exception ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "AdminCajerosPage", "Error cargando los cajeros.", ex);
        }
    }
}
