using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace SistecreditoTEF.Maui.Common.Behaviors;

/// <summary>
/// HU8-973 (UI/UX): feedback tactil. Al presionar un boton lo encoge
/// levemente y al soltar lo restaura con un pequeno rebote.
/// </summary>
public sealed class PressableBehavior : Behavior<Button>
{
    protected override void OnAttachedTo(Button bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.Pressed += OnPressed;
        bindable.Released += OnReleased;
    }

    protected override void OnDetachingFrom(Button bindable)
    {
        bindable.Pressed -= OnPressed;
        bindable.Released -= OnReleased;
        base.OnDetachingFrom(bindable);
    }

    // Los dos handlers son async void (lo exige el evento) y por eso van envueltos
    // en try/catch: una excepcion que se escape de un async void sube al
    // SynchronizationContext de Android y MATA el proceso. Que un boton no rebote
    // es invisible; que la app se cierre al tocarlo, no.

    private static async void OnPressed(object? sender, EventArgs e)
    {
        try
        {
            if (sender is VisualElement v)
                await v.ScaleToAsync(0.97, 70, Easing.CubicOut);
        }
        catch (Exception ex)
        {
            AppLogger.W("PressableBehavior", $"Animacion de presion fallida: {ex.Message}");
        }
    }

    private static async void OnReleased(object? sender, EventArgs e)
    {
        try
        {
            if (sender is not VisualElement v) return;

            // SpringOut en la vuelta: el boton "responde" en vez de solo volver.
            // Es la diferencia entre un tap que se siente mecanico y uno que se
            // siente fisico.
            await v.ScaleToAsync(1.0, 140, Easing.SpringOut);
        }
        catch (Exception ex)
        {
            AppLogger.W("PressableBehavior", $"Animacion de soltado fallida: {ex.Message}");

            // Si la animacion fallo a mitad, el boton podria quedar encogido.
            if (sender is VisualElement v) v.Scale = 1.0;
        }
    }
}

/// <summary>
/// HU8-973 (UI/UX): entrada sutil (fade + leve desplazamiento). Discreta.
///
/// A PRUEBA DE FALLOS: el contenido SIEMPRE termina visible. Si el evento
/// Loaded no dispara (pasa en Shell segun el momento del ciclo de vida),
/// un fallback por Dispatcher restaura la opacidad. Nunca deja la pantalla
/// en blanco.
/// </summary>
public sealed class EntranceBehavior : Behavior<View>
{
    public double Offset { get; set; } = 14;
    public uint Duration { get; set; } = 340;
    public int DelayMs { get; set; }

    /// <summary>
    /// Escala inicial. Muy cerca de 1 a proposito: el elemento "se acerca" apenas,
    /// lo justo para que la entrada se sienta material en vez de un fade plano.
    /// Valores mas bajos se leen como un pop de juguete, fuera de lugar en un POS.
    /// </summary>
    public double FromScale { get; set; } = 0.985;

    private bool _played;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.Opacity = 0;
        bindable.TranslationY = Offset;
        bindable.Scale = FromScale;
        bindable.Loaded += OnLoaded;

        if (bindable.IsLoaded)
            OnLoaded(bindable, EventArgs.Empty);

        // Fallback garantizado: aunque Loaded no dispare, la vista se muestra.
        bindable.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(DelayMs + 600),
            () => Reveal(bindable));
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.Loaded -= OnLoaded;
        base.OnDetachingFrom(bindable);
    }

    // async void por la firma del evento. Va envuelto porque una excepcion que se
    // escape de aca sube al manejador global y mata el proceso, y este handler
    // anima una vista que puede estar siendo destruida al mismo tiempo: con el
    // cajero navegando rapido, la animacion sigue corriendo despues de que la
    // pagina se fue. Perder la animacion es cosmetico; tirar la app en una caja no.
    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not View v || _played) return;
        _played = true;
        v.Loaded -= OnLoaded;

        try
        {
            if (DelayMs > 0)
                await Task.Delay(DelayMs);

            // Las tres propiedades se animan JUNTAS y con la misma curva: si cada una
            // llevara su duracion, el elemento llegaria a destino por partes y se veria
            // como un glitch en vez de un movimiento.
            await Task.WhenAll(
                v.FadeToAsync(1, Duration, Easing.CubicOut),
                v.TranslateToAsync(0, 0, Duration, Easing.CubicOut),
                v.ScaleToAsync(1, Duration, Easing.CubicOut));
        }
        catch (Exception ex)
        {
            // Se deja el elemento en su estado final a mano. Si la animacion murio
            // a mitad de camino, sin esto quedaria medio transparente o corrido
            // para siempre.
            ForzarEstadoFinal(v);
            AppLogger.W("EntranceBehavior", $"Animacion de entrada interrumpida: {ex.Message}");
        }
    }

    private static void ForzarEstadoFinal(View v)
    {
        try
        {
            v.Opacity = 1;
            v.TranslationY = 0;
            v.Scale = 1;
        }
        catch (Exception) { /* la vista ya no existe: no hay nada que dejar visible */ }
    }

    /// <summary>
    /// Muestra el elemento SIN animar.
    ///
    /// Es la red de seguridad: si <c>Loaded</c> nunca dispara —pasa en algunas
    /// recreaciones de la Activity— sin esto el elemento se quedaria invisible para
    /// siempre. Una animacion que no corre es un detalle; una pantalla en blanco en
    /// una caja es una venta perdida.
    /// </summary>
    private void Reveal(View v)
    {
        if (_played) return;
        _played = true;
        ForzarEstadoFinal(v);
    }
}

/// <summary>
/// HU8-973 (UI/UX): "pop" de entrada con rebote — escala 0 → 1.12 → 1 + fade.
/// Ideal para el ícono de éxito (check). A prueba de fallos: si Loaded no
/// dispara, un fallback restaura la vista visible.
/// </summary>
public sealed class PopInBehavior : Behavior<View>
{
    public int DelayMs { get; set; }

    private bool _played;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.Scale = 0;
        bindable.Opacity = 0;
        bindable.Loaded += OnLoaded;
        if (bindable.IsLoaded)
            OnLoaded(bindable, EventArgs.Empty);

        bindable.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(DelayMs + 700),
            () => { if (!_played) { _played = true; bindable.Scale = 1; bindable.Opacity = 1; } });
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.Loaded -= OnLoaded;
        base.OnDetachingFrom(bindable);
    }

    // Mismo motivo que en EntranceBehavior: async void por la firma del evento, y
    // envuelto para que una vista destruida a mitad del rebote no tire el proceso.
    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not View v || _played) return;
        _played = true;
        v.Loaded -= OnLoaded;

        try
        {
            if (DelayMs > 0)
                await Task.Delay(DelayMs);

            v.Opacity = 1;
            await v.ScaleToAsync(1.12, 240, Easing.CubicOut);
            await v.ScaleToAsync(1.0, 140, Easing.CubicIn);
        }
        catch (Exception ex)
        {
            try { v.Opacity = 1; v.Scale = 1; } catch (Exception) { }
            AppLogger.W("PopInBehavior", $"Animacion de entrada interrumpida: {ex.Message}");
        }
    }
}
