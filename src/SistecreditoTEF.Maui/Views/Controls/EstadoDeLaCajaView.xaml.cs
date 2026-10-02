using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Views.Controls;

/// <summary>
/// Code-behind de [EstadoDeLaCajaView]. Ver el XAML para el por que del control.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE LOS DATOS NO VIENEN POR BINDING
/// ─────────────────────────────────────────────────────────────────────────────
/// Este control vive DENTRO de la cabecera de cada pantalla, asi que su
/// BindingContext es el ViewModel de la pagina que lo hospeda —uno distinto en
/// cada una—. Hacerlo por binding obligaria a que los trece ViewModels expusieran
/// las mismas dos propiedades, y la que se olvidara dejaria la cabecera en blanco.
///
/// Resolver los dos servicios del contenedor es el mismo camino que ya usa
/// [ScaffoldView] para la salida a HioPos, y deja el control autonomo: se pone en
/// cualquier pantalla y funciona.
///
/// NUNCA LANZA: es la cabecera. Si no se puede leer la tienda o la red, se muestra
/// lo que se pueda y la caja sigue operando.
/// </summary>
public partial class EstadoDeLaCajaView : ContentView
{
    /// <summary>
    /// Si se dibuja sobre la cabecera azul (true) o sobre el fondo claro de las
    /// pantallas de configuracion (false).
    /// </summary>
    public static readonly BindableProperty SobreOscuroProperty =
        BindableProperty.Create(nameof(SobreOscuro), typeof(bool), typeof(EstadoDeLaCajaView), false,
            propertyChanged: (b, _, _) => (b as EstadoDeLaCajaView)?.Refrescar());

    /// <inheritdoc cref="SobreOscuroProperty" />
    public bool SobreOscuro
    {
        get => (bool)GetValue(SobreOscuroProperty);
        set => SetValue(SobreOscuroProperty, value);
    }

    private IEstadoDeLaConexion? _salud;

    public EstadoDeLaCajaView()
    {
        InitializeComponent();

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Refrescar();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // El enganche se hace al APARECER y se suelta al irse: si se hiciera en el
        // constructor, cada pantalla visitada dejaria un suscriptor vivo colgado
        // del servicio de red —que es singleton— y la lista crece toda la jornada.
        try
        {
            _salud = Servicio<IEstadoDeLaConexion>();
            if (_salud is not null) _salud.Cambio += OnRedCambio;
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaCaja", $"No se pudo escuchar el estado del modulo: {ex.Message}");
        }

        Refrescar();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        try
        {
            if (_salud is not null) _salud.Cambio -= OnRedCambio;
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaCaja", $"No se pudo soltar el evento de estado: {ex.Message}");
        }
        finally
        {
            _salud = null;
        }
    }

    private void OnRedCambio(object? sender, EventArgs e)
    {
        // El evento llega del hilo que haya detectado el cambio; pintar fuera del
        // hilo de UI en Android revienta la vista.
        try
        {
            if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(Refrescar);
            else Refrescar();
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaCaja", $"No se pudo repintar el estado: {ex.Message}");
        }
    }

    /// <summary>
    /// Vuelve a leer la tienda y la red y repinta. Publico porque la pagina lo
    /// llama en <c>OnAppearing</c>: elegir la tienda ocurre en OTRA pantalla, y al
    /// volver esta cabecera tiene que decir la nueva. Es el mismo motivo por el que
    /// existen los <c>RefrescarTienda</c> de los ViewModels.
    /// </summary>
    public void Refrescar()
    {
        try
        {
            PintarTienda();
            PintarRed();
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaCaja", $"No se pudo dibujar el estado de la caja: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------

    private void PintarTienda()
    {
        if (GetTemplateChild("PART_Tienda") is not Label etiqueta) return;

        var tienda = Servicio<ITiendaEnOperacion>();
        var texto  = tienda?.Etiqueta ?? "Sin tienda";
        var falta  = tienda is null || !tienda.EstaProvisionada;

        etiqueta.Text = texto;

        // Sin tienda elegida el chip se pinta de aviso, no de informacion: esa caja
        // no puede registrar creditos y el instalador tiene que verlo sin leer.
        etiqueta.TextColor = falta
            ? Recurso(SobreOscuro ? "OnHeroWarning" : "Warning", "#B26A00")
            : Recurso(SobreOscuro ? "OnDark" : "TextPrimary", "#333333");

        if (GetTemplateChild("PART_TiendaChip") is Border chip)
        {
            chip.BackgroundColor = falta
                ? Recurso(SobreOscuro ? "OnHeroTile" : "WarningSoft", "#FBF0DE")
                : Recurso(SobreOscuro ? "OnHeroTile" : "SurfaceAlt", "#EDEDED");
            chip.Stroke = new SolidColorBrush(
                SobreOscuro ? Recurso("OnHeroTileBorder", "#4DFFFFFF") : Colors.Transparent);
        }
    }

    /// <summary>
    /// EL SEMAFORO, CON LOS CINCO ESTADOS QUE DE VERDAD EXISTEN.
    ///
    /// La version anterior tenia dos —"En línea" y "Sin conexión"— y los sacaba de
    /// si el terminal tenia wifi. En una caja eso esta en verde casi siempre, asi
    /// que el indicador no podia ponerse en rojo aunque el modulo no pudiera
    /// operar. Una luz que solo sabe decir que si no informa nada.
    ///
    /// Ahora sale de las llamadas reales a Credinet. Y mientras no haya habido
    /// ninguna dice "Sin verificar", que es la verdad: todavia no se sabe.
    /// </summary>
    private void PintarRed()
    {
        var salud = _salud?.Estado
                    ?? Servicio<IEstadoDeLaConexion>()?.Estado
                    ?? SaludDelModulo.SinVerificar;

        var (etiqueta, clave, respaldo) = salud switch
        {
            SaludDelModulo.EnLinea =>
                ("En línea", SobreOscuro ? "OnHeroOnline" : "Success", "#2E7D52"),

            // El unico que bloquea. Va en rojo y lo dice con todas las letras: no
            // es un problema de red que se arregle esperando.
            SaludDelModulo.TiendaRechazada =>
                ("Tienda rechazada", SobreOscuro ? "OnHeroDanger" : "Error", "#B23A2A"),

            SaludDelModulo.SinRed =>
                ("Sin conexión", SobreOscuro ? "OnHeroDanger" : "Error", "#B23A2A"),

            // Hay wifi pero Credinet no contesta. Es el caso que el indicador
            // viejo pintaba de verde.
            SaludDelModulo.SinRespuesta =>
                ("Sin respuesta", SobreOscuro ? "OnHeroWarning" : "Warning", "#B26A00"),

            _ => ("Sin verificar", SobreOscuro ? "OnHeroMuted" : "TextMuted", "#9A9A9A")
        };

        var color = Recurso(clave, respaldo);

        if (GetTemplateChild("PART_Red") is Label texto)
        {
            texto.Text = etiqueta;
            texto.TextColor = salud == SaludDelModulo.EnLinea
                ? Recurso(SobreOscuro ? "OnDark" : "TextSecondary", "#6B6B6B")
                : color;
        }

        if (GetTemplateChild("PART_Punto") is BoxView punto)
            punto.Color = color;

        if (GetTemplateChild("PART_RedChip") is Border chip)
        {
            var sano = salud is SaludDelModulo.EnLinea or SaludDelModulo.SinVerificar;
            chip.BackgroundColor = SobreOscuro
                ? Recurso("OnHeroTile", "#1FFFFFFF")
                : Recurso(sano ? "SurfaceAlt" : "ErrorSoft", "#EDEDED");
            chip.Stroke = new SolidColorBrush(
                SobreOscuro ? Recurso("OnHeroTileBorder", "#4DFFFFFF") : Colors.Transparent);
        }
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Servicio del contenedor, o null. Devuelve null en vez de lanzar porque este
    /// control se dibuja tambien durante el arranque, antes de que el contenedor
    /// este resuelto del todo.
    /// </summary>
    private static T? Servicio<T>() where T : class
    {
        try
        {
            return IPlatformApplication.Current?.Services.GetService<T>();
        }
        catch (Exception ex)
        {
            AppLogger.W("EstadoDeLaCaja", $"No se pudo resolver {typeof(T).Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Un color del diccionario de la app, con respaldo literal. El respaldo no es
    /// decorativo: si la clave se renombra en [Colors.xaml], sin el la cabecera
    /// quedaria con texto transparente en vez de con un color viejo.
    /// </summary>
    private static Color Recurso(string clave, string respaldo)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(clave, out var valor) == true
                && valor is Color color)
                return color;
        }
        catch (Exception) { /* diccionario no disponible todavia */ }

        return Color.FromArgb(respaldo);
    }
}
