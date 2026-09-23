using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

/// <summary>
/// Replicación del padrón entre cajas.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL SOCKET MUERE CON LA PANTALLA
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>OnDisappearing</c> apaga el servidor y el descubrimiento. Sin esto, salir de
/// la pantalla dejaría a esta caja ofreciendo el padrón de la tienda en la red
/// todo el día, casi siempre sin nadie mirando.
/// </summary>
public partial class ReplicacionPage : ContentPage
{
    private readonly ReplicacionViewModel _vm;
    private IDispatcherTimer? _cuentaRegresiva;

    public ReplicacionPage(ReplicacionViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Refresca "se cierra en 9:47 · 2 cajas configuradas". El operador tiene que
        // ver cuánto le queda sin tener que adivinarlo.
        _cuentaRegresiva = Dispatcher.CreateTimer();
        _cuentaRegresiva.Interval = TimeSpan.FromSeconds(1);
        _cuentaRegresiva.Tick += (_, _) => _vm.ActualizarEstadoEmisor();
        _cuentaRegresiva.Start();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        _cuentaRegresiva?.Stop();
        _cuentaRegresiva = null;

        try
        {
            await _vm.DetenerAsync();
        }
        catch (Exception ex)
        {
            // async void: una excepción acá se lleva el proceso por delante.
            AppLogger.E("ReplicacionPage", "Error cerrando la ventana de replicación.", ex);
        }
    }
}
