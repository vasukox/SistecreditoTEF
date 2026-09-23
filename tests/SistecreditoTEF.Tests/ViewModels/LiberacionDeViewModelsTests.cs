using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// Un ViewModel registrado en el contenedor NO puede implementar solo
/// <see cref="IAsyncDisposable"/>.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// ESTO TUMBO LA APP EN PRODUCCION
/// ─────────────────────────────────────────────────────────────────────────────
/// [ReplicacionViewModel] se escribio con <c>DisposeAsync</c> porque cierra un
/// socket, que es una operacion asincrona. Parece lo correcto y no lo es: el
/// contenedor de dependencias de MAUI libera los servicios <c>transient</c> de
/// forma SINCRONA, y al encontrarse uno que solo sabe liberarse async lanza
///
///   [System.InvalidOperationException]: AsyncDisposableServiceDispose,
///   SistecreditoTEF.Maui.ViewModels.ReplicacionViewModel
///
/// Capturado el 22/09/2026 en la caja 3 de la primera tienda: el padron se copiaba
/// bien —la excepcion llega despues de guardar— y la app se caia 11 segundos mas
/// tarde, al salir de la pantalla.
///
/// El cierre del socket vive donde corresponde: <c>OnDisappearing</c> de la Page,
/// que ademas es lo que hace que el servicio muera con la PANTALLA y no con el
/// contenedor. Agregar <c>IDisposable</c> para "arreglarlo" seria peor: obligaria
/// a cerrar bloqueando un hilo del pool en una app de pagos.
/// </summary>
public class LiberacionDeViewModelsTests
{
    [Fact]
    public void ReplicacionViewModel_no_implementa_IAsyncDisposable()
    {
        Assert.False(
            typeof(IAsyncDisposable).IsAssignableFrom(typeof(ReplicacionViewModel)),
            "Un ViewModel transient que solo implementa IAsyncDisposable hace que el " +
            "contenedor lance AsyncDisposableServiceDispose al liberarlo. El cierre va " +
            "en OnDisappearing de la Page.");
    }

    /// <summary>
    /// La contracara: el metodo que apaga el socket tiene que seguir existiendo, y
    /// ser publico, porque lo llama la Page. Si alguien lo borra junto con el
    /// DisposeAsync, la caja queda repartiendo el padron en la red.
    /// </summary>
    [Fact]
    public void ReplicacionViewModel_conserva_el_apagado_que_llama_la_Page()
    {
        var metodo = typeof(ReplicacionViewModel).GetMethod("DetenerAsync");

        Assert.NotNull(metodo);
        Assert.True(metodo!.IsPublic);
    }
}
