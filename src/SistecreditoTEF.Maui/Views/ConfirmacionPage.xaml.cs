using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class ConfirmacionPage : ContentPage
{
    private readonly ConfirmacionViewModel _vm;

    public ConfirmacionPage(ConfirmacionViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Inicializar();
    }
}
