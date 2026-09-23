using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class IngresoCajeroPage : ContentPage
{
    public IngresoCajeroPage(IngresoCajeroViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // Ya no hay nada que cargar en OnAppearing: el ingreso es por usuario escrito,
    // no por lista de cajeros. Antes se recargaba la lista en cada aparicion.
}
