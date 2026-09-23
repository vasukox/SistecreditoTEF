using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class CapturaCedulaPage : HioposFlowPage
{
    private readonly CapturaCedulaViewModel _vm;

    public CapturaCedulaPage(CapturaCedulaViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // HU-134: autocompletar la cedula desde el cliente asignado en HioPos.
        _vm.Inicializar();
    }

    // El "atras" de esta pantalla ya no vive aca: lo maneja [HioposFlowPage], que
    // devuelve el control al POS en vez de hacer pop. Se movio porque el mismo
    // problema aparecia en las otras pantallas del flujo y tenerlo copiado cinco
    // veces era pedirle a la proxima pantalla que lo volviera a olvidar. El detalle
    // de por que no se puede salir sin responderle a HioPos esta documentado en
    // [HioposExit].
}
