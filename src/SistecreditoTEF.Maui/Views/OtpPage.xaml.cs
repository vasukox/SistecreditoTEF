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

        // OnAppearing es async void: lo exige la firma del override, y una
        // excepcion que se escape de un async void no se puede atrapar arriba —
        // sube al manejador global y mata el proceso. En esta pantalla eso seria
        // el peor momento posible: el cliente ya recibio el OTP y el credito esta
        // a un paso de crearse.
        //
        // SolicitarAsync ya atrapa todo por dentro, asi que hoy nada se escapa.
        // El try igual va, por dos razones: el throttle se consulta ANTES de ese
        // try interno, y sobre todo para que el dia que alguien agregue una linea
        // aca no herede un crash de proceso sin darse cuenta.
        try
        {
            // V14 fix: el side-effect antes vivia en el constructor (Task.Run).
            // Ahora lo disparamos en OnAppearing, controlado por el ciclo de vida
            // de la Page.
            if (_vm.Status == OtpViewModel.Estado.Idle)
                await _vm.SolicitarAsync();
        }
        catch (Exception ex)
        {
            SistecreditoTEF.Maui.Common.AppLogger.E(
                "OtpPage", "Error solicitando el OTP al abrir la pantalla.", ex);
        }
    }
}
