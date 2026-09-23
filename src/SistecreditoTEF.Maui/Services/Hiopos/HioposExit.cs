using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// Devolver el control a HioPos desde cualquier pantalla del flujo, sin cobrar.
///
/// ───────────────────────────────────────────────────────────────────────────────
/// POR QUE ES UN SERVICIO Y NO CODIGO EN CADA PAGINA
/// ───────────────────────────────────────────────────────────────────────────────
/// Salirse de una venta de HioPos son tres pasos que hay que hacer SIEMPRE los
/// tres, en orden, o algo queda roto:
///   1. leer el TransactionType antes de limpiar (Clear lo borra),
///   2. limpiar el estado del flujo,
///   3. entregarle el resultado al POS.
///
/// Si se olvida el paso 3, HioPos queda ESPERANDO para siempre con la venta
/// colgada. Eso ya paso una vez —el "atras" de la primera pantalla se comia el
/// evento sin responder— y el sintoma en caja no se parece nada a la causa. Con
/// un solo lugar que lo haga, agregar el boton a una pantalla nueva no puede
/// reintroducir ese bug.
///
/// Fuera de HioPos (abonos abiertos desde el icono) [Disponible] es false y
/// [Volver] no hace nada: el "atras" de esas pantallas es un pop normal.
/// </summary>
public interface IHioposExit
{
    /// <summary>
    /// True solo si hay una operacion de HioPos viva esperando resultado, es decir
    /// si tiene sentido ofrecerle al cajero un boton "Volver a HioPos".
    /// </summary>
    bool Disponible { get; }

    /// <summary>
    /// Devuelve el control a HioPos sin cobrar y cierra el modulo.
    /// </summary>
    /// <param name="origen">Pantalla o gesto que lo disparo; solo para el log.</param>
    /// <returns>
    /// true si se devolvio el control (el llamador NO debe navegar); false si no
    /// habia nada que devolver o si fallo, y entonces el llamador debe hacer su
    /// navegacion normal — es peor dejar al cajero atrapado en la pantalla.
    /// </returns>
    bool Volver(string origen);
}

/// <inheritdoc />
public sealed class HioposExit : IHioposExit
{
    private readonly ITransactionStateStore _state;
    private readonly ITransactionResultHandler _resultHandler;
    private readonly HioposResultBuilder _resultBuilder;

    public HioposExit(
        ITransactionStateStore state,
        ITransactionResultHandler resultHandler,
        HioposResultBuilder resultBuilder)
    {
        _state = state;
        _resultHandler = resultHandler;
        _resultBuilder = resultBuilder;
    }

    public bool Disponible => _state.HioposTransactionActive;

    public bool Volver(string origen)
    {
        if (!_state.HioposTransactionActive)
            return false;

        try
        {
            // El TransactionType se lee ANTES de Clear(), que lo borra junto con el
            // resto del estado. Se hace eco al POS para que sepa que operacion cierra.
            var tipo = _state.ActiveTransaction?.TransactionType;

            AppLogger.I("HioposExit",
                $"Volver a HioPos desde {origen}: no hubo cobro, se devuelve FAILED con mensaje propio.");

            _state.Clear();
            _resultHandler.FinishWithResult(_resultBuilder.BuildTransactionCanceledByUser(tipo));
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.E("HioposExit",
                $"No se pudo devolver el control a HioPos desde {origen}; se deja la navegacion normal.", ex);
            return false;
        }
    }
}
