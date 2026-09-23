using Microsoft.Maui.Controls;

namespace SistecreditoTEF.Maui.Common.Behaviors;

/// <summary>
/// Formatea un <see cref="Entry"/> de monto con separador de miles mientras el
/// cajero escribe: "50000" → "50.000".
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL TEXTO NO SE REESCRIBE DENTRO DE TextChanged. NUNCA.
/// ─────────────────────────────────────────────────────────────────────────────
/// Esta es la regla que sostiene todo el archivo, y viene de un crash reproducido
/// en la caja: el cajero elegía un crédito, cambiaba el monto precargado y la app
/// se cerraba sola.
///
/// La traza, capturada en el terminal:
///
///   java.lang.IllegalArgumentException: end should be &lt; than charSequence length
///     at androidx.emoji2.text.EmojiCompat.process
///     at androidx.emoji2.viewsintegration.EmojiTextWatcher.afterTextChanged
///     at android.widget.TextView.sendAfterTextChanged
///     at android.text.SpannableStringBuilder.replace
///     at android.text.method.NumberKeyListener.onKeyDown
///
/// Cómo se produce: al presionar una tecla, <c>NumberKeyListener</c> reemplaza el
/// texto del <c>SpannableStringBuilder</c> y dispara <c>afterTextChanged</c> de
/// forma SINCRÓNICA. Nuestro handler corría ahí dentro y cambiaba el LARGO del
/// texto al reformatearlo. Cuando después le tocaba el turno al TextWatcher de
/// <c>androidx.emoji2</c>, ése seguía teniendo los índices start/end de ANTES del
/// reformateo, y llamaba a <c>EmojiCompat.process</c> con un <c>end</c> que ya
/// caía fuera del texto nuevo —más corto—. Excepción de Java, en el hilo main,
/// nacida en un TextWatcher de terceros: ningún try/catch nuestro la alcanza.
///
/// La solución es no pelearse con esa ventana sino salir de ella: el reformateo se
/// DIFIERE al siguiente turno del dispatcher. El evento de tecla termina, emoji2
/// procesa el texto que realmente hay, y recién entonces se reescribe. Cuando la
/// reescritura ocurre fuera del dispatch del teclado, los índices que ve emoji2
/// corresponden al texto nuevo y no hay nada inconsistente.
///
/// Un intento anterior atacó el orden de <c>CursorPosition</c> / SelectionLength
/// creyendo que el problema era un <c>setSelection</c> fuera de rango. No era eso,
/// y el crash siguió igual. El manejo de cursor que quedó abajo es correcto e
/// igual hace falta —el cursor tiene que terminar al final del monto— pero no es
/// lo que evita el cierre de la app.
///
/// El ViewModel queda con una sola responsabilidad: leer el valor de los dígitos
/// (ver [MoneyInput]). No toca el texto.
/// </summary>
public sealed class MoneyEntryBehavior : Behavior<Entry>
{
    /// <summary>Evita reentrar cuando la propia reescritura dispara TextChanged.</summary>
    private bool _actualizando;

    /// <summary>
    /// Evita encolar varios reformateos si el cajero teclea rápido: alcanza con uno,
    /// porque siempre se relee el texto actual del campo.
    /// </summary>
    private bool _reformateoEncolado;

    protected override void OnAttachedTo(Entry bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.TextChanged += OnTextChanged;
    }

    protected override void OnDetachingFrom(Entry bindable)
    {
        bindable.TextChanged -= OnTextChanged;
        base.OnDetachingFrom(bindable);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_actualizando || _reformateoEncolado || sender is not Entry entry) return;

        try
        {
            var actual = e.NewTextValue ?? string.Empty;

            // Si ya está bien escrito no se toca nada: es el caso más común (el
            // cajero escribiendo dígitos que no cruzan un millar) y así se evita
            // encolar trabajo por gusto.
            if (string.Equals(MoneyInput.Format(actual), actual, StringComparison.Ordinal))
                return;

            // ACÁ NO SE ESCRIBE. Se difiere: ver el comentario de la clase.
            _reformateoEncolado = true;
            entry.Dispatcher.Dispatch(() => AplicarFormato(entry));
        }
        catch (Exception ex)
        {
            _reformateoEncolado = false;
            AppLogger.W("MoneyEntryBehavior",
                $"No se pudo encolar el formateo del monto: {ex.Message}");
        }
    }

    /// <summary>
    /// Reescribe el campo ya formateado. Corre en un turno propio del dispatcher, o
    /// sea fuera del dispatch del evento de tecla, que es justo lo que evita el
    /// crash de emoji2.
    /// </summary>
    private void AplicarFormato(Entry entry)
    {
        _reformateoEncolado = false;
        if (_actualizando) return;

        try
        {
            // Se relee el texto ACTUAL en vez de usar el que venía en el evento: para
            // cuando llega este turno, el cajero ya pudo haber tecleado más.
            var actual = entry.Text ?? string.Empty;
            var formateado = MoneyInput.Format(actual);
            if (string.Equals(formateado, actual, StringComparison.Ordinal)) return;

            _actualizando = true;

            // Se colapsa la selección antes de acortar el texto para no dejar un rango
            // del texto viejo apuntando fuera del nuevo.
            ColapsarSeleccion(entry);

            entry.Text = formateado;
            FijarCursorAlFinal(entry, formateado.Length);
        }
        catch (Exception ex)
        {
            // Que el monto salga sin puntos es aceptable; que la app se cierre
            // durante un cobro, no.
            AppLogger.W("MoneyEntryBehavior",
                $"No se pudo formatear el monto, se deja el texto crudo: {ex.Message}");
        }
        finally
        {
            _actualizando = false;
        }
    }

    /// <summary>
    /// Deja el campo sin selección y con el cursor al principio, que es la única
    /// posición válida para cualquier texto no vacío.
    /// </summary>
    private static void ColapsarSeleccion(Entry entry)
    {
        try
        {
            entry.SelectionLength = 0;
            entry.CursorPosition = 0;
        }
        catch (Exception ex)
        {
            AppLogger.W("MoneyEntryBehavior", $"No se pudo colapsar la seleccion: {ex.Message}");
        }
    }

    /// <summary>
    /// Deja el cursor al final del monto, acotado al largo real del texto. Es lo que
    /// hace que se pueda seguir escribiendo después de cada reformateo.
    /// </summary>
    private static void FijarCursorAlFinal(Entry entry, int largo)
    {
        try
        {
            // Primero la selección, después la posición: MAUI traduce el par a un
            // setSelection(inicio, inicio + SelectionLength), así que un
            // SelectionLength viejo puede empujar el fin fuera del texto.
            entry.SelectionLength = 0;
            entry.CursorPosition = Math.Max(0, Math.Min(largo, entry.Text?.Length ?? 0));
        }
        catch (Exception ex)
        {
            AppLogger.W("MoneyEntryBehavior", $"No se pudo ubicar el cursor: {ex.Message}");
        }
    }
}
