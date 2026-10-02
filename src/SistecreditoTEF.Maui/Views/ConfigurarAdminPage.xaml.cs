using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class ConfigurarAdminPage : ContentPage
{
    private readonly ConfigurarAdminViewModel _vm;

    public ConfigurarAdminPage(ConfigurarAdminViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>
    /// Al volver de elegir la tienda hay que releerla.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// EL SINTOMA QUE ESTO CORRIGE
    /// ─────────────────────────────────────────────────────────────────────────
    /// Se reporto desde la terminal: "uno selecciona la tienda y despues se
    /// devuelve y no ve que StoreId esta". Y era cierto. La tarjeta enlaza
    /// propiedades CALCULADAS (leen la configuracion en cada acceso), pero nadie
    /// avisaba que habian cambiado: MAUI las evalua al armar el enlace y no las
    /// vuelve a mirar. La pagina seguia mostrando lo que habia al entrar.
    ///
    /// Peor todavia en esta pantalla: quien instala elige la tienda, vuelve, ve lo
    /// mismo de antes y concluye que no se guardo.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefrescarTienda();

        // La tienda ya no tiene tarjeta propia en esta pantalla: vive en la franja
        // de estado de la cabecera, y esa tambien hay que releerla al volver.
        Chrome.RefrescarEstado();
    }
}
