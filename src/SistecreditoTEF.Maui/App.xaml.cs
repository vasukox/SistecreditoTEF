namespace SistecreditoTEF.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // MainPage esta deprecated en .NET 10: usar CreateWindow override.
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // NO usamos base.CreateWindow(activationState): cuando MAUI arranca
        // por un intent custom (no LAUNCHER, como icg.actions.electronicpayment.
        // sistecredito.GET_VERSION), el base lanza NotImplementedException
        // porque la MainPage no esta seteada. Creamos el Window directo.
        var shell = new AppShell();

        // La pantalla del cliente sigue a la navegacion del Shell. Se engancha
        // aca —el unico lugar donde nace el Shell— y no en cada pagina.
        //
        // Va envuelto: si algo falla al resolver el servicio, la app tiene que
        // arrancar igual. Un cartel que no se pinta no es motivo para que una
        // terminal no pueda facturar.
        try
        {
            (IPlatformApplication.Current?.Services
                .GetService(typeof(Services.PantallaCliente.VitrinaCoordinador))
                as Services.PantallaCliente.VitrinaCoordinador)?.Enganchar(shell);
        }
        catch (Exception ex)
        {
            Common.AppLogger.W("Vitrina",
                $"No se pudo enganchar la pantalla del cliente al arrancar: {ex.Message}");
        }

        return new Window(shell)
        {
            Title = "SistecreditoTEF"
        };
    }
}
