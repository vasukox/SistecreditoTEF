using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.Tiendas;

/// <summary>Como le fue a la comprobacion contra Sistecredito.</summary>
public enum ResultadoDeVerificacion
{
    /// <summary>Sistecredito reconoce la tienda de esta caja.</summary>
    Aceptada,

    /// <summary>
    /// Sistecredito contesto <c>225 StoreNotFound</c>: esta caja esta mandando un
    /// identificador que para ellos no existe. No se puede operar.
    /// </summary>
    Rechazada,

    /// <summary>No se pudo llegar a Sistecredito. No dice nada de la tienda.</summary>
    SinRespuesta,

    /// <summary>
    /// En pruebas la tienda NO viaja a Credinet a proposito, asi que no hay nada
    /// que verificar. Se dice en vez de inventar un "todo bien".
    /// </summary>
    NoAplicaEnPruebas
}

/// <summary>
/// EL BOTON QUE PREGUNTA SI ESTA TIENDA EXISTE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE HACIA FALTA ADEMAS DEL SEMAFORO
/// ─────────────────────────────────────────────────────────────────────────────
/// [IEstadoDeLaConexion] es PASIVO: se entera del estado de la tienda a partir de
/// las llamadas que el modulo hace igual, operando. Eso tiene un agujero grande
/// justo donde mas duele — al INSTALAR. El instalador elige la tienda, no vende
/// nada todavia, y se va con el semaforo en "sin verificar". El error se descubre
/// con la primera venta, que es exactamente lo que se venia queriendo evitar.
///
/// Esto lo convierte en una pregunta explicita: se elige la tienda y en el acto se
/// comprueba contra Sistecredito, antes de que nadie cobre nada.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE SE PREGUNTA CON getSimulatedMonthLimit
/// ─────────────────────────────────────────────────────────────────────────────
/// De las siete llamadas del modulo es la UNICA que lleva la tienda y no lleva
/// datos de ningun cliente: solo un monto. Verificar la configuracion de una caja
/// no puede obligar a teclear la cedula de alguien, y menos la de un cliente
/// cualquiera que pase por ahi.
///
/// Ademas no escribe nada: es una consulta de simulacion. Se puede tocar el boton
/// las veces que haga falta sin consecuencias.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LO QUE ESTA COMPROBACION NO DEMUESTRA
/// ─────────────────────────────────────────────────────────────────────────────
/// Que Sistecredito ACEPTE el identificador no prueba que vaya a atribuir los
/// creditos a esa tienda: la respuesta no dice a nombre de quien quedaria nada. Si
/// el identificador existe pero del otro lado apunta a otra tienda, esto da verde
/// igual. Queda escrito aqui para que el verde no se lea como mas de lo que es.
/// </summary>
public sealed class VerificacionDeTienda(
    ICredinetRepository credinet,
    IApiConfigSource configuracion)
{
    /// <summary>
    /// Monto de la consulta. Cualquiera sirve —no se crea nada— pero se usa uno
    /// redondo y comun para que, si alguien mira el log de Sistecredito, se
    /// reconozca como una comprobacion y no como una simulacion de venta.
    /// </summary>
    private const double MontoDeLaConsulta = 500_000;

    /// <summary>
    /// Pregunta si la tienda configurada en esta caja existe para Sistecredito.
    /// NUNCA lanza: es un boton de diagnostico.
    /// </summary>
    public async Task<ResultadoDeVerificacion> VerificarAsync()
    {
        try
        {
            var config = configuracion.Current;

            // En pruebas el identificador no viaja (ver [ApiConfig.StoreId]), asi
            // que la llamada saldria sin tienda y volveria OK siempre. Dar verde
            // ahi seria decirle al instalador que comprobo algo que no comprobo.
            if (!config.IsProduction || string.IsNullOrWhiteSpace(config.StoreId))
                return ResultadoDeVerificacion.NoAplicaEnPruebas;

            var resultado = await credinet.GetSimulatedMonthLimitAsync(MontoDeLaConsulta);

            return resultado switch
            {
                ApiResult<Models.SimulatedMonthLimit>.Ok<Models.SimulatedMonthLimit> =>
                    ResultadoDeVerificacion.Aceptada,

                // 225 StoreNotFound: la respuesta llego y dice que esta tienda no
                // existe. Es el unico "no" que importa.
                ApiResult<Models.SimulatedMonthLimit>.Failure<Models.SimulatedMonthLimit>
                    { Cause: ApiError.Business { Code: EstadoDeLaConexion.StoreNotFound } } =>
                    ResultadoDeVerificacion.Rechazada,

                // Cualquier otro error de NEGOCIO significa que Sistecredito
                // contesto y que la tienda le sirvio: lo que no le gusto fue otra
                // cosa (el monto, por ejemplo). La tienda esta bien.
                ApiResult<Models.SimulatedMonthLimit>.Failure<Models.SimulatedMonthLimit>
                    { Cause: ApiError.Business } =>
                    ResultadoDeVerificacion.Aceptada,

                _ => ResultadoDeVerificacion.SinRespuesta
            };
        }
        catch (Exception ex)
        {
            AppLogger.E("VerificacionDeTienda", "No se pudo verificar la tienda.", ex);
            return ResultadoDeVerificacion.SinRespuesta;
        }
    }
}
