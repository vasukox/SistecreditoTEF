using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// Constantes de rutas de navegacion Shell.
///
/// DRY: cualquier referencia a "capturaCedula", "//otp", etc. usa
/// estas constantes. Antes estaban hardcodeadas en code-behinds y VMs.
///
/// Formato Shell URIs:
///   "//ruta"      - ruta absoluta (limpia el stack)
///   "ruta"        - ruta relativa (push)
///   ".."          - pop
/// </summary>
public static class AppRoutes
{
    // HU8-973: rutas RELATIVAS (push) para que se arme el stack y el botón
    // "atrás" haga pop en vez de cerrar la app. Home queda absoluta ("//")
    // porque es el reset del stack (al finalizar / volver al inicio).
    /// <summary>
    /// Raiz del Shell: la pantalla de marca que decide a que modulo entrar.
    ///
    /// El Shell navega a su raiz por si mismo antes de que nadie pueda decidir,
    /// asi que la raiz tiene que ser NEUTRA. Con HomePage de raiz se veia el menu
    /// un instante y despues la app saltaba al modulo real.
    /// </summary>
    public const string Splash            = "//splash";

    /// <summary>
    /// Menu de dos opciones. Ya NO es la raiz; queda alcanzable como ruta normal.
    /// </summary>
    public const string Menu              = "menu";

    /// <summary>
    /// Reset del stack. Apunta a la raiz real (la pantalla de marca), que vuelve a
    /// resolver el destino: es el comportamiento correcto al finalizar un flujo.
    /// </summary>
    public const string Home              = "//splash";
    public const string CapturaCedula     = "capturaCedula";
    public const string ValidacionCliente = "validacionCliente";
    public const string SeleccionCuotas   = "seleccionCuotas";
    public const string Otp               = "otp";
    public const string Confirmacion      = "confirmacion";
    public const string CreditosActivos   = "creditosActivos";
    public const string Pago              = "pago";
    public const string ReciboPago        = "reciboPago";

    // ------------------------------------------------------------------
    // Ingreso de cajeros (solo abonos)
    // ------------------------------------------------------------------
    // Los abonos se hacen abriendo el APK desde el icono, fuera de HioPos, asi que
    // nadie valido quien esta operando. Estas tres pantallas cubren eso:
    //   1. ConfigurarAdmin  - primera vez que se abre la app: crear el PIN.
    //   2. IngresoCajero    - antes de cada abono: quien sos y tu clave.
    //   3. AdminCajeros     - alta, baja y cambio de clave. Detras del PIN.
    //
    // La venta a credito NO pasa por aca: entra por HioPos, donde el cajero ya se
    // identifico en el POS.
    public const string ConfigurarAdmin   = "configurarAdmin";
    public const string IngresoCajero     = "ingresoCajero";
    public const string AdminCajeros      = "adminCajeros";

    /// <summary>
    /// EL SEGUNDO PASO DEL MONTAJE: como se configura esta caja.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ES UNA PANTALLA Y NO DOS BOTONES DENTRO DE OTRA
    /// ─────────────────────────────────────────────────────────────────────────
    /// Las dos formas de montar una caja —copiarla de otra o crearla desde cero—
    /// vivian en la MISMA pantalla que el formulario del PIN: el atajo arriba, el
    /// formulario abajo. El que llegaba con una caja ya montada en la tienda veia
    /// primero un campo de PIN, y lo normal era llenarlo; el PIN que creaba
    /// quedaba pisado dos minutos despues al copiar de la otra caja.
    ///
    /// Separado en una pantalla que no hace mas que preguntar, la eleccion ocurre
    /// ANTES de que haya nada que llenar.
    /// </summary>
    public const string ConfiguracionInicio = "configuracionInicio";

    /// <summary>Replicacion del padron de cajeros entre cajas de la misma tienda.</summary>
    public const string Replicacion       = "replicacion";

    /// <summary>
    /// Que tienda es esta caja. Se elige al instalar y decide a nombre de quien
    /// quedan los creditos en Sistecredito. Ver [ResolucionDeTienda].
    /// </summary>
    public const string Tienda            = "tienda";

    /// <summary>
    /// Claves de parametros de QueryProperty.
    /// </summary>
    public static class Params
    {
        public const string Payment      = "payment";
        public const string Client       = "client";

        /// <summary>
        /// Abre [Replicacion] en modo ACTUALIZAR: solo el padron de cajeros, sin
        /// tocar el PIN de administrador. Ver [INavigationService.GoToActualizarCajerosAsync].
        /// </summary>
        public const string SoloCajeros  = "soloCajeros";

        /// <summary>
        /// Abre [Tienda] como PRIMER PASO del montaje, no como pantalla de
        /// ajustes. Cambia dos cosas: al elegir se pide confirmar, y al confirmar
        /// se sigue hacia adelante en vez de quedarse.
        ///
        /// Viaja como parametro y no se deduce del estado de la caja ("todavia no
        /// hay tienda") porque ese estado tambien lo tiene una caja ya montada a
        /// la que alguien le cambio la tienda: esa no esta instalandose, y
        /// mandarla al asistente la sacaria de administracion sin motivo.
        /// </summary>
        public const string Asistente    = "asistente";
    }
}
