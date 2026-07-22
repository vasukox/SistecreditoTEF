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

    private static async void OnPressed(object? sender, EventArgs e)
    {
        if (sender is VisualElement v)
            await v.ScaleTo(0.98, 60, Easing.CubicOut);
    }

    private static async void OnReleased(object? sender, EventArgs e)
    {
        if (sender is VisualElement v)
            await v.ScaleTo(1.0, 90, Easing.CubicOut);
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
    public double Offset { get; set; } = 10;
    public uint Duration { get; set; } = 300;
    public int DelayMs { get; set; }

    private bool _played;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.Opacity = 0;
        bindable.TranslationY = Offset;
        bindable.Loaded += OnLoaded;

        if (bindable.IsLoaded)
            OnLoaded(bindable, EventArgs.Empty);

        // Fallback garantizado: aunque Loaded no dispare, la vista se muestra.
        bindable.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(DelayMs + 600),
            () => Reveal(bindable, animate: false));
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.Loaded -= OnLoaded;
        base.OnDetachingFrom(bindable);
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not View v || _played) return;
        _played = true;
        v.Loaded -= OnLoaded;

        if (DelayMs > 0)
            await Task.Delay(DelayMs);

        var fade = v.FadeTo(1, Duration, Easing.CubicOut);
        var slide = v.TranslateTo(0, 0, Duration, Easing.CubicOut);
        await Task.WhenAll(fade, slide);
    }

    private void Reveal(View v, bool animate)
    {
        if (_played) return;
        _played = true;
        v.Opacity = 1;
        v.TranslationY = 0;
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

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not View v || _played) return;
        _played = true;
        v.Loaded -= OnLoaded;

        if (DelayMs > 0)
            await Task.Delay(DelayMs);

        v.Opacity = 1;
        await v.ScaleTo(1.12, 240, Easing.CubicOut);
        await v.ScaleTo(1.0, 140, Easing.CubicIn);
    }
}
