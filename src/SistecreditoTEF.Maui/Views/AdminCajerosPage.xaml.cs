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

        // Al volver de elegir la tienda hay que releerla: son propiedades calculadas
        // y MAUI no las re-evalua sola. Ver [AdminCajerosViewModel.RefrescarTienda].
        _vm.RefrescarTienda();

        // La franja de estado de la cabecera lee lo mismo y por el mismo motivo.
        Chrome.RefrescarEstado();

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
