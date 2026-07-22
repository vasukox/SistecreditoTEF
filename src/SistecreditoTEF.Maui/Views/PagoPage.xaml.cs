using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Models;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

[QueryProperty(nameof(SelectedCredit), AppRoutes.Params.Payment)]
public partial class PagoPage : ContentPage
{
    private readonly PagoViewModel _vm;

    public ActiveCredit? SelectedCredit
    {
        get => _selected;
        set
        {
            _selected = value;
            if (value is not null)
                _vm.CreditoSeleccionado = value;
        }
    }
    private ActiveCredit? _selected;

    public PagoPage(PagoViewModel vm)
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
