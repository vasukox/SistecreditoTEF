using SistecreditoTEF.Maui.Services.Pairing;

namespace SistecreditoTEF.Maui.Services.Auth;

/// <summary>
/// Persistencia del PIN de administrador y de los cajeros.
///
/// Es una interfaz para que la logica de ingreso ([AuthService]) se pueda testear
/// sin Android ni base de datos: el almacenamiento real usa la BD cifrada del
/// terminal, y los tests usan una implementacion en memoria.
/// </summary>
public interface IAuthStore
{
    /// <summary>
    /// Hash del PIN de administrador, o null si todavia no se configuro. Es lo que
    /// decide si la app arranca en la pantalla de configuracion inicial.
    /// </summary>
    Task<string?> GetAdminPinHashAsync();

    Task SetAdminPinHashAsync(string hash);

    /// <summary>Todos los cajeros, activos e inactivos.</summary>
    Task<IReadOnlyList<Cajero>> GetCajerosAsync();

    Task<Cajero?> GetCajeroAsync(string id);

    /// <summary>Crea o reemplaza un cajero (upsert por Id).</summary>
    Task GuardarCajeroAsync(Cajero cajero);

    /// <summary>
    /// Activa o desactiva a un cajero. NO se borra el registro: los abonos que hizo
    /// quedan referenciados por su nombre, y perder quien cobro deja un hueco en la
    /// trazabilidad. Un cajero inactivo deja de aparecer para ingresar.
    ///
    /// Es una sola operacion con bandera, y no un <c>Desactivar</c> suelto, porque
    /// asi estaba antes: se podia dar de baja pero NO volver a dar de alta. Un
    /// cajero desactivado por error quedaba inservible para siempre —y como el alta
    /// rechaza nombres repetidos, tampoco se podia recrear con el mismo nombre—.
    /// </summary>
    Task EstablecerActivoAsync(string id, bool activo);

    // ------------------------------------------------------------------
    // Replicacion entre cajas (ver [Services.Pairing])
    // ------------------------------------------------------------------
    //
    // Estos dos metodos viven ACA, en la interfaz que ya es dueña del
    // almacenamiento, y no en un servicio aparte. Sacarlos afuera obligaria a
    // exponer la escritura del hash del PIN de administrador, y entonces cualquier
    // pantalla podria pisarlo.

    /// <summary>
    /// Arma el sobre con lo que esta caja tiene configurado. Devuelve <c>null</c>
    /// si la caja todavia no esta completa —sin PIN o sin cajeros—: repartir media
    /// configuracion deja a la receptora creyendose configurada y fallando al
    /// primer ingreso, sin forma de saber que lo que recibio venia incompleto.
    /// </summary>
    Task<CashierRosterEnvelope?> ExportarPadronAsync();

    /// <summary>
    /// Escribe el PIN y el padron recibidos de otra caja.
    ///
    /// El padron se escribe COMPLETO, no se mezcla: una caja que recibe
    /// configuracion se esta montando, y arrastrar cajeros de una instalacion
    /// anterior es como se cuelan usuarios que nadie dio de alta ahi.
    /// </summary>
    /// <returns><c>false</c> si no se pudo escribir; la caja queda como estaba.</returns>
    Task<bool> ImportarPadronAsync(CashierRosterEnvelope sobre);
}
