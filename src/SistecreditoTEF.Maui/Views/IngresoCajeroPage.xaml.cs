using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class IngresoCajeroPage : ContentPage
{
    public IngresoCajeroPage(IngresoCajeroViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // Del formulario no hay nada que cargar: el ingreso es por usuario escrito, no
    // por lista de cajeros. Lo que si se relee es la franja de estado: desde aqui
    // se va a administracion, donde se puede cambiar la tienda, y al volver la
    // cabecera tiene que decir la de ahora.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Chrome.RefrescarEstado();
    }
}
