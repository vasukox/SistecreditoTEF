using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.Services.Tiendas;

namespace SistecreditoTEF.Maui.ViewModels;

/// <summary>
/// Replicacion del padron entre cajas de una misma tienda, desde la pantalla de
/// administracion de cajeros (detras del PIN).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// QUE PROBLEMA RESUELVE, Y CUAL NO
/// ─────────────────────────────────────────────────────────────────────────────
/// El despliegue son ~512 tiendas con ~3 cajas cada una. La credencial de Credinet,
/// la URL y el ambiente NO se tocan aca: bajan solas de CloudLicense. Lo que hoy se
/// teclea caja por caja es el PIN de administrador y el alta de cada cajero con su
/// clave, y eso es lo que esto copia.
///
/// Elimina las cajas 2 y 3 de cada tienda, NO la primera: tiene que haber una caja
/// configurada de la cual copiar. Es una mejora de 3x, no de infinito. Conviene
/// decirlo antes de que alguien lo venda de otra forma.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// UNA PANTALLA, DOS OFICIOS
/// ─────────────────────────────────────────────────────────────────────────────
/// Aca vienen dos personas distintas: la que monta una caja nueva (RECIBE) y la que
/// esta parada frente a la caja que ya funciona (COMPARTE). Antes se les mostraba
/// todo junto, una debajo de la otra, y el que instalaba tenia que leer dos
/// formularios completos para darse cuenta de cual le tocaba.
///
/// Ahora se elige el oficio primero —[ModoCompartir]— y solo se ve la mitad que
/// corresponde. Arranca en RECIBIR porque es el caso que llega desde la pantalla de
/// primer uso, que es por donde entra casi todo el mundo.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL SERVICIO MUERE CON LA PANTALLA
/// ─────────────────────────────────────────────────────────────────────────────
/// Se enciende en <see cref="AbrirVentanaAsync"/> y se apaga en
/// <see cref="DetenerAsync"/>, que la Page llama en <c>OnDisappearing</c>. Este
/// ViewModel se registra TRANSIENT como todos los demas.
///
/// Si esto se hiciera singleton, el socket sobreviviria a salir de la pantalla y
/// quedaria una caja ofreciendo el padron de la tienda en la red todo el dia, casi
/// siempre sin nadie mirando.
/// </summary>
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTE ViewModel NO ES IAsyncDisposable
/// ─────────────────────────────────────────────────────────────────────────────
/// Lo era, y tumbaba la app en produccion. El contenedor de dependencias de MAUI
/// libera los servicios transient de forma SINCRONA, y al encontrarse uno que solo
/// implementa IAsyncDisposable lanza:
///
///   [System.InvalidOperationException]: AsyncDisposableServiceDispose,
///   SistecreditoTEF.Maui.ViewModels.ReplicacionViewModel
///
/// Capturado en la caja 3 de la primera tienda: el padron se copiaba bien y la app
/// se caia 11 segundos despues, al salir de la pantalla. El fallo llega DESPUES de
/// guardar, que es lo unico que evito que quedara media configuracion escrita.
///
/// El apagado del socket NO se pierde: lo hace [ReplicacionPage.OnDisappearing],
/// que es donde tiene que estar —el servicio muere con la pantalla, no con el
/// contenedor—. Implementar ademas IDisposable para "arreglarlo" seria peor:
/// obligaria a resolver el cierre bloqueando un hilo, que es exactamente el
/// sync-over-async que este diseño evita.
public partial class ReplicacionViewModel(
    IAuthStore store,
    ApiConfig config,
    ISesionCajero sesion,
    INavigationService nav,
    ITiendaDeLaCaja tiendaDeLaCaja,
    ApiConfigProvider configuracion) : ObservableObject
{
    // ══════════════════════════════════════════════════════════════════════════
    // DOS OPERACIONES EN LA MISMA PANTALLA
    // ══════════════════════════════════════════════════════════════════════════
    //
    // COPIAR (default)   caja recien instalada: trae el PIN de administrador y el
    //                    padron. Es lo que ya funcionaba y no se toco.
    //
    // ACTUALIZAR         caja que ya opera: trae SOLO el padron de cajeros. Se abre
    //                    desde administracion, o sea detras del PIN.
    //
    // La caja que REPARTE no distingue las dos: manda el mismo sobre de siempre. Eso
    // es a proposito — las cajas que ya tienen el APK instalado siguen sirviendo como
    // emisoras sin actualizarlas—. Lo que cambia es que se escribe de este lado.

    /// <summary>
    /// Modo ACTUALIZAR: solo el padron, sin tocar el PIN de administrador.
    /// Lo fija la ruta (ver [AppRoutes.Params.SoloCajeros]).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    [NotifyPropertyChangedFor(nameof(Explicacion))]
    [NotifyPropertyChangedFor(nameof(TextoModoRecibir))]
    [NotifyPropertyChangedFor(nameof(TextoModoCompartir))]
    [NotifyPropertyChangedFor(nameof(TextoDelBotonTraer))]
    [NotifyPropertyChangedFor(nameof(TextoDelBotonAceptar))]
    [NotifyPropertyChangedFor(nameof(TituloDeCompartir))]
    [NotifyPropertyChangedFor(nameof(ExplicacionDelCodigo))]
    [NotifyPropertyChangedFor(nameof(PistaParaLaOtraCaja))]
    private bool soloCajeros;

    public string Titulo => SoloCajeros
        ? "Actualizar cajeros"
        : "Copiar configuración de otra caja";

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────
    /// LA ACTUALIZACION VA EN LOS DOS SENTIDOS
    /// ─────────────────────────────────────────────────────────────────────────
    /// La primera version daba por hecho que quien abria esta pantalla venia a
    /// RECIBIR. Pero el padron al dia lo puede tener cualquiera de las tres cajas:
    /// el que dio de alta al cajero nuevo esta parado frente a la suya, y desde ahi
    /// lo normal es querer pasarlo, no traerlo.
    ///
    /// Obligarlo a recibir lo manda a caminar hasta la otra caja para generar un
    /// codigo —o peor, a traerse el padron VIEJO encima del suyo, que es
    /// exactamente la baja silenciosa que el diferencial vino a evitar—.
    ///
    /// Asi que el sentido se elige, y se elige primero.
    /// </summary>
    public string Explicacion => (SoloCajeros, ModoCompartir) switch
    {
        (true, false) =>
            "Trae el padrón de cajeros de otra caja de esta misma tienda. No cambia " +
            "nada más de esta caja.",

        (true, true) =>
            "Esta caja le pasa su padrón de cajeros a otra. Acá no cambia nada: los " +
            "cambios los aplica la otra caja, y ahí se ve antes qué cambia.",

        _ =>
            "Se copia el PIN de administrador y el padrón de cajeros. Las credenciales " +
            "de Sistecrédito no: bajan solas desde HioPosCloud.",
    };

    public string TextoModoRecibir => SoloCajeros ? "Traer cajeros" : "Traer a esta caja";

    public string TextoModoCompartir => SoloCajeros ? "Enviar mis cajeros" : "Compartir desde aquí";

    public string TextoDelBotonTraer => SoloCajeros ? "Ver qué cambia" : "Traer configuración";

    public string TextoDelBotonAceptar => SoloCajeros ? "Aplicar cambios" : "Aceptar y guardar";

    public string TituloDeCompartir => SoloCajeros ? "ENVIAR MIS CAJEROS" : "COMPARTIR A OTRA CAJA";

    public string ExplicacionDelCodigo => SoloCajeros
        ? "Se genera un código de 6 dígitos que dura 10 minutos. Sirve para todas las " +
          "cajas que alcances a actualizar en ese rato."
        : "Se genera un código de 6 dígitos que dura 10 minutos. Sirve para todas las " +
          "cajas que alcances a configurar dentro de ese rato.";

    /// <summary>Que tiene que hacer el de la otra caja. Cambia con el sentido.</summary>
    public string PistaParaLaOtraCaja => SoloCajeros
        ? "En la otra caja: Administración → Actualizar cajeros → «Traer cajeros», " +
          "escanear la red y escribir el código."
        : "En la otra caja: abre esta misma pantalla, deja «Traer a esta caja», " +
          "escanea la red y escribe el código.";

    /// <summary>
    /// Traer tambien el PIN de administrador al actualizar. APAGADO por defecto.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE NO VA PRENDIDO
    /// ─────────────────────────────────────────────────────────────────────────
    /// El boton dice "actualizar cajeros" y tiene que hacer solo eso. El PIN de
    /// administrador es lo unico que separa a un cajero de poder administrar la
    /// caja: si se copiara en cada actualizacion, un PIN cambiado por error en UNA
    /// caja se reparte a toda la tienda sin que nadie lo pida, y el que lo cambio
    /// bien se queda afuera de las otras dos.
    ///
    /// Pero tampoco se deja sin salida: cuando de verdad hay que propagar un PIN
    /// nuevo, esta casilla lo hace. Explicita, del operador, y visible.
    /// </summary>
    [ObservableProperty]
    private bool incluirPinAdmin;

    /// <summary>Bajo este tiempo restante la ventana se muestra en ambar.</summary>
    private static readonly TimeSpan Apuro = TimeSpan.FromMinutes(3);

    /// <summary>Bajo este tiempo restante se muestra en rojo y el codigo late.</summary>
    private static readonly TimeSpan Agonia = TimeSpan.FromMinutes(1);

    private PairingHost? _host;
    private PairingDiscovery? _discovery;
    private CashierRosterEnvelope? _recibido;

    // ══════════════════════════════════════════════════════════════════════════
    // QUE MITAD SE VE
    // ══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool modoCompartir;

    public bool ModoRecibir => !ModoCompartir;

    partial void OnModoCompartirChanged(bool value)
    {
        OnPropertyChanged(nameof(ModoRecibir));

        // La explicacion depende del sentido: al enviar hay que decir que ACA no
        // cambia nada, o el operador teme estar pisandose su propio padron.
        OnPropertyChanged(nameof(Explicacion));
    }

    [RelayCommand]
    private void VerRecibir() => ModoCompartir = false;

    [RelayCommand]
    private void VerCompartir() => ModoCompartir = true;

    // ══════════════════════════════════════════════════════════════════════════
    // EMISOR: la caja que ya esta configurada
    // ══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string? codigo;

    [ObservableProperty]
    private string? direccionPropia;

    [ObservableProperty]
    private string? estadoEmisor;

    /// <summary>
    /// Distingue un aviso que hay que atender de un dato informativo. La pantalla
    /// los pinta distinto: no es lo mismo "esta caja no esta completa" que "se
    /// cerro la ventana".
    /// </summary>
    [ObservableProperty]
    private bool emisorEnFalla;

    /// <summary>Hay un codigo vivo y el socket esta escuchando.</summary>
    [ObservableProperty]
    private bool ventanaAbierta;

    /// <summary>
    /// Cuanto queda de la ventana, de 1 a 0. Es lo que dibuja la barra que se
    /// vacia: el operador ve el tiempo irse sin tener que leer un numero.
    /// </summary>
    [ObservableProperty]
    private double fraccionRestante;

    /// <summary>"9:47". Se actualiza cinco veces por segundo, pero solo cambia
    /// —y por lo tanto solo redibuja— una vez por segundo.</summary>
    [ObservableProperty]
    private string tiempoRestante = "0:00";

    /// <summary>Menos de 3 minutos: la barra pasa a ambar.</summary>
    [ObservableProperty]
    private bool apurando;

    /// <summary>Menos de 1 minuto: la barra se pone roja y el codigo late.</summary>
    [ObservableProperty]
    private bool porVencer;

    /// <summary>"2 cajas ya copiaron" — la unica confirmacion que tiene el que
    /// reparte de que del otro lado paso algo.</summary>
    [ObservableProperty]
    private string cajasCopiadas = string.Empty;

    /// <summary>Intentos que quedan antes de que el codigo se queme.</summary>
    [ObservableProperty]
    private int intentosRestantes = PairingSecret.MaxAttempts;

    public bool TieneCodigo => !string.IsNullOrEmpty(Codigo);

    /// <summary>
    /// El codigo partido en digitos, para mostrarlo en casillas separadas.
    ///
    /// Se dicta en voz alta de una caja a otra, a veces de un piso a otro. Seis
    /// cifras pegadas se leen mal y se repiten peor; separadas, se cantan de a una.
    /// </summary>
    public IReadOnlyList<string> CodigoDigitos =>
        (Codigo ?? string.Empty).Select(c => c.ToString()).ToList();

    partial void OnCodigoChanged(string? value)
    {
        OnPropertyChanged(nameof(TieneCodigo));
        OnPropertyChanged(nameof(CodigoDigitos));
    }

    /// <summary>
    /// Abre la ventana de 10 minutos y muestra el codigo.
    ///
    /// Solo si la caja esta COMPLETA: con PIN y al menos un cajero. Repartir media
    /// configuracion deja a la receptora creyendose configurada y fallando al
    /// primer ingreso.
    /// </summary>
    [RelayCommand]
    private async Task AbrirVentanaAsync()
    {
        EstadoEmisor = null;
        EmisorEnFalla = false;

        try
        {
            var sobre = await store.ExportarPadronAsync();
            if (sobre is null)
            {
                EmisorEnFalla = true;
                EstadoEmisor =
                    "Esta caja todavia no esta completa: necesita el PIN de administrador " +
                    "y al menos un cajero para poder compartir.";
                return;
            }

            await DetenerAsync();

            // La fabrica es asincrona y se vuelve a llamar en CADA entrega: entre la
            // primera caja y la tercera el administrador pudo dar de alta un cajero,
            // y la tercera tiene que recibir el padron de verdad.
            // El StoreId se anuncia en el SALUDO, no en el sobre: sirve para que la
            // caja receptora rechace un padron de otra tienda, y no se guarda de ese
            // lado. Ver [PairingGreeting].
            //
            // Va la tienda ELEGIDA ([ApiConfig.Tienda]) y no [ApiConfig.StoreId]: ese
            // ultimo es el que viaja a Credinet, y en sandbox es null a proposito
            // (ver [ApiConfig.StoreId]). Con el, la verificacion entre cajas se caia
            // a comparar por NOMBRE en todo el ambiente de pruebas — justo la barrera
            // debil que el comentario de [EsLaMismaTienda] advierte que no sirve.
            //
            // El sobre ADEMAS lleva la tienda, para que una caja nueva quede asociada
            // sola. Se sella aca y no en [IAuthStore]: el padron es de la base de
            // cajeros y la tienda es de la configuracion; mezclarlas obligaria al
            // store a conocer el catalogo. La caja receptora solo la aplica cuando se
            // esta MONTANDO, nunca al actualizar cajeros. Ver [CashierRosterEnvelope].
            _host = new PairingHost(
                async _ =>
                {
                    var padron = await store.ExportarPadronAsync();
                    return padron is null
                        ? null
                        : padron with { StoreId = config.Tienda.StoreId };
                },
                config.StoreName,
                storeId: config.Tienda.StoreId);
            _host.Start();

            _discovery = new PairingDiscovery();
            _discovery.StartResponding(config.StoreName, _host.Port);

            Codigo = _host.Code;
            DireccionPropia = PairingDiscovery.LocalAddress();

            // Se deja la barra llena ANTES de que corra el primer tick: si empezara
            // en cero, la tarjeta aparece con la ventana aparentemente vencida y se
            // llena un instante despues. Se ve como un error.
            FraccionRestante = 1;
            VentanaAbierta = true;

            ActualizarEstadoEmisor();

            AppLogger.I("ReplicacionViewModel",
                $"Ventana de replicacion abierta en el puerto {_host.Port} " +
                $"({sobre.Cajeros.Count} cajeros por compartir).");
        }
        catch (Exception ex)
        {
            AppLogger.E("ReplicacionViewModel", "No se pudo abrir la ventana de replicacion.", ex);
            EmisorEnFalla = true;
            EstadoEmisor = "No se pudo abrir la ventana. Intenta de nuevo.";
            await DetenerAsync();
        }
    }

    /// <summary>
    /// Cierra la ventana a mano, sin esperar los 10 minutos.
    ///
    /// Hace falta: terminada la ultima caja, el que instala se va y el codigo queda
    /// vivo. El cierre por OnDisappearing ya lo cubre, pero solo si alguien sale de
    /// la pantalla; esto le da un boton explicito y —sobre todo— una forma de
    /// generar uno nuevo sin salir y volver a entrar.
    /// </summary>
    [RelayCommand]
    private async Task CerrarVentanaAsync()
    {
        await DetenerAsync();
        EmisorEnFalla = false;
        EstadoEmisor = "Ventana cerrada. Esta caja ya no esta compartiendo nada.";
    }

    /// <summary>
    /// Refresca el estado del emisor. Lo llama la Page con un temporizador, varias
    /// veces por segundo: el operador tiene que ver cuanto le queda y cuantas cajas
    /// copiaron, sin leer un texto.
    ///
    /// Las propiedades observables no notifican si el valor no cambio, asi que
    /// llamarla cinco veces por segundo no redibuja cinco veces: solo mueve la
    /// barra, que es lo unico que cambia de verdad en cada paso.
    /// </summary>
    public void ActualizarEstadoEmisor()
    {
        if (_host is null)
        {
            PintarSinVentana();
            return;
        }

        if (!_host.IsOpen)
        {
            PintarVentanaVencida(_host.AttemptsRemaining <= 0);
            return;
        }

        PintarVentanaViva(_host.TimeLeft, _host.SuccessfulTransfers, _host.AttemptsRemaining);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LOS TRES "PINTAR" SON PUBLICOS PARA PODER PROBARLOS
    // ──────────────────────────────────────────────────────────────────────────
    // La cuenta regresiva tiene reglas que vale la pena fijar: donde pasa a ambar,
    // donde a rojo, como se escribe un reloj de un digito de minuto, que dice el
    // contador cuando todavia no copio nadie.
    //
    // Probar eso a traves de [ActualizarEstadoEmisor] obligaria a abrir la ventana
    // de verdad, y abrirla levanta un TcpListener en el puerto fijo 47114 mas el
    // socket UDP del descubrimiento. Una prueba que ocupa puertos fijos falla sola
    // en cuanto dos corren a la vez, que es exactamente lo que hace el runner.
    //
    // Separando el calculo del socket, las reglas se prueban con tres numeros.

    /// <summary>No hay ventana: ni abierta ni recien cerrada.</summary>
    public void PintarSinVentana()
    {
        VentanaAbierta = false;
        Apurando = PorVencer = false;
        FraccionRestante = 0;
        TiempoRestante = "0:00";
        CajasCopiadas = string.Empty;
    }

    /// <param name="quemado">
    /// <c>true</c> si se acabo por intentos fallidos y no por tiempo. Son dos
    /// cosas distintas para el que esta mirando: una es "genera otro", la otra es
    /// "alguien estuvo tecleando codigos que no eran".
    /// </param>
    public void PintarVentanaVencida(bool quemado)
    {
        VentanaAbierta = false;
        Apurando = PorVencer = false;
        FraccionRestante = 0;
        TiempoRestante = "0:00";
        Codigo = null;

        EmisorEnFalla = quemado;
        EstadoEmisor = quemado
            ? "El codigo se quemo por intentos fallidos. Genera uno nuevo."
            : "La ventana se cerro. Genera un codigo nuevo.";
    }

    public void PintarVentanaViva(TimeSpan restante, int cajas, int intentos)
    {
        VentanaAbierta = true;
        FraccionRestante = Math.Clamp(
            restante.TotalSeconds / PairingProtocol.Window.TotalSeconds, 0, 1);

        TiempoRestante =
            $"{restante.Minutes}:{restante.Seconds.ToString("D2", CultureInfo.InvariantCulture)}";

        Apurando = restante <= Apuro;
        PorVencer = restante <= Agonia;
        IntentosRestantes = intentos;

        CajasCopiadas = cajas == 0
            ? "Todavia no copio ninguna caja"
            : $"{cajas} caja{(cajas == 1 ? "" : "s")} ya copi{(cajas == 1 ? "o" : "aron")}";

        // Con la barra, el reloj y el contador a la vista, el renglon de texto
        // sobra: repetiria lo que ya se ve.
        EstadoEmisor = null;
        EmisorEnFalla = false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RECEPTOR: la caja nueva
    // ══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotivoTraer))]
    [NotifyPropertyChangedFor(nameof(PuedeTraer))]
    [NotifyCanExecuteChangedFor(nameof(TraerCommand))]
    private string direccionOrigen = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotivoTraer))]
    [NotifyPropertyChangedFor(nameof(PuedeTraer))]
    [NotifyCanExecuteChangedFor(nameof(TraerCommand))]
    private string codigoIngresado = string.Empty;

    [ObservableProperty]
    private string? estadoReceptor;

    /// <summary>Igual que en el emisor: separa el aviso del dato.</summary>
    [ObservableProperty]
    private bool receptorEnFalla;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotivoTraer))]
    [NotifyPropertyChangedFor(nameof(PuedeTraer))]
    [NotifyCanExecuteChangedFor(nameof(TraerCommand))]
    private bool copiando;

    /// <summary>Datos del sobre traido, para que el operador confirme ANTES de guardar.</summary>
    [ObservableProperty]
    private string? resumenRecibido;

    /// <summary>Tienda de la que viene el sobre, para el encabezado de la confirmacion.</summary>
    [ObservableProperty]
    private string? tiendaRecibida;

    public bool HayAlgoPorConfirmar => _recibido is not null;

    /// <summary>Cajas repartiendo que respondieron en la red.</summary>
    public ObservableCollection<CajaEncontrada> CajasEncontradas { get; } = [];

    public bool HayCajasEncontradas => CajasEncontradas.Count > 0;

    [ObservableProperty]
    private bool escaneando;

    /// <summary>
    /// El campo para teclear la direccion a mano arranca plegado.
    ///
    /// Es la salida cuando la difusion esta bloqueada, no el camino normal, y
    /// mostrarlo siempre convertia el paso 1 en dos formularios: el instalador
    /// escribia una IP a mano teniendo el boton de escanear al lado.
    /// </summary>
    [ObservableProperty]
    private bool direccionAMano;

    [RelayCommand]
    private void AlternarDireccionAMano() => DireccionAMano = !DireccionAMano;

    /// <summary>
    /// PASO 1. Busca cajas repartiendo. Si no encuentra ninguna NO es un error: la
    /// difusion puede estar bloqueada aunque las cajas se vean entre si, y para eso
    /// queda el campo de la direccion a mano.
    /// </summary>
    [RelayCommand]
    private async Task BuscarAsync()
    {
        if (Escaneando) return;

        Escaneando = true;
        ReceptorEnFalla = false;
        EstadoReceptor = "Buscando cajas en la red...";
        CajasEncontradas.Clear();
        OnPropertyChanged(nameof(HayCajasEncontradas));

        try
        {
            var encontradas = await PairingDiscovery.BuscarAsync(TimeSpan.FromSeconds(3));

            foreach (var c in encontradas) CajasEncontradas.Add(c);
            OnPropertyChanged(nameof(HayCajasEncontradas));

            if (encontradas.Count == 0)
            {
                // Se abre solo el campo a mano: es exactamente lo que toca hacer
                // ahora, y buscarlo plegado seria un paso de mas justo cuando algo
                // ya salio distinto de lo esperado.
                DireccionAMano = true;
                ReceptorEnFalla = true;
                EstadoReceptor =
                    "No se encontro ninguna caja. Escribe abajo la direccion que muestra " +
                    "la otra caja en su pantalla.";
                return;
            }

            // Con una sola, se elige sola: un toque menos.
            var primera = encontradas[0];
            DireccionOrigen = primera.Address;

            EstadoReceptor = encontradas.Count == 1
                ? $"Caja encontrada: {primera.Address} ({primera.Tienda}). Escribe el codigo."
                : $"{encontradas.Count} cajas encontradas. Elige una y escribe el codigo.";
        }
        finally
        {
            Escaneando = false;
        }
    }

    /// <summary>La caja elegida de la lista, cuando hubo mas de una.</summary>
    [ObservableProperty]
    private CajaEncontrada? cajaSeleccionada;

    partial void OnCajaSeleccionadaChanged(CajaEncontrada? value)
    {
        if (value is null) return;
        DireccionOrigen = value.Address;
        ReceptorEnFalla = false;
        EstadoReceptor = $"Caja elegida: {value.Address} ({value.Tienda}). Escribe el codigo.";
    }

    /// <summary>
    /// Por que no se puede traer todavia, o vacio. Un boton gris sin explicacion
    /// deja al instalador sin saber que falta — la misma leccion que en la pantalla
    /// de primer uso.
    /// </summary>
    public string MotivoTraer
    {
        get
        {
            if (Copiando) return "Trayendo la configuracion...";
            if (string.IsNullOrWhiteSpace(DireccionOrigen))
                return "Falta elegir la caja de la que se copia.";
            if (CodigoIngresado.Trim().Length != PairingSecret.CodeLength)
                return $"Falta el codigo de {PairingSecret.CodeLength} digitos.";
            return string.Empty;
        }
    }

    public bool PuedeTraer => MotivoTraer.Length == 0;

    /// <summary>
    /// PASO 2: trae el padron y lo MUESTRA. No guarda nada.
    ///
    /// Guardar sin confirmar deja una caja operando con el padron de otra tienda, y
    /// eso no se nota hasta que alguien no puede ingresar o firma un abono con un
    /// usuario que no es de ahi.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeTraer))]
    private async Task TraerAsync()
    {
        if (Copiando) return;

        EstadoReceptor = null;
        ReceptorEnFalla = false;
        _recibido = null;
        ResumenRecibido = null;
        TiendaRecibida = null;
        DetalleDeBajas = null;
        HayBajas = false;
        NoHayCambios = false;
        OnPropertyChanged(nameof(HayAlgoPorConfirmar));

        // LA VALIDACION SE REPITE ACA A PROPOSITO. El CanExecute apaga el boton
        // pero no protege el comando: invocarlo por codigo lo ejecuta igual. Es la
        // misma leccion que dejo [ConfigurarAdminViewModel], donde la falta de esta
        // linea guardaba un PIN que el instalador no habia escrito.
        if (!PuedeTraer)
        {
            ReceptorEnFalla = true;
            EstadoReceptor = MotivoTraer;
            return;
        }

        Copiando = true;
        try
        {
            var resultado = await new PairingClient()
                .FetchAsync(DireccionOrigen.Trim(), CodigoIngresado.Trim());

            EstadoReceptor = PairingClient.Describir(resultado);
            ReceptorEnFalla = !resultado.Succeeded;

            if (!resultado.Succeeded) return;

            // ─────────────────────────────────────────────────────────────────
            // AL ACTUALIZAR, LA TIENDA SE VERIFICA; NO SE MUESTRA Y YA
            // ─────────────────────────────────────────────────────────────────
            // En una caja que se monta, el operador esta mirando la pantalla y ve de
            // que tienda viene el sobre. En una actualizacion rutinaria nadie lee.
            //
            // Y el escenario no es teorico: en un centro comercial hay otra KOAJ en
            // la misma red. Un codigo tecleado en la caja equivocada reemplazaria el
            // padron de esta tienda por el de la de al lado.
            //
            // Se compara el nombre de tienda que viaja en el saludo contra el de esta
            // caja. Los dos salen de CloudLicense, asi que si son distintos son dos
            // tiendas distintas. No se agrega nada al sobre: ver la nota de identidad
            // en [CashierRosterEnvelope].
            if (SoloCajeros && !EsLaMismaTienda(resultado))
            {
                ReceptorEnFalla = true;
                EstadoReceptor =
                    $"Esa caja es de otra tienda ({Nombrar(resultado.Tienda)}) y esta es de " +
                    $"{Nombrar(config.StoreName)}. No se actualiza nada.";

                AppLogger.W("ReplicacionViewModel",
                    $"Actualizacion rechazada: origen tienda='{resultado.Tienda}' " +
                    $"storeId='{resultado.StoreId ?? "(no lo manda)"}'; " +
                    $"esta caja tienda='{config.StoreName}' " +
                    $"storeId='{config.Tienda.StoreId ?? "(sin elegir)"}'.");
                return;
            }

            _recibido = resultado.Envelope;
            TiendaRecibida = resultado.Tienda;

            if (SoloCajeros)
            {
                // Si no se pudo calcular el diferencial, NO se deja aplicar nada.
                // Aplicar a ciegas es justo el caso que esta pantalla existe para
                // evitar: el padron se reemplaza entero y las bajas no se ven.
                if (!await PrepararDiferenciaAsync())
                {
                    _recibido = null;
                    TiendaRecibida = null;
                    OnPropertyChanged(nameof(HayAlgoPorConfirmar));
                    return;
                }
            }
            else
            {
                PrepararResumenCompleto();
            }

            EstadoReceptor = null;
            OnPropertyChanged(nameof(HayAlgoPorConfirmar));
        }
        finally
        {
            Copiando = false;
        }
    }

    /// <summary>
    /// ¿El padron que llego es de ESTA tienda?
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// SE PREFIERE EL StoreId, Y NO ES UN DETALLE
    /// ─────────────────────────────────────────────────────────────────────────
    /// STORE_ID es obligatorio en CloudLicense —la configuracion no valida sin el—
    /// mientras que STORE_NAME es opcional. Si se comparara solo por nombre y la
    /// tienda no lo tuviera configurado, las dos cajas dirian "Permoda" y el control
    /// pasaria SIEMPRE: una barrera que parece que protege y no discrimina nada es
    /// peor que no tenerla, porque nadie la revisa.
    ///
    /// Por eso: si las dos cajas anuncian StoreId, mandan los StoreId. Si la otra no
    /// lo manda —APK viejo, que tiene que seguir funcionando de emisor— se cae al
    /// nombre y queda anotado en el log que no se pudo verificar en firme.
    /// </summary>
    private bool EsLaMismaTienda(PairingResult resultado)
    {
        // La tienda ELEGIDA en el terminal, no la que viaja a Credinet: esa ultima
        // es null en sandbox a proposito. Ver el saludo mas arriba.
        var mio = config.Tienda.StoreId?.Trim();
        var suyo = resultado.StoreId?.Trim();

        if (!string.IsNullOrEmpty(mio) && !string.IsNullOrEmpty(suyo))
            return string.Equals(mio, suyo, StringComparison.OrdinalIgnoreCase);

        AppLogger.W("ReplicacionViewModel",
            $"No se pudo verificar la tienda por StoreId (esta caja: " +
            $"'{mio ?? "(sin configurar)"}', la otra: '{suyo ?? "(no lo manda)"}'). " +
            "Se compara por nombre de tienda.");

        return !string.IsNullOrWhiteSpace(resultado.Tienda)
            && !string.IsNullOrWhiteSpace(config.StoreName)
            && string.Equals(
                resultado.Tienda.Trim(), config.StoreName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Nombrar(string? tienda) =>
        string.IsNullOrWhiteSpace(tienda) ? "sin nombre" : tienda.Trim();

    /// <summary>
    /// QUE TIENDA CORRESPONDE APLICAR AL RECIBIR UN SOBRE. Null = ninguna.
    ///
    /// Es la regla, separada del efecto, para poder ejercitarla sin levantar un
    /// socket ni montar una caja. Decide tres cosas:
    ///
    ///   · En "actualizar cajeros" NO se aplica NUNCA. Esa operacion es
    ///     deliberadamente angosta: toca el padron y nada mas. Si moviera la tienda,
    ///     un boton que dice "cajeros" estaria cambiando a nombre de quien se venden
    ///     los creditos, y eso no se veria hasta conciliar.
    ///
    ///   · Un sobre sin tienda (APK anterior en la caja emisora) no aplica nada: se
    ///     elige a mano y la pantalla lo dice.
    ///
    ///   · Una tienda que no figura en el catalogo de ESTE APK tampoco se aplica.
    ///     Seria dejar la caja operando con una tienda que no puede ni nombrar en
    ///     pantalla; lo correcto es actualizar el APK.
    /// </summary>
    public static TiendaDelCatalogo? TiendaAAplicar(bool soloCajeros, string? storeIdDelSobre) =>
        soloCajeros ? null : CatalogoDeTiendas.PorStoreId(storeIdDelSobre);

    /// <summary>
    /// Deja esta caja asociada a la tienda que vino en el sobre. Devuelve como se
    /// llama esa tienda, o null si no se aplico nada.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ESTO AHORRA DOS TERCIOS DE LOS ERRORES
    /// ─────────────────────────────────────────────────────────────────────────
    /// Sin esto hay que elegir la tienda en las tres cajas de cada local: tres
    /// oportunidades de equivocarse donde alcanza con una. Y el error no se ve —la
    /// caja vende igual— hasta que alguien concilia y encuentra creditos a nombre
    /// de otra tienda.
    ///
    /// NUNCA LANZA. Si algo falla, la caja queda sin tienda y el mensaje de exito
    /// lo dice: eso es recuperable tocando un boton. Tumbar la app a mitad del
    /// montaje, no.
    /// </summary>
    private string? AplicarTiendaRecibida()
    {
        var id = _recibido?.StoreId;
        var delCatalogo = TiendaAAplicar(SoloCajeros, id);

        if (delCatalogo is null)
        {
            AppLogger.W("ReplicacionViewModel",
                string.IsNullOrWhiteSpace(id)
                    // Sobre de un APK anterior. No es un fallo: se elige a mano.
                    ? "El sobre no trae tienda (APK anterior en la caja emisora): " +
                      "hay que elegirla en esta caja."
                    // La otra caja tiene una hoja de tiendas mas nueva que este APK.
                    // Aceptar un id que no se reconoce dejaria la caja operando con
                    // una tienda que ni siquiera puede nombrar en pantalla.
                    : $"La tienda recibida ({id}) no figura en el catalogo de este APK. " +
                      "No se aplica: hay que actualizar el APK o elegirla a mano.");
            return null;
        }

        try
        {
            tiendaDeLaCaja.Fijar(delCatalogo);

            // Se recarga para que la configuracion vigente ya refleje la tienda
            // nueva: la pantalla siguiente la lee de ahi.
            configuracion.Reload();

            AppLogger.I("ReplicacionViewModel",
                $"Esta caja queda asociada a {delCatalogo.Etiqueta} (copiada de la otra caja).");

            return delCatalogo.Etiqueta;
        }
        catch (Exception ex)
        {
            AppLogger.E("ReplicacionViewModel", "No se pudo aplicar la tienda recibida.", ex);
            return null;
        }
    }

    /// <summary>Lo que ve el operador al MONTAR una caja: que va a quedar escrito.</summary>
    private void PrepararResumenCompleto()
    {
        var usuarios = string.Join(", ", _recibido!.Cajeros
            .Where(c => c.Activo)
            .Select(c => c.Usuario)
            .Take(8));

        // La tienda se nombra ANTES de aceptar, y no despues.
        //
        // Es el dato que decide a nombre de quien quedan los creditos de esta caja,
        // asi que el operador tiene que verlo mientras todavia puede cancelar. Un
        // sobre de APK viejo no la trae: ahi se dice que hay que elegirla, en vez de
        // callar y dejar la caja sin tienda sin que nadie se entere.
        var tienda = string.IsNullOrWhiteSpace(_recibido.StoreId)
            ? "Tienda: esa caja no la manda (APK anterior). Hay que elegirla en esta."
            : $"Tienda: {CatalogoDeTiendas.Describir(_recibido.StoreId)}";

        ResumenRecibido =
            $"Cajeros: {_recibido.Cajeros.Count} ({_recibido.CajerosActivos} activos)\n" +
            (string.IsNullOrEmpty(usuarios) ? string.Empty : $"Usuarios: {usuarios}\n") +
            "PIN de administrador: se copia el de esa caja\n" +
            tienda;
    }

    /// <summary>
    /// Lo que ve el operador al ACTUALIZAR: que CAMBIA, no que hay.
    ///
    /// "7 cajeros" no deja ver que tres desaparecen. Ver [DiferenciaDePadron].
    /// </summary>
    /// <returns>
    /// <c>false</c> si no se pudo calcular. El llamador entonces descarta el sobre:
    /// sin diferencial a la vista, aplicar es exactamente lo que esta pantalla
    /// existe para evitar.
    /// </returns>
    private async Task<bool> PrepararDiferenciaAsync()
    {
        IReadOnlyList<Cajero> actuales;
        try
        {
            // Leer el padron local toca la BD cifrada, y eso puede fallar (SQLCipher,
            // llave del Keystore). Sin este catch la excepcion sube por el comando y
            // se lleva el proceso, en una caja que solo queria actualizar usuarios.
            actuales = await store.GetCajerosAsync();
        }
        catch (Exception ex)
        {
            AppLogger.E("ReplicacionViewModel",
                "No se pudo leer el padron actual para compararlo con el recibido.", ex);

            ReceptorEnFalla = true;
            EstadoReceptor =
                "No se pudo leer el padron actual de esta caja, asi que no se puede " +
                "mostrar que cambiaria. No se aplico nada; intenta de nuevo.";
            return false;
        }

        var diferencia = DiferenciaDePadron.Entre(actuales, _recibido!.Cajeros);

        ResumenRecibido = diferencia.Resumen;
        HayBajas = diferencia.HayBajas;
        DetalleDeBajas = diferencia.HayBajas
            ? $"Se van a quitar: {diferencia.DetalleDeBajas}."
            : null;
        NoHayCambios = diferencia.SinCambios;

        AppLogger.I("ReplicacionViewModel",
            $"Diferencia del padron contra {Nombrar(TiendaRecibida)}: {diferencia.Resumen}");
        return true;
    }

    /// <summary>
    /// Hay cajeros que van a desaparecer. Es el unico cambio que el operador NO
    /// pidio: viene de que la caja de origen tenga el padron mas viejo.
    /// </summary>
    [ObservableProperty]
    private bool hayBajas;

    [ObservableProperty]
    private string? detalleDeBajas;

    /// <summary>
    /// El sobre trae exactamente lo mismo que ya hay. Decirlo evita que el operador
    /// aplique "por las dudas" y se quede sin saber si funciono.
    /// </summary>
    [ObservableProperty]
    private bool noHayCambios;

    /// <summary>
    /// PASO 3: recien aca se escribe, y se avisa.
    ///
    /// Antes esto guardaba y dejaba al instalador mirando la misma pantalla, sin
    /// una sola señal de que hubiera pasado algo. Ahora confirma con un mensaje y
    /// lo lleva al ingreso: la caja ya esta lista para operar, y es el unico
    /// destino que tiene sentido.
    /// </summary>
    [ObservableProperty]
    private string? mensajeExito;

    public bool TieneMensajeExito => !string.IsNullOrEmpty(MensajeExito);

    partial void OnMensajeExitoChanged(string? value) =>
        OnPropertyChanged(nameof(TieneMensajeExito));

    [RelayCommand]
    private async Task ConfirmarAsync()
    {
        if (_recibido is null) return;

        var cantidad = _recibido.Cajeros.Count;

        // ACA SE SEPARAN LAS DOS OPERACIONES, Y ES EL UNICO LUGAR DONDE IMPORTA.
        //   copiar     -> escribe el PIN de administrador y el padron.
        //   actualizar -> solo el padron, salvo que el operador pida lo contrario.
        var ok = SoloCajeros
            ? await store.ActualizarCajerosAsync(_recibido, IncluirPinAdmin)
            : await store.ImportarPadronAsync(_recibido);

        if (!ok)
        {
            ReceptorEnFalla = true;
            EstadoReceptor = SoloCajeros
                ? "No se pudo actualizar el padron. La caja quedo como estaba."
                : "No se pudo guardar la configuracion recibida. Configura la caja a mano.";
            return;
        }

        // La tienda SOLO al montar una caja nueva. "Actualizar cajeros" es la
        // operacion angosta: toca el padron y nada mas. Si cambiara la tienda, un
        // boton que dice "cajeros" estaria moviendo a nombre de quien se venden los
        // creditos — justo el tipo de efecto invisible que este modulo ya pago caro.
        var tiendaAplicada = SoloCajeros ? null : AplicarTiendaRecibida();

        // ─────────────────────────────────────────────────────────────────────
        // SE CIERRA LA SESION DEL CAJERO
        // ─────────────────────────────────────────────────────────────────────
        // El padron se reemplazo, asi que el que estaba operando pudo haber quedado
        // dado de baja hace un segundo. Dejarlo adentro seria seguir cobrando con
        // una identidad que la tienda acaba de revocar, y ese nombre viaja a
        // Credinet en cada abono.
        sesion.Cerrar();

        _recibido = null;
        ResumenRecibido = null;
        TiendaRecibida = null;
        DetalleDeBajas = null;
        HayBajas = false;
        NoHayCambios = false;
        CodigoIngresado = string.Empty;
        OnPropertyChanged(nameof(HayAlgoPorConfirmar));

        MensajeExito = SoloCajeros
            ? $"Cajeros actualizados: quedaron {cantidad} en esta caja." +
              (IncluirPinAdmin ? " El PIN de administrador tambien se actualizo." : string.Empty)
            : $"Configuracion importada correctamente: {cantidad} cajeros y el PIN de " +
              "administrador quedaron en esta caja." +
              (tiendaAplicada is null
                  ? " Falta elegir la tienda de esta caja."
                  : $" Esta caja queda como {tiendaAplicada}.");

        EstadoReceptor = null;
        ReceptorEnFalla = false;

        AppLogger.I("ReplicacionViewModel",
            $"{(SoloCajeros ? "Padron actualizado" : "Padron importado")} desde otra caja " +
            $"({cantidad} cajeros). Se cierra la sesion y se navega al ingreso.");

        // Un respiro para que el mensaje se alcance a leer antes de cambiar de
        // pantalla. Sin esto el exito pasa tan rapido que parece que no paso nada.
        // Es el tiempo que tarda el check en terminar su rebote.
        await Task.Delay(1600);

        try
        {
            await nav.GoToIngresoCajeroAsync();
        }
        catch (Exception ex)
        {
            // La configuracion YA quedo escrita: que falle la navegacion no puede
            // hacer creer que no se importo.
            AppLogger.E("ReplicacionViewModel", "Error navegando al ingreso tras importar.", ex);
            EstadoReceptor =
                "La configuracion quedo guardada. Vuelve atras para ingresar con tu usuario.";
        }
    }

    /// <summary>
    /// Descarta lo traido sin guardarlo. La confirmacion tiene que tener las dos
    /// salidas: si el resumen muestra una tienda que no es, el unico camino no
    /// puede ser aceptar.
    /// </summary>
    [RelayCommand]
    private void DescartarRecibido()
    {
        _recibido = null;
        ResumenRecibido = null;
        TiendaRecibida = null;
        DetalleDeBajas = null;
        HayBajas = false;
        NoHayCambios = false;
        OnPropertyChanged(nameof(HayAlgoPorConfirmar));

        EstadoReceptor = "Se descarto lo recibido. No se cambio nada en esta caja.";
        ReceptorEnFalla = false;
    }

    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apaga el socket y el descubrimiento. La Page lo llama en OnDisappearing: en
    /// cuanto el operador sale de la pantalla, esta caja deja de ofrecer el padron.
    /// </summary>
    public async Task DetenerAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }

        if (_discovery is not null)
        {
            await _discovery.DisposeAsync();
            _discovery = null;
        }

        VentanaAbierta = false;
        Apurando = PorVencer = false;
        FraccionRestante = 0;
        TiempoRestante = "0:00";
        CajasCopiadas = string.Empty;
        Codigo = null;
    }
}
