using System.ComponentModel;
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
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO QUE SE MUEVE, Y POR QUÉ SE MUEVE DESDE ACÁ
/// ─────────────────────────────────────────────────────────────────────────────
/// El ViewModel dice CUÁNTO queda; esta clase lo hace visible. Son tres cosas:
///
///   · La barra se vacía sola. Se logra con el temporizador a 200 ms, no animando:
///     una ventana de 10 minutos avanza 1/3000 por tic, o sea que el movimiento ya
///     es continuo y no hace falta interpolar nada entre dos valores.
///   · Bajo el último minuto el código LATE una vez por segundo. Desde el otro
///     extremo de la tienda no se lee un reloj, pero un pulso sí se ve.
///   · La tarjeta del código y la de confirmación entran con un fundido cuando
///     aparecen, en vez de materializarse de golpe a mitad de la pantalla.
///
/// Todas las animaciones terminan en un estado final garantizado: una animación
/// que muere a mitad no puede dejar una tarjeta a medio transparente en una caja.
/// </summary>
[QueryProperty(nameof(SoloCajeros), AppRoutes.Params.SoloCajeros)]
public partial class ReplicacionPage : ContentPage
{
    /// <summary>
    /// Modo ACTUALIZAR, puesto por la ruta.
    ///
    /// El modo viaja por parametro y no se deduce del estado de la caja a proposito:
    /// "ya tiene PIN configurado" no distingue a un instalador que viene a
    /// actualizar de uno que se equivoco de boton, y la diferencia entre los dos es
    /// si se pisa el PIN de administrador.
    ///
    /// Shell entrega los parametros como texto cuando la navegacion viene de una
    /// URI, asi que se acepta cualquier forma razonable de "si".
    /// </summary>
    public object? SoloCajeros
    {
        set
        {
            var pedido = value switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var b) && b,
                _ => false
            };

            if (BindingContext is ReplicacionViewModel vm)
                vm.SoloCajeros = pedido;

            AppLogger.I("ReplicacionPage",
                $"Modo de la pantalla: {(pedido ? "actualizar cajeros" : "copiar configuracion")}.");
        }
    }

    /// <summary>
    /// Cinco veces por segundo. El trabajo por tic es restar dos instantes y
    /// asignar propiedades observables, que no notifican si el valor no cambió:
    /// el texto del reloj redibuja una vez por segundo aunque se recalcule cinco.
    /// </summary>
    private static readonly TimeSpan Tic = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan PasoDelLatido = TimeSpan.FromSeconds(1);

    private readonly ReplicacionViewModel _vm;
    private IDispatcherTimer? _cuentaRegresiva;
    private DateTime _ultimoLatido = DateTime.MinValue;
    private bool _codigoPresentado;
    private bool _confirmacionPresentada;

    public ReplicacionPage(ReplicacionViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _vm.PropertyChanged += OnCambioDelViewModel;

        _cuentaRegresiva = Dispatcher.CreateTimer();
        _cuentaRegresiva.Interval = Tic;
        _cuentaRegresiva.Tick += OnTic;
        _cuentaRegresiva.Start();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        _vm.PropertyChanged -= OnCambioDelViewModel;

        if (_cuentaRegresiva is not null)
        {
            _cuentaRegresiva.Tick -= OnTic;
            _cuentaRegresiva.Stop();
            _cuentaRegresiva = null;
        }

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

    private void OnTic(object? sender, EventArgs e)
    {
        _vm.ActualizarEstadoEmisor();

        if (!_vm.PorVencer) return;

        // El tic va a 200 ms pero el latido es de uno por segundo: cinco pulsos por
        // segundo se leerían como un parpadeo roto, no como una cuenta regresiva.
        var ahora = DateTime.UtcNow;
        if (ahora - _ultimoLatido < PasoDelLatido) return;
        _ultimoLatido = ahora;

        Fire.AndForget(LatirAsync, "ReplicacionPage");
    }

    /// <summary>Un pulso corto de la tarjeta del código: último minuto.</summary>
    private async Task LatirAsync()
    {
        try
        {
            await TarjetaCodigo.ScaleToAsync(1.025, 180, Easing.CubicOut);
            await TarjetaCodigo.ScaleToAsync(1.0, 280, Easing.CubicIn);
        }
        catch (Exception ex)
        {
            // Si el pulso murió a mitad, la tarjeta quedaría agrandada para siempre.
            try { TarjetaCodigo.Scale = 1; } catch (Exception) { }
            AppLogger.W("ReplicacionPage", $"Latido del codigo interrumpido: {ex.Message}");
        }
    }

    private void OnCambioDelViewModel(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ReplicacionViewModel.VentanaAbierta):
                if (!_vm.VentanaAbierta)
                {
                    _codigoPresentado = false;
                    return;
                }

                if (_codigoPresentado) return;
                _codigoPresentado = true;
                Fire.AndForget(() => PresentarAsync(TarjetaCodigo), "ReplicacionPage");
                break;

            case nameof(ReplicacionViewModel.HayAlgoPorConfirmar):
                if (!_vm.HayAlgoPorConfirmar)
                {
                    _confirmacionPresentada = false;
                    return;
                }

                if (_confirmacionPresentada) return;
                _confirmacionPresentada = true;
                Fire.AndForget(() => PresentarAsync(TarjetaConfirmacion), "ReplicacionPage");
                break;
        }
    }

    /// <summary>
    /// Entrada de una tarjeta que acaba de aparecer: fundido corto con un leve
    /// acercamiento.
    ///
    /// No se usa [EntranceBehavior] porque ese se juega su única carta en
    /// <c>Loaded</c>, y estas dos tarjetas ya estaban cargadas —ocultas— desde que
    /// se abrió la pantalla. Lo que sí se copia de él es la red de seguridad: el
    /// estado final se garantiza por Dispatcher aunque la animación no llegue.
    /// </summary>
    private static async Task PresentarAsync(View vista)
    {
        // Pase lo que pase con la animación, la tarjeta termina visible. Sin esto,
        // una animación interrumpida deja al instalador mirando un hueco donde
        // tendría que estar el código.
        vista.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
        {
            try { vista.Opacity = 1; vista.Scale = 1; vista.TranslationY = 0; }
            catch (Exception) { /* la vista ya no existe */ }
        });

        try
        {
            vista.Opacity = 0;
            vista.Scale = 0.97;
            vista.TranslationY = 10;

            // Las tres juntas y con la misma curva: por separado el elemento llega
            // a destino por partes y se ve como un glitch.
            await Task.WhenAll(
                vista.FadeToAsync(1, 320, Easing.CubicOut),
                vista.ScaleToAsync(1, 320, Easing.CubicOut),
                vista.TranslateToAsync(0, 0, 320, Easing.CubicOut));
        }
        catch (Exception ex)
        {
            try { vista.Opacity = 1; vista.Scale = 1; vista.TranslationY = 0; }
            catch (Exception) { }
            AppLogger.W("ReplicacionPage", $"Entrada de tarjeta interrumpida: {ex.Message}");
        }
    }
}
