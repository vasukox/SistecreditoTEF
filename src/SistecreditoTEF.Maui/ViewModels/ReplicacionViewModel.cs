using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Auth;
using SistecreditoTEF.Maui.Services.Credinet;
using SistecreditoTEF.Maui.Services.Pairing;
using SistecreditoTEF.Maui.Services.Platform;

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
    INavigationService nav) : ObservableObject
{
    private PairingHost? _host;
    private PairingDiscovery? _discovery;
    private CashierRosterEnvelope? _recibido;

    // ══════════════════════════════════════════════════════════════════════════
    // EMISOR: la caja que ya esta configurada
    // ══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string? codigo;

    [ObservableProperty]
    private string? direccionPropia;

    [ObservableProperty]
    private string? estadoEmisor;

    [ObservableProperty]
    private bool repartiendo;

    public bool TieneCodigo => !string.IsNullOrEmpty(Codigo);

    partial void OnCodigoChanged(string? value) => OnPropertyChanged(nameof(TieneCodigo));

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

        try
        {
            var sobre = await store.ExportarPadronAsync();
            if (sobre is null)
            {
                EstadoEmisor =
                    "Esta caja todavia no esta completa: necesita el PIN de administrador " +
                    "y al menos un cajero para poder compartir.";
                return;
            }

            await DetenerAsync();

            // La fabrica es asincrona y se vuelve a llamar en CADA entrega: entre la
            // primera caja y la tercera el administrador pudo dar de alta un cajero,
            // y la tercera tiene que recibir el padron de verdad.
            _host = new PairingHost(_ => store.ExportarPadronAsync(), config.StoreName);
            _host.Start();

            _discovery = new PairingDiscovery();
            _discovery.StartResponding(config.StoreName, _host.Port);

            Codigo = _host.Code;
            DireccionPropia = PairingDiscovery.LocalAddress();
            Repartiendo = true;

            ActualizarEstadoEmisor();

            AppLogger.I("ReplicacionViewModel",
                $"Ventana de replicacion abierta en el puerto {_host.Port} " +
                $"({sobre.Cajeros.Count} cajeros por compartir).");
        }
        catch (Exception ex)
        {
            AppLogger.E("ReplicacionViewModel", "No se pudo abrir la ventana de replicacion.", ex);
            EstadoEmisor = "No se pudo abrir la ventana. Intenta de nuevo.";
            await DetenerAsync();
        }
    }

    /// <summary>
    /// Texto de estado del emisor. Lo refresca la Page con un temporizador: el
    /// operador tiene que ver cuanto le queda y cuantas cajas copiaron.
    /// </summary>
    public void ActualizarEstadoEmisor()
    {
        if (_host is null) { EstadoEmisor = null; return; }

        if (!_host.IsOpen)
        {
            Repartiendo = false;
            Codigo = null;
            EstadoEmisor = _host.AttemptsRemaining <= 0
                ? "El codigo se quemo por intentos fallidos. Genera uno nuevo."
                : "La ventana se cerro. Genera un codigo nuevo.";
            return;
        }

        var restante = _host.TimeLeft;
        var cajas = _host.SuccessfulTransfers;

        EstadoEmisor =
            $"Se cierra en {restante.Minutes}:{restante.Seconds.ToString("D2", CultureInfo.InvariantCulture)}" +
            $" · {cajas} caja{(cajas == 1 ? "" : "s")} configurada{(cajas == 1 ? "" : "s")}";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RECEPTOR: la caja nueva
    // ══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string direccionOrigen = string.Empty;

    [ObservableProperty]
    private string codigoIngresado = string.Empty;

    [ObservableProperty]
    private string? estadoReceptor;

    [ObservableProperty]
    private bool copiando;

    /// <summary>Datos del sobre traido, para que el operador confirme ANTES de guardar.</summary>
    [ObservableProperty]
    private string? resumenRecibido;

    public bool HayAlgoPorConfirmar => _recibido is not null;

    /// <summary>Cajas repartiendo que respondieron en la red.</summary>
    public ObservableCollection<CajaEncontrada> CajasEncontradas { get; } = [];

    [ObservableProperty]
    private bool escaneando;

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
        EstadoReceptor = "Buscando cajas en la red...";
        CajasEncontradas.Clear();

        try
        {
            var encontradas = await PairingDiscovery.BuscarAsync(TimeSpan.FromSeconds(3));

            foreach (var c in encontradas) CajasEncontradas.Add(c);

            if (encontradas.Count == 0)
            {
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
        EstadoReceptor = $"Caja elegida: {value.Address} ({value.Tienda}). Escribe el codigo.";
    }

    /// <summary>
    /// PASO 1: trae el padron y lo MUESTRA. No guarda nada.
    ///
    /// Guardar sin confirmar deja una caja operando con el padron de otra tienda, y
    /// eso no se nota hasta que alguien no puede ingresar o firma un abono con un
    /// usuario que no es de ahi.
    /// </summary>
    [RelayCommand]
    private async Task TraerAsync()
    {
        if (Copiando) return;

        EstadoReceptor = null;
        _recibido = null;
        ResumenRecibido = null;
        OnPropertyChanged(nameof(HayAlgoPorConfirmar));

        if (string.IsNullOrWhiteSpace(DireccionOrigen) ||
            CodigoIngresado.Trim().Length != PairingSecret.CodeLength)
        {
            EstadoReceptor =
                $"Escribe la direccion de la otra caja y el codigo de {PairingSecret.CodeLength} digitos.";
            return;
        }

        Copiando = true;
        try
        {
            var resultado = await new PairingClient()
                .FetchAsync(DireccionOrigen.Trim(), CodigoIngresado.Trim());

            EstadoReceptor = PairingClient.Describir(resultado);

            if (!resultado.Succeeded) return;

            _recibido = resultado.Envelope;

            // Lo que el operador tiene que poder leer ANTES de aceptar: de donde
            // viene y que va a quedar en esta caja.
            var usuarios = string.Join(", ", _recibido!.Cajeros
                .Where(c => c.Activo)
                .Select(c => c.Usuario)
                .Take(8));

            ResumenRecibido =
                $"Tienda: {resultado.Tienda}\n" +
                $"Cajeros: {_recibido.Cajeros.Count} ({_recibido.CajerosActivos} activos)\n" +
                (string.IsNullOrEmpty(usuarios) ? string.Empty : $"Usuarios: {usuarios}\n") +
                "PIN de administrador: se copia el de esa caja\n\n" +
                "Al aceptar, esto reemplaza la configuracion de ESTA caja.";

            EstadoReceptor = "Configuracion lista para importar. Revisala y toca Aceptar.";
            OnPropertyChanged(nameof(HayAlgoPorConfirmar));
        }
        finally
        {
            Copiando = false;
        }
    }

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
        var ok = await store.ImportarPadronAsync(_recibido);

        if (!ok)
        {
            EstadoReceptor =
                "No se pudo guardar la configuracion recibida. Configura la caja a mano.";
            return;
        }

        _recibido = null;
        ResumenRecibido = null;
        CodigoIngresado = string.Empty;
        OnPropertyChanged(nameof(HayAlgoPorConfirmar));

        MensajeExito =
            $"Configuracion importada correctamente: {cantidad} cajeros y el PIN de " +
            "administrador quedaron en esta caja.";
        EstadoReceptor = null;

        AppLogger.I("ReplicacionViewModel",
            $"Padron importado desde otra caja ({cantidad} cajeros). Se navega al ingreso.");

        // Un respiro para que el mensaje se alcance a leer antes de cambiar de
        // pantalla. Sin esto el exito pasa tan rapido que parece que no paso nada.
        await Task.Delay(1200);

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

        Repartiendo = false;
        Codigo = null;
    }
}
