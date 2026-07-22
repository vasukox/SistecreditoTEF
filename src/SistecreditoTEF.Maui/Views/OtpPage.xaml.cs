using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class OtpPage : ContentPage
{
    private readonly OtpViewModel _vm;

    public OtpPage(OtpViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // V14 fix: el side-effect antes vivia en el constructor (Task.Run).
        // Ahora lo disparamos en OnAppearing, controlado por el ciclo de vida de la Page.
        if (_vm.Status == OtpViewModel.Estado.Idle)
            await _vm.SolicitarAsync();
    }
}
