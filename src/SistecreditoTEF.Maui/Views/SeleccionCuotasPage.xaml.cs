using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class SeleccionCuotasPage : HioposFlowPage
{
    private readonly SeleccionCuotasViewModel _vm;

    public SeleccionCuotasPage(SeleccionCuotasViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Monto))
                SistecreditoTEF.Maui.Common.Fire.AndForget(
                    vm.OnMontoChangedAsync(vm.Monto), "SeleccionCuotasPage");
            else if (e.PropertyName == nameof(vm.HasResults) && vm.HasResults)
                await ScrollToResultadoAsync();
        };
    }

    /// <summary>
    /// Tras "Calcular y simular", baja la vista hasta la tarjeta de resultado
    /// (donde aparece el boton "Confirmar credito") para que el cajero no
    /// tenga que hacer scroll manual en el POS.
    /// </summary>
    private async Task ScrollToResultadoAsync()
    {
        // Pequena espera para que el resultado ya este pintado/medido.
        await Task.Delay(120);
        try
        {
            await PageScroll.ScrollToAsync(ResultadoCard, ScrollToPosition.Start, animated: true);
        }
        catch
        {
            // Si el elemento aun no esta en el arbol visual, ignoramos.
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // HU-134: precarga el monto facturado en HioPos.
        _vm.Inicializar();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is SeleccionCuotasViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { }
}
