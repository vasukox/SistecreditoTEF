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
        return new Window(new AppShell())
        {
            Title = "SistecreditoTEF"
        };
    }
}
