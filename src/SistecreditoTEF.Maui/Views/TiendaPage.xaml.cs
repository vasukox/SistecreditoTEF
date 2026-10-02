using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

/// <summary>
/// Code-behind de la eleccion de tienda.
///
/// El modo ASISTENTE llega como parametro de ruta
/// (<see cref="AppRoutes.Params.Asistente"/>) y no se deduce del estado de la
/// caja: "todavia no hay tienda elegida" tambien es cierto en una caja ya montada
/// a la que alguien le cambio la tienda, y esa no se esta instalando.
/// </summary>
[QueryProperty(nameof(Asistente), AppRoutes.Params.Asistente)]
public partial class TiendaPage : ContentPage
{
    private readonly TiendaViewModel _vm;

    public TiendaPage(TiendaViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    /// <summary>
    /// Lo escribe Shell al navegar con el parametro. Se enlaza al ViewModel en el
    /// setter —y no en OnAppearing— porque el parametro puede llegar antes o
    /// despues de que la pagina aparezca, segun el momento del ciclo de vida.
    ///
    /// Se acepta <c>object</c> y no <c>bool</c> por el mismo motivo que en
    /// [ReplicacionPage.SoloCajeros]: Shell entrega los parametros como TEXTO
    /// cuando la navegacion viene de una URI, y declararlo bool hace que la
    /// asignacion se pierda en silencio.
    /// </summary>
    public object? Asistente
    {
        set
        {
            var pedido = value switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var b) && b,
                _ => false
            };

            _vm.ModoAsistente = pedido;

            AppLogger.I("TiendaPage",
                $"Modo de la pantalla: {(pedido ? "asistente de montaje" : "ajustes")}.");
        }
    }

    /// <summary>
    /// Se recarga en cada aparicion, no solo al construir: el INITIALIZE de HioPos
    /// puede haber traido un STORE_ID mientras esta pantalla estaba en el stack, y
    /// entonces lo que se muestra dejaria de ser lo que la caja esta usando.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _vm.Cargar();
            Barra.RefrescarEstado();
        }
        catch (Exception ex)
        {
            AppLogger.E("TiendaPage", "Error cargando el catalogo de tiendas.", ex);
        }
    }
}
