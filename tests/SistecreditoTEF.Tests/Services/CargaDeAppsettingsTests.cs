using System.Text;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SistecreditoTEF.Tests.Services;

/// <summary>
/// Cargar el <c>appsettings.json</c> embebido al arrancar la app.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL BUG QUE ESTAS PRUEBAS FIJAN
/// ─────────────────────────────────────────────────────────────────────────────
/// <c>AddJsonStream</c> NO lee el stream en el momento de la llamada: lo guarda y lo
/// parsea DESPUÉS, cuando alguien resuelve <c>IConfiguration</c>. El código lo
/// entregaba envuelto en un <c>using</c>, así que para cuando tocaba parsear el
/// stream ya estaba cerrado y la carga fallaba con <c>ObjectDisposed_StreamClosed</c>.
///
/// La excepción se comía en un catch, y la app arrancaba IGUAL pero con el
/// contenedor de configuración vacío. Como <c>Environment</c> queda en su valor por
/// defecto ("sandbox"), una caja de PRODUCCIÓN operaba en modo pruebas y la barrera
/// de coherencia (<c>ApiConfig.Validate</c>) no se activaba: el módulo se quedaba sin
/// protección, en silencio, en un POS que cobra plata real.
///
/// Estos tests fijan el patrón correcto —entregar un stream que no dependa de que
/// nadie lo cierre— sin depender de MAUI, que es lo que hacía imposible probarlo.
/// </summary>
public class CargaDeAppsettingsTests
{
    private const string Json = """{"Credinet":{"Environment":"production","StoreId":"abc"}}""";

    /// <summary>
    /// El caso que fallaba: el stream se entrega y se cierra, y el parseo ocurre
    /// después. Con el patrón viejo esto tira <c>ObjectDisposed_StreamClosed</c>.
    /// </summary>
    [Fact]
    public void Un_stream_cerrado_antes_de_resolver_la_configuracion_hace_fallar_la_carga()
    {
        var builder = new ConfigurationBuilder();

        using (var queSeCierra = new MemoryStream(Encoding.UTF8.GetBytes(Json)))
        {
            builder.AddJsonStream(queSeCierra);
        }

        // Acá es donde se parsea de verdad, y es donde el patrón viejo rompía.
        var ex = Record.Exception(() => builder.Build().GetValue<string>("Credinet:Environment"));

        Assert.NotNull(ex);
    }

    /// <summary>
    /// El patrón que hay que usar: copiar a memoria ANTES de entregar el stream, de
    /// modo que lo que se parsea ya no dependa de que nadie lo cierre.
    /// </summary>
    [Fact]
    public void Copiar_a_memoria_antes_de_entregar_el_stream_hace_la_carga_confiable()
    {
        var builder = new ConfigurationBuilder();

        byte[] contenido;
        using (var origen = new MemoryStream(Encoding.UTF8.GetBytes(Json)))
        {
            using var buffer = new MemoryStream();
            origen.CopyTo(buffer);
            contenido = buffer.ToArray();
        }

        builder.AddJsonStream(new MemoryStream(contenido));

        var config = builder.Build();
        Assert.Equal("production", config.GetValue<string>("Credinet:Environment"));
        Assert.Equal("abc", config.GetValue<string>("Credinet:StoreId"));
    }

    /// <summary>
    /// Lo que se parsea tiene que ser el contenido completo, no un stream truncado
    /// o a medio copiar: un <c>appsettings.json</c> a medias deja el ambiente sin
    /// definir y activa exactamente el fallo que se vino a corregir.
    /// </summary>
    [Fact]
    public void El_contenido_copiado_llega_completo()
    {
        var json = Json + " " + string.Join(' ', Enumerable.Repeat("/* relleno */", 50)) + " " + Json;

        byte[] contenido;
        using (var origen = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            using var buffer = new MemoryStream();
            origen.CopyTo(buffer);
            contenido = buffer.ToArray();
        }

        Assert.Equal(Encoding.UTF8.GetByteCount(json), contenido.Length);
    }
}
