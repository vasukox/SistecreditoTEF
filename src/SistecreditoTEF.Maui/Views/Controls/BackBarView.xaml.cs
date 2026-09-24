using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Views.Controls;

/// <summary>
/// Code-behind de [BackBarView]. Ver el XAML para el por que del control.
///
/// El texto y el destino del boton se resuelven UNA vez, al aplicarse el template:
/// el modo (dentro de HioPos o standalone) queda fijado por el Intent con el que
/// arranco la Activity y no cambia mientras la pantalla vive.
/// </summary>
public partial class BackBarView : ContentView
{
    public static readonly BindableProperty TitleTextProperty =
        BindableProperty.Create(nameof(TitleText), typeof(string), typeof(BackBarView), string.Empty);

    /// <summary>Titulo de la pantalla, al lado del boton (reemplaza al del Shell).</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly BindableProperty PuedeVolverProperty =
        BindableProperty.Create(nameof(PuedeVolver), typeof(bool), typeof(BackBarView), true,
            propertyChanged: OnPuedeVolverChanged);

    /// <summary>
    /// Permite APAGAR el boton sin quitarlo del arbol. Lo usa la pantalla de abono
    /// mientras el cobro esta en vuelo: irse a HioPos en ese momento dejaria un
    /// pago en un estado que el POS no conoce.
    /// </summary>
    public bool PuedeVolver
    {
        get => (bool)GetValue(PuedeVolverProperty);
        set => SetValue(PuedeVolverProperty, value);
    }

    /// <summary>
    /// Si esta pantalla puede ofrecer "Volver a HioPos". Por defecto FALSE y hay que
    /// pedirlo: el boton pertenece al modulo de VENTA (pagar a credito). En el de
    /// RECAUDO (pagar credito) la flecha es para moverse dentro del flujo.
    /// </summary>
    public static readonly BindableProperty PermitirVolverAHioposProperty =
        BindableProperty.Create(nameof(PermitirVolverAHiopos), typeof(bool), typeof(BackBarView), false,
            propertyChanged: (b, _, _) => (b as BackBarView)?.ActualizarBoton());

    /// <inheritdoc cref="PermitirVolverAHioposProperty" />
    public bool PermitirVolverAHiopos
    {
        get => (bool)GetValue(PermitirVolverAHioposProperty);
        set => SetValue(PermitirVolverAHioposProperty, value);
    }

    public BackBarView()
    {
        InitializeComponent();
    }

    private static void OnPuedeVolverChanged(BindableObject bindable, object oldValue, object newValue) =>
        (bindable as BackBarView)?.ActualizarBoton();

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Back") is not Button boton)
            return;

        boton.Clicked -= OnBackClicked;
        boton.Clicked += OnBackClicked;

        ActualizarBoton();
    }

    /// <summary>
    /// Pone el texto y la visibilidad del boton segun el estado ACTUAL.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POR QUE NO ALCANZA CON HACERLO EN OnApplyTemplate
    /// ─────────────────────────────────────────────────────────────────────────────
    /// [OnApplyTemplate] corre cuando el control aplica su propio ControlTemplate, o
    /// sea dentro de su InitializeComponent — ANTES de que la pagina padre le asigne
    /// PermitirVolverAHiopos="True". Leerlo ahi devolvia siempre el default false.
    ///
    /// El sintoma, visto en terminal: en plena venta el boton decia "Atras" en vez de
    /// "Volver a HioPos"... y al tocarlo TE SACABA a HioPos igual, porque el handler
    /// del click si lee la propiedad tarde, cuando ya esta puesta. Un boton que dice
    /// una cosa y hace otra es peor que cualquiera de las dos.
    ///
    /// Por eso se llama tambien desde el propertyChanged: la ultima asignacion gana,
    /// venga del template o del padre.
    /// </summary>
    private void ActualizarBoton()
    {
        if (GetTemplateChild("PART_Back") is not Button boton) return;

        boton.Text = OfreceSalidaAHiopos ? "←  Volver a HioPos" : "←  Atras";
        boton.IsVisible = PuedeVolver;
    }

    /// <summary>
    /// Las dos condiciones son necesarias: que la pantalla lo pida Y que haya una
    /// operacion de HioPos viva. Sin la primera el boton aparecia tambien en el
    /// modulo de recaudo, donde no corresponde.
    /// </summary>
    private bool OfreceSalidaAHiopos =>
        PermitirVolverAHiopos
        && IPlatformApplication.Current?.Services.GetService<IHioposExit>()?.Disponible == true;

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            if (OfreceSalidaAHiopos
                && IPlatformApplication.Current?.Services.GetService<IHioposExit>()
                       ?.Volver("BackBarView") == true)
                return;   // el control ya volvio al POS y la Activity se esta cerrando

            // EL BOTON PUEDE DECIR "VOLVER A HIOPOS" Y NO PODER HACERLO.
            //
            // El texto se fija al dibujar la pantalla; la operacion de HioPos puede
            // haberse terminado despues. Entonces cae en el pop de abajo, que en la
            // primera pantalla del flujo lleva a la raiz del Shell, que resuelve
            // destino y manda a abonos: el cajero toca "Volver a HioPos" y aterriza
            // en pagar credito.
            //
            // El motivo de fondo —una Activity que se iba borrando el estado de la
            // que llegaba— esta corregido en [MainActivity.OnDestroy]. Esta traza
            // queda para que la proxima vez que pase se vea en el log en vez de
            // deducirse de un sintoma que no se parece a la causa.
            if (PermitirVolverAHiopos)
                AppLogger.W("BackBarView",
                    "Se pidio volver a HioPos pero no hay operacion viva que devolver: " +
                    "se hace el 'atras' normal.");

            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            // Un boton "atras" que lanza deja al cajero encerrado en la pantalla.
            AppLogger.E("BackBarView", "Fallo el boton atras.", ex);
        }
    }
}
