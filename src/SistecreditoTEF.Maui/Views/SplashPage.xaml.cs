using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

/// <summary>
/// Pantalla raiz: resuelve a que modulo entrar, y nada mas.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// YA NO HAY ANIMACION DE MARCA
/// ─────────────────────────────────────────────────────────────────────────────
/// Antes esta pantalla animaba la marca "sistecredito" (halo, fade+escala,
/// subrayado, subtitulo) EN PARALELO con la decision, y esperaba al MAXIMO de las
/// dos. La idea era que la animacion no sumara tiempo; en la practica lo sumaba
/// igual, porque la animacion duraba ~1 s y la decision resolvia en decenas de
/// milisegundos. O sea que el piso de arranque lo ponia la animacion, no el
/// trabajo real.
///
/// Se quito por pedido explicito. Ahora se espera SOLO la decision y se navega en
/// cuanto esta lista: el arranque pasa a costar lo que cuesta leer la BD cifrada.
/// </summary>
public partial class SplashPage : ContentPage
{
    private readonly SplashViewModel _vm;

    public SplashPage(SplashViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.DecidirYNavegarAsync();
        }
        catch (Exception ex)
        {
            // OnAppearing es async void: una excepcion que se escape mata el proceso.
            AppLogger.E("SplashPage", "Error en el arranque.", ex);
        }
    }
}
