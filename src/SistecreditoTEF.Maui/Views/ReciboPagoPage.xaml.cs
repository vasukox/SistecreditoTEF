using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

[QueryProperty(nameof(Pago), AppRoutes.Params.Payment)]
public partial class ReciboPagoPage : ContentPage
{
    private readonly ReciboPagoViewModel _vm;

    public Payment? Pago
    {
        get => _pago;
        set
        {
            _pago = value;
            if (value is not null)
                _vm.Pago = value;
        }
    }
    private Payment? _pago;

    public ReciboPagoPage(ReciboPagoViewModel vm)
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
