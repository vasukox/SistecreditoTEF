using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.Services.Tiendas;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// QUE TIENDA ES ESTA CAJA.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTA PANTALLA EXISTE
/// ─────────────────────────────────────────────────────────────────────────────
/// El StoreId decide a nombre de que tienda queda cada credito en Sistecredito.
/// El canal previsto para entregarlo es <c>STORE_ID</c> por CloudLicense, y sigue
/// mandando — pero mientras ICG no provisione una terminal, esa caja no puede
/// operar. Aca el instalador la deja andando en el acto.
///
/// Se ELIGE DE UNA LISTA, nunca se teclea. Un ObjectId de 24 caracteres escrito a
/// mano es una errata esperando a pasar, y la errata se paga como creditos
/// atribuidos a otra tienda — que es exactamente el problema que se vivio.
/// </summary>
/// <remarks>
/// <para>
/// ESTA PANTALLA TIENE DOS VIDAS, Y SON DISTINTAS.
/// </para>
/// <para>
/// <b>Ajustes</b> (se llega desde administracion, detras del PIN): se toca una
/// tienda y queda aplicada en el acto. Quien llega aqui ya sabe lo que viene a
/// hacer y la pantalla no le pone pasos.
/// </para>
/// <para>
/// <b>Asistente</b> (primer paso del montaje de una caja): se toca una tienda, se
/// CONFIRMA, y recien entonces se aplica y se sigue al paso 2. El paso de
/// confirmacion existe porque en el montaje no hay vuelta atras facil —la
/// correccion vive detras de un PIN que todavia no existe— y porque dos de las 76
/// tiendas se llaman casi igual. Un toque de mas aqui cuesta un segundo; una
/// tienda equivocada cuesta creditos registrados a nombre de otra.
/// </para>
/// <para>El modo llega por parametro de ruta: ver [AppRoutes.Params.Asistente].</para>
/// </remarks>
public partial class TiendaViewModel(
    ITiendaDeLaCaja tiendaDeLaCaja,
    ApiConfigProvider configuracion,
    INavigationService? nav = null,
    EstadoDeLaConexion? salud = null,
    VerificacionDeTienda? verificacion = null) : ObservableObject
{
    // ------------------------------------------------------------------
    // COMPROBAR LA TIENDA CONTRA SISTECREDITO
    // ------------------------------------------------------------------
    // El semaforo de la cabecera es pasivo: se entera del estado de la tienda a
    // partir de las operaciones reales. Eso deja sin cubrir el momento en que mas
    // falta hace —el montaje—, cuando todavia no se vendio nada. Esto lo
    // convierte en una pregunta explicita. Ver [VerificacionDeTienda].

    [ObservableProperty]
    private bool verificando;

    [ObservableProperty]
    private string mensajeDeVerificacion = string.Empty;

    /// <summary>true solo cuando Sistecredito reconocio la tienda.</summary>
    [ObservableProperty]
    private bool verificada;

    public bool HayMensajeDeVerificacion => MensajeDeVerificacion.Length > 0;

    partial void OnMensajeDeVerificacionChanged(string value) =>
        OnPropertyChanged(nameof(HayMensajeDeVerificacion));

    /// <summary>
    /// Le pregunta a Sistecredito si la tienda de esta caja existe.
    ///
    /// No escribe nada: es una consulta de simulacion, sin datos de cliente. Se
    /// puede tocar las veces que haga falta.
    /// </summary>
    [RelayCommand]
    private async Task VerificarAsync()
    {
        if (verificacion is null || Verificando) return;

        Verificando = true;
        Verificada = false;
        MensajeDeVerificacion = "Preguntándole a Sistecrédito…";
        try
        {
            var r = await verificacion.VerificarAsync();

            (MensajeDeVerificacion, Verificada) = r switch
            {
                ResultadoDeVerificacion.Aceptada =>
                    ("Sistecrédito reconoce esta tienda. La caja puede operar.", true),

                ResultadoDeVerificacion.Rechazada =>
                    ("Sistecrédito NO reconoce esta tienda. Esta caja no va a poder " +
                     "vender ni recaudar: revise que sea la tienda correcta y, si lo es, " +
                     "avise al área de sistemas.", false),

                ResultadoDeVerificacion.NoAplicaEnPruebas =>
                    ("En el ambiente de pruebas la tienda no se envía a Sistecrédito, " +
                     "así que aquí no hay nada que comprobar.", false),

                _ => ("No se pudo hablar con Sistecrédito. Revise la conexión e intente " +
                      "de nuevo; esto no dice nada sobre la tienda.", false)
            };

            AppLogger.I("TiendaViewModel", $"Verificacion de la tienda: {r}.");
        }
        catch (Exception ex)
        {
            AppLogger.E("TiendaViewModel", "Fallo la verificacion de la tienda.", ex);
            MensajeDeVerificacion = "No se pudo verificar la tienda.";
            Verificada = false;
        }
        finally
        {
            Verificando = false;
        }
    }

    // ------------------------------------------------------------------
    // En que vida esta la pantalla
    // ------------------------------------------------------------------

    /// <summary>
    /// true cuando se entro como primer paso del montaje. Ver el bloque de arriba.
    /// </summary>
    [ObservableProperty]
    private bool modoAsistente;

    /// <summary>
    /// Tienda tocada y todavia NO aplicada. Solo existe en el asistente: en
    /// ajustes el toque aplica directo.
    /// </summary>
    [ObservableProperty]
    private TiendaDelCatalogo? seleccionada;

    /// <summary>Ya se confirmo y la caja quedo operando como esa tienda.</summary>
    [ObservableProperty]
    private bool confirmada;

    /// <summary>La lista y el buscador: el estado normal.</summary>
    public bool MostrandoLista => !ModoAsistente || (Seleccionada is null && !Confirmada);

    /// <summary>"¿Esta caja opera en 037?" — el paso de confirmacion.</summary>
    public bool MostrandoConfirmacion => ModoAsistente && Seleccionada is not null && !Confirmada;

    /// <summary>"Listo, esta caja opera como 037" y el boton de seguir.</summary>
    public bool MostrandoListo => ModoAsistente && Confirmada;

    /// <summary>Titulo de la cabecera, distinto en cada vida.</summary>
    public string TituloDeLaPantalla =>
        ModoAsistente ? "¿En qué tienda está esta caja?" : "Tienda de esta caja";

    /// <summary>Solo en ajustes se puede volver: en el paso 1 no hay nada detras.</summary>
    public bool SePuedeVolver => !ModoAsistente;

    partial void OnModoAsistenteChanged(bool value) => AvisarDeLosEstados();
    partial void OnSeleccionadaChanged(TiendaDelCatalogo? value) => AvisarDeLosEstados();
    partial void OnConfirmadaChanged(bool value) => AvisarDeLosEstados();

    private void AvisarDeLosEstados()
    {
        OnPropertyChanged(nameof(MostrandoLista));
        OnPropertyChanged(nameof(MostrandoConfirmacion));
        OnPropertyChanged(nameof(MostrandoListo));
        OnPropertyChanged(nameof(TituloDeLaPantalla));
        OnPropertyChanged(nameof(SePuedeVolver));
    }

    // ------------------------------------------------------------------
    // Estado actual
    // ------------------------------------------------------------------

    [ObservableProperty]
    private string titulo = string.Empty;

    [ObservableProperty]
    private string detalle = string.Empty;

    /// <summary>
    /// Lo que dice HioPosCloud cuando NO coincide con la tienda elegida aca, o
    /// vacio. Es informativo: manda la elegida en el terminal. Ver [HayAvisoDeHioPos].
    /// </summary>
    [ObservableProperty]
    private string avisoDeHioPos = string.Empty;

    [ObservableProperty]
    private string mensajeOk = string.Empty;

    public bool HayAvisoDeHioPos => AvisoDeHioPos.Length > 0;
    public bool TieneMensajeOk => MensajeOk.Length > 0;

    partial void OnAvisoDeHioPosChanged(string value) => OnPropertyChanged(nameof(HayAvisoDeHioPos));
    partial void OnMensajeOkChanged(string value) => OnPropertyChanged(nameof(TieneMensajeOk));

    // ------------------------------------------------------------------
    // La lista
    // ------------------------------------------------------------------

    public ObservableCollection<TiendaDelCatalogo> Tiendas { get; } = [];

    [ObservableProperty]
    private string busqueda = string.Empty;

    partial void OnBusquedaChanged(string value) => Filtrar();

    /// <summary>
    /// Se llama al abrir la pantalla. Deja la lista completa —no vacia— y describe
    /// con que tienda esta operando la caja ahora mismo.
    /// </summary>
    public void Cargar()
    {
        Filtrar();
        Refrescar();
    }

    private void Filtrar()
    {
        Tiendas.Clear();
        foreach (var t in CatalogoDeTiendas.Buscar(Busqueda))
            Tiendas.Add(t);
    }

    private void Refrescar()
    {
        ResolucionDeTienda resolucion;
        try
        {
            resolucion = configuracion.Current.Tienda;
        }
        catch (Exception ex)
        {
            // Describir la tienda no puede impedir elegirla: si la configuracion no
            // se puede leer, la lista sigue estando y el instalador puede corregir.
            AppLogger.W("TiendaViewModel", $"No se pudo leer la configuracion: {ex.Message}");
            Titulo = "No se pudo leer la configuración";
            Detalle = "Elija la tienda de la lista para dejarla configurada.";
            AvisoDeHioPos = string.Empty;
            return;
        }

        // La discrepancia con el POS se INFORMA, no frena: manda la tienda elegida
        // aca. Se muestra igual porque significa que ICG cree otra cosa, y eso es
        // algo que alguien va a querer saber antes de que aparezca en una
        // conciliacion.
        AvisoDeHioPos = resolucion.DiscrepaConHioPos
            ? $"HioPosCloud dice que este terminal es " +
              $"{CatalogoDeTiendas.Describir(resolucion.DeHioPos)}.\n\n" +
              "Se está usando la tienda elegida aquí, que es la que manda. " +
              "Si la correcta fuera la de HioPosCloud, elíjala de la lista."
            : string.Empty;

        (Titulo, Detalle) = resolucion.Estado == EstadoDeLaTienda.SinElegir
            ? ("Falta elegir la tienda",
               "Sin tienda no se pueden registrar créditos. Búsquela abajo por código o por nombre.")
            : (CatalogoDeTiendas.Describir(resolucion.StoreId),
               $"StoreId: {resolucion.StoreId}");
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------

    /// <summary>
    /// Deja esta caja operando como la tienda elegida.
    ///
    /// Despues de fijarla se RECARGA la configuracion: sin eso, el modulo seguiria
    /// usando la anterior hasta el proximo INITIALIZE de HioPos, y el instalador
    /// veria en pantalla una tienda distinta de la que se esta usando de verdad
    /// — que es la clase de desfase que produjo todo este problema.
    /// </summary>
    [RelayCommand]
    private void Elegir(TiendaDelCatalogo? tienda)
    {
        if (tienda is null) return;

        // En el asistente, tocar NO aplica: deja la tienda a la espera de que se
        // confirme. Ver el bloque de las dos vidas, arriba.
        if (ModoAsistente)
        {
            MensajeOk = string.Empty;
            Seleccionada = tienda;
            return;
        }

        Aplicar(tienda);
    }

    /// <summary>
    /// Deja la caja operando como la tienda, de verdad. Es el unico lugar que
    /// escribe: el toque en ajustes y la confirmacion en el asistente terminan
    /// los dos aqui, asi que no hay dos caminos que puedan divergir.
    /// </summary>
    private void Aplicar(TiendaDelCatalogo tienda)
    {
        try
        {
            tiendaDeLaCaja.Fijar(tienda);
            configuracion.Reload();

            // El semaforo vuelve a "sin verificar". Un rechazo de la tienda
            // ANTERIOR no dice nada de esta, y arrastrarlo dejaria la caja
            // bloqueada justo despues de haber corregido el problema. La proxima
            // llamada a Credinet lo pone en verde o en rojo con la nueva.
            salud?.Olvidar();

            MensajeOk = $"Esta caja queda como {tienda.Etiqueta}.";
            Busqueda = string.Empty;
            Refrescar();
        }
        catch (Exception ex)
        {
            AppLogger.E("TiendaViewModel", "No se pudo fijar la tienda de la caja.", ex);
            MensajeOk = string.Empty;
            Titulo = "No se pudo guardar la tienda";
            Detalle = "Intente de nuevo; si persiste, avise al área de sistemas.";
        }
    }

    // ------------------------------------------------------------------
    // Solo en el asistente
    // ------------------------------------------------------------------

    /// <summary>
    /// Confirma la tienda tocada y la aplica. A partir de aqui la caja ya opera
    /// como esa tienda, aunque el instalador no termine el paso 2.
    /// </summary>
    [RelayCommand]
    private async Task AceptarAsync()
    {
        if (Seleccionada is not { } tienda) return;

        Aplicar(tienda);

        // Solo se da por confirmada si de verdad quedo guardada. Si [Aplicar]
        // fallo, se devuelve a la lista —que es donde [Titulo] y [Detalle] cuentan
        // que paso— en vez de mostrar un "listo" sobre una caja sin tienda.
        if (tiendaDeLaCaja.StoreId != tienda.StoreId)
        {
            Seleccionada = null;
            return;
        }

        Confirmada = true;

        // Y SE COMPRUEBA EN EL ACTO, sin que nadie tenga que acordarse de tocar un
        // boton. Este es el unico momento del montaje en que alguien esta mirando
        // la pantalla con la tienda recien elegida: si el identificador no sirve,
        // se entera aqui y no con la primera venta.
        await VerificarAsync();
    }

    /// <summary>Vuelve a la lista. Tambien deshace el "listo" ya confirmado.</summary>
    [RelayCommand]
    private void ElegirOtra()
    {
        Seleccionada = null;
        Confirmada = false;
        MensajeOk = string.Empty;

        // Lo que se comprobo era de la tienda anterior: no puede quedar en
        // pantalla mientras se elige otra.
        MensajeDeVerificacion = string.Empty;
        Verificada = false;
    }

    /// <summary>
    /// Paso 2: como se configura esta caja.
    ///
    /// Si no hay navegacion —el ViewModel se puede construir sin ella en pruebas—
    /// no hace nada en vez de lanzar: la tienda ya quedo guardada, que es lo que
    /// importa.
    /// </summary>
    [RelayCommand]
    private async Task SiguienteAsync()
    {
        if (nav is null) return;

        try
        {
            await nav.GoToConfiguracionInicioAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("TiendaViewModel", "No se pudo abrir el paso siguiente.", ex);
            Titulo = "No se pudo continuar";
            Detalle = "La tienda quedó guardada. Cierre y vuelva a abrir la aplicación.";
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // NO HAY "QUITAR LA TIENDA", Y ES A PROPOSITO
    // ──────────────────────────────────────────────────────────────────────
    // Existio mientras la tienda podia venir de HioPosCloud: quitar la local era la
    // salida cuando la buena era la del POS. Ahora la tienda se elige siempre a
    // mano, asi que quitarla solo dejaria la caja sin poder cobrar — un boton facil
    // de tocar por error cuyo unico efecto es romper el terminal.
    //
    // Para cambiar de tienda se elige otra de la lista. No hace falta pasar por
    // "ninguna". [ITiendaDeLaCaja.Olvidar] sigue existiendo para las pruebas.
}
