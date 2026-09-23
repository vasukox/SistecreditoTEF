using Microsoft.Maui.Controls;

namespace SistecreditoTEF.Maui.Common.Behaviors;

/// <summary>
/// Pulso suave de opacidad para las barras "esqueleto" que ocupan el lugar del
/// resultado mientras Sistecredito responde.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POR QUE UN ESQUELETO Y NO UN SPINNER
/// ─────────────────────────────────────────────────────────────────────────────────
/// El spinner suelto no dice DONDE va a aparecer el resultado, y la simulacion tarda
/// lo suficiente para que el cajero se pregunte si toco bien. Unas barras con la forma
/// del resultado, latiendo despacio, comunican "esto se esta llenando" sin una palabra.
///
/// El pulso es lento y de rango corto a proposito (0.35 → 0.85 en ~900 ms): en una
/// caja, una animacion rapida o de mucho contraste se lee como una alarma.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// EL BUCLE SE APAGA SOLO
/// ─────────────────────────────────────────────────────────────────────────────────
/// Una animacion en bucle que nadie detiene sigue corriendo sobre una vista oculta y
/// se queda con el dispatcher para siempre. Aca se corta en tres lugares: cuando la
/// vista deja de ser visible, cuando el behavior se desprende, y por el token de
/// cancelacion, que ademas evita que dos ciclos se solapen si la visibilidad parpadea.
/// </summary>
public sealed class ShimmerBehavior : Behavior<View>
{
    /// <summary>Opacidad minima del pulso.</summary>
    public double MinOpacity { get; set; } = 0.35;

    /// <summary>Opacidad maxima del pulso.</summary>
    public double MaxOpacity { get; set; } = 0.85;

    /// <summary>Duracion de cada mitad del pulso, en ms.</summary>
    public uint Duration { get; set; } = 900;

    /// <summary>
    /// Desfase inicial. Con varias barras a distinto delay el conjunto se lee como una
    /// onda en vez de tres cosas parpadeando al unisono.
    /// </summary>
    public int DelayMs { get; set; }

    private CancellationTokenSource? _cts;
    private View? _view;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        bindable.PropertyChanged += OnViewPropertyChanged;

        if (bindable.IsVisible)
            Arrancar(bindable);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.PropertyChanged -= OnViewPropertyChanged;
        Detener();
        _view = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnViewPropertyChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(View.IsVisible) || _view is null) return;

        if (_view.IsVisible) Arrancar(_view);
        else                 Detener();
    }

    private void Arrancar(View v)
    {
        Detener();
        _cts = new CancellationTokenSource();
        Fire.AndForget(PulsarAsync(v, _cts.Token), "ShimmerBehavior");
    }

    private void Detener()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* ya liberado */ }
        finally { cts.Dispose(); }
    }

    private async Task PulsarAsync(View v, CancellationToken token)
    {
        try
        {
            if (DelayMs > 0) await Task.Delay(DelayMs, token);

            v.Opacity = MinOpacity;

            while (!token.IsCancellationRequested)
            {
                await v.FadeToAsync(MaxOpacity, Duration, Easing.SinInOut);
                if (token.IsCancellationRequested) break;
                await v.FadeToAsync(MinOpacity, Duration, Easing.SinInOut);

                // SALIDA POR VISTA MUERTA.
                //
                // Las tres cancelaciones de arriba cubren los casos normales, pero
                // todas dependen de que alguien avise. Si la vista se desprende sin
                // que MAUI llame OnDetachingFrom, nadie cancela: y sobre una vista
                // sin handler FadeTo no anima, retorna YA. Los dos await dejan de
                // ceder el control y el while pasa a girar sin freno en el hilo de
                // UI, o sea la pantalla congelada en una caja.
                //
                // Con esto el bucle se apaga solo en cuanto la vista deja de estar
                // en pantalla, sin depender de que le avisen.
                if (!v.IsVisible || v.Handler is null) break;

                // Piso de tiempo por ciclo. Es irrelevante frente a los ~1800 ms del
                // pulso, y garantiza que el bucle ceda el control aunque las dos
                // animaciones vuelvan al instante.
                await Task.Delay(1, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Esperado al ocultarse o al desprenderse.
        }
        catch (Exception)
        {
            // Una animacion que falla no puede tumbar la pantalla: se sale del bucle
            // y el flujo sigue.
        }
        finally
        {
            // Si quedo a media transicion y ya no se ve, se deja en un estado limpio
            // para la proxima vez que se muestre.
            if (!v.IsVisible) v.Opacity = 1;
        }
    }
}
