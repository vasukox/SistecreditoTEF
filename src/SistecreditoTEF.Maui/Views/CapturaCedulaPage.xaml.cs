using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class CapturaCedulaPage : ContentPage
{
    private readonly CapturaCedulaViewModel _vm;

    public CapturaCedulaPage(CapturaCedulaViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // HU-134: autocompletar la cedula desde el cliente asignado en HioPos.
        _vm.Inicializar();
    }
}
