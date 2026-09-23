using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Views.Controls;

/// <summary>
/// HU8-973: chrome de pantalla (header verde + contenido + footer HI-POS).
/// El botón "atrás" hace pop de la navegación (Shell "..").
/// </summary>
public partial class ScaffoldView : ContentView
{
    public static readonly BindableProperty TitleTextProperty =
        BindableProperty.Create(nameof(TitleText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty SubtitleTextProperty =
        BindableProperty.Create(nameof(SubtitleText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty EyebrowTextProperty =
        BindableProperty.Create(nameof(EyebrowText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty ShowBackProperty =
        BindableProperty.Create(nameof(ShowBack), typeof(bool), typeof(ScaffoldView), true);

    /// <summary>
    /// Si esta pantalla puede ofrecer "Volver a HioPos" cuando el modulo esta
    /// atendiendo una operacion del POS.
    ///
    /// Por defecto FALSE, y hay que pedirlo explicitamente. El boton pertenece al
    /// modulo de VENTA (pagar a credito): ahi el cajero eligio Sistecredito como
    /// medio de pago y se puede arrepentir sin consecuencias. En el modulo de
    /// RECAUDO (pagar credito) no va: la flecha ahi es para moverse dentro del
    /// flujo, y ofrecer una salida al POS en medio de un cobro invita a irse de una
    /// pantalla donde lo que sigue es plata del cliente.
    /// </summary>
    public static readonly BindableProperty PermitirVolverAHioposProperty =
        BindableProperty.Create(nameof(PermitirVolverAHiopos), typeof(bool), typeof(ScaffoldView), false,
            propertyChanged: (b, _, _) => (b as ScaffoldView)?.ActualizarBoton());

    public string TitleText    { get => (string)GetValue(TitleTextProperty);    set => SetValue(TitleTextProperty, value); }
    public string SubtitleText { get => (string)GetValue(SubtitleTextProperty); set => SetValue(SubtitleTextProperty, value); }
    public string EyebrowText  { get => (string)GetValue(EyebrowTextProperty);  set => SetValue(EyebrowTextProperty, value); }
    public string TrailingText { get => (string)GetValue(TrailingTextProperty); set => SetValue(TrailingTextProperty, value); }
    public bool   ShowBack     { get => (bool)GetValue(ShowBackProperty);       set => SetValue(ShowBackProperty, value); }

    /// <inheritdoc cref="PermitirVolverAHioposProperty" />
    public bool PermitirVolverAHiopos
    {
        get => (bool)GetValue(PermitirVolverAHioposProperty);
        set => SetValue(PermitirVolverAHioposProperty, value);
    }

    public ScaffoldView()
    {
        InitializeComponent();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Back") is not Button back)
            return;

        back.Clicked -= OnBackClicked;
        back.Clicked += OnBackClicked;

        ActualizarBoton();
    }

    /// <summary>
    /// Le da al boton la forma que corresponde al estado ACTUAL.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE NO ALCANZA CON HACERLO EN OnApplyTemplate
    /// ─────────────────────────────────────────────────────────────────────────────
    /// [OnApplyTemplate] corre cuando el control aplica su propio ControlTemplate, o
    /// sea dentro de su InitializeComponent — ANTES de que la pagina padre le asigne
    /// PermitirVolverAHiopos="True". Leerlo ahi devolvia siempre el default false, y
    /// en plena venta el header mostraba la flecha pelada en vez de la pastilla...
    /// pero al tocarla te sacaba a HioPos igual, porque el click si lee la propiedad
    /// tarde. Un control que dice una cosa y hace otra.
    ///
    /// Dentro de una operacion de HioPos la flecha sola no alcanza: el cajero que
    /// entro desde una venta necesita ver que ese boton lo devuelve al POS, no que lo
    /// mueve una pantalla atras.
    /// </summary>
    private void ActualizarBoton()
    {
        if (GetTemplateChild("PART_Back") is not Button back) return;

        if (OfreceSalidaAHiopos)
        {
            back.Text            = "←  Volver a HioPos";
            back.FontSize        = 14;
            back.Padding         = new Thickness(14, 0);
            back.BackgroundColor = Color.FromArgb("#33FFFFFF");
        }
        else
        {
            // Se restaura la flecha sola: sin esto, una pantalla que deja de ofrecer
            // la salida se quedaria con la pastilla puesta.
            back.Text            = "←";
            back.FontSize        = 26;
            back.Padding         = new Thickness(0);
            back.BackgroundColor = Colors.Transparent;
        }
    }

    /// <summary>
    /// True solo si esta pantalla pidio el boton Y hay una operacion de HioPos viva.
    /// Las dos condiciones son necesarias: sin la primera el boton aparecia tambien
    /// en el modulo de recaudo, donde no corresponde.
    /// </summary>
    private static bool DisponibleEnHiopos =>
        IPlatformApplication.Current?.Services.GetService<IHioposExit>()?.Disponible == true;

    private bool OfreceSalidaAHiopos => PermitirVolverAHiopos && DisponibleEnHiopos;

    /// <summary>
    /// Un solo gesto con dos destinos posibles, y el orden importa: si esta pantalla
    /// ofrece la salida al POS, salirse SIN respondersela le deja la venta colgada.
    /// Por eso primero se intenta devolver el control y solo si esta pantalla no
    /// ofrece esa salida se hace el pop normal.
    /// </summary>
    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            if (OfreceSalidaAHiopos
                && IPlatformApplication.Current?.Services.GetService<IHioposExit>()
                       ?.Volver("ScaffoldView") == true)
                return;   // ya se devolvio el control; la Activity se esta cerrando

            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            // Un boton "atras" que lanza deja al cajero encerrado en la pantalla.
            AppLogger.E("ScaffoldView", "Fallo el boton atras.", ex);
        }
    }
}
