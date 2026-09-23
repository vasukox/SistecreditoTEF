using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class ValidacionClientePage : HioposFlowPage
{
    private readonly ValidacionClienteViewModel _vm;

    public ValidacionClientePage(ValidacionClienteViewModel vm)
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
