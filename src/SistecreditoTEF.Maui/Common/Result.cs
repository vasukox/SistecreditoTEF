namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Tipo seguro que representa el resultado de una operacion de datos.
/// Equivalente 1:1 al ApiResult&lt;T&gt; sellado de Kotlin.
///
/// Shape:
///   - Ok&lt;U&gt;(U Data)        : con el dato de dominio.
///   - Failure&lt;U&gt;(ApiError)  : envuelve un ApiError categorizado.
///
/// POR QUE LOS RECORDS ANIDADOS SON GENERICOS (Ok&lt;U&gt;, Failure&lt;U&gt;):
///   Para soportar [Map&lt;R&gt;] sin perder el tipo. Si Ok y Failure
///   heredaran directo de ApiResult&lt;T&gt; (con T del padre), no
///   podriamos construir un Ok&lt;R&gt; al mapear T -> R.
///
/// DRY: las 3 causas de fallo (red, HTTP, negocio) se unifican en
/// ApiError, no se exponen 3 data classes distintas al UI.
/// </summary>
public abstract record ApiResult<T>
{
    public sealed record Ok<U>(U Data) : ApiResult<U>;

    public sealed record Failure<U>(ApiError Cause) : ApiResult<U>;

    /// <summary>
    /// Mappea el dato de exito preservando el Failure (V13: ya no se
    /// repite el generic redundante).
    ///
    /// HU8-973: el tipo enclosing DEBE calificarse a R. Ok/Failure estan
    /// anidados en ApiResult&lt;T&gt;, asi que su tipo CLR real es
    /// ApiResult&lt;T&gt;.Ok&lt;U&gt; (arrastra el T de afuera). Si aqui se
    /// construye "new Ok&lt;R&gt;(...)" dentro de un ApiResult&lt;TDto&gt;,
    /// el objeto queda como ApiResult&lt;TDto&gt;.Ok&lt;R&gt; y NO matchea el
    /// patron "case ApiResult&lt;R&gt;.Ok&lt;R&gt;" que usan el service y los
    /// ViewModels -> el switch cae de largo y la UI se cuelga en Loading.
    /// Calificar a ApiResult&lt;R&gt;.Ok&lt;R&gt; deja el T-enclosing en R.
    /// </summary>
    public ApiResult<R> Map<R>(Func<T, R> transform) => this switch
    {
        Ok<T> ok         => new ApiResult<R>.Ok<R>(transform(ok.Data)),
        Failure<T> fail  => new ApiResult<R>.Failure<R>(fail.Cause),
        _                => throw new InvalidOperationException("Unreachable")
    };
}
