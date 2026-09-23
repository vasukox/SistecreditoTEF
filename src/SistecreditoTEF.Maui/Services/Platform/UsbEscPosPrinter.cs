using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using SistecreditoTEF.Maui.Common;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Imprime el comprobante de abono en la impresora térmica USB del POS,
/// enviándole ESC/POS directamente por la API USB Host de Android.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUÉ ESTE CAMINO
/// ─────────────────────────────────────────────────────────────────────────────
/// Se verificó en el terminal:
///
///   • La impresora es `SOL801V Printer`, VID 0x0483 / PID 0x5720, con una
///     interfaz de **clase 07 (impresora)**, subclase 01, protocolo 02, y un
///     endpoint **bulk OUT 0x01** de 64 bytes.
///   • `enabled_print_services` está en **null**: Android no tiene ningún servicio
///     de impresión activo, y el integrado (`com.android.bips`) es para impresoras
///     de red, no USB. Por eso el Android Print Framework nunca la ve.
///   • **HioPos le habla por USB Host**, capturado de su propio log:
///       E/DBG(HioPos): file:/dev/bus/usb/002/006; vid:1155; pid:22304;
///                      product:SOL801V Printer; class:7; ... endp:0; addr:1;
///       D/UsbDeviceConnectionJNI(HioPos): close
///
/// Así que este printer replica el mecanismo del propio POS. No necesita SDK de
/// fabricante, ni root, ni habilitar nada en Ajustes.
///
/// La detección es **por clase de interfaz (07), no por VID/PID**: si mañana
/// cambian el modelo de impresora, sigue funcionando. El VID/PID se registra en el
/// log para diagnóstico.
/// </summary>
public class UsbEscPosPrinter : IReceiptPrinter
{
    /// <summary>
    /// Accion del broadcast interno del permiso de USB.
    ///
    /// Se deriva del package en tiempo de ejecucion y no se escribe fija: estaba
    /// como "com.pos2pay.USB_PERMISSION" y al cambiar el package a
    /// com.permoda.sistecreditotef habria quedado apuntando a un nombre que ya no
    /// existe. El PendingIntent se filtra por package, asi que un desajuste ahi
    /// significa que la respuesta del cajero al dialogo nunca vuelve y la impresion
    /// queda colgada hasta el timeout.
    /// </summary>
    private static string PermissionAction =>
        $"{Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName}.USB_PERMISSION";

    /// <summary>Timeout de cada escritura bulk. Una térmica responde en milisegundos.</summary>
    private const int WriteTimeoutMs = 5_000;

    /// <summary>Cuánto se espera a que el cajero acepte el permiso de USB.</summary>
    private static readonly TimeSpan PermissionTimeout = TimeSpan.FromSeconds(25);

    public string Name => "Impresora USB (ESC/POS)";

    public bool IsAvailable
    {
        get
        {
            try
            {
                return FindPrinter(out _, out _) is not null;
            }
            catch (Exception ex)
            {
                AppLogger.W("UsbEscPosPrinter", $"No se pudo consultar el bus USB: {ex.Message}");
                return false;
            }
        }
    }

    public async Task<bool> PrintAsync(StandaloneReceipt receipt)
    {
        try
        {
            var manager = UsbManagerOrNull();
            if (manager is null)
            {
                AppLogger.E("UsbEscPosPrinter", "UsbManager no disponible.");
                return false;
            }

            var device = FindPrinter(out var iface, out var endpoint);
            if (device is null || iface is null || endpoint is null)
            {
                AppLogger.W("UsbEscPosPrinter",
                    "No se encontro ninguna impresora USB con endpoint bulk de salida.");
                return false;
            }

            // OJO con el formato: endpoint.Address es del enum UsbAddressing, y "X2"
            // no es un especificador valido para enums en .NET — lanza
            // FormatException. Hay que convertir a int ANTES de formatear.
            //
            // Este detalle tumbo la primera version: PrintAsync explotaba armando
            // este mismo mensaje de log, antes de siquiera pedir el permiso de USB,
            // y el comprobante nunca se imprimia.
            AppLogger.I("UsbEscPosPrinter",
                $"Impresora detectada: '{device.ProductName}' " +
                $"VID=0x{device.VendorId:X4} PID=0x{device.ProductId:X4}, " +
                $"endpoint OUT=0x{(int)endpoint.Address:X2} maxPacket={endpoint.MaxPacketSize}.");

            if (!await EnsurePermissionAsync(manager, device))
            {
                AppLogger.E("UsbEscPosPrinter",
                    "El cajero no concedio permiso para usar la impresora USB.");
                return false;
            }

            var payload = EscPos.Build(ReceiptTextBuilder.BuildLines(receipt));
            AppLogger.I("UsbEscPosPrinter",
                $"Enviando {payload.Length} bytes ESC/POS del comprobante #{receipt.PaymentNumber}.");

            return await Task.Run(() => Write(manager, device, iface, endpoint, payload));
        }
        catch (Exception ex)
        {
            AppLogger.E("UsbEscPosPrinter", "Error imprimiendo por USB", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Detección
    // ------------------------------------------------------------------

    private static UsbManager? UsbManagerOrNull()
    {
        var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ApplicationContext;
        return context?.GetSystemService(Context.UsbService) as UsbManager;
    }

    /// <summary>
    /// Busca una impresora USB con un endpoint bulk de salida.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE NO ALCANZA CON LA CLASE 07
    /// ─────────────────────────────────────────────────────────────────────────
    /// La version anterior exigia interfaz de clase 07 (Printer) y descartaba todo
    /// lo demas. En el terminal de Permoda eso hacia que la impresora nunca se
    /// encontrara, y el modulo reportaba "no disponible" sin siquiera intentar.
    ///
    /// Lo que hay conectado, leido del propio terminal:
    ///
    ///   /sys/bus/usb/devices/2-1.3.1/  VID=04b8 PID=0202  prod=TM-T88V
    ///   clases de interfaz presentes:  09 09 ff 03 09 09 09
    ///
    /// Una Epson TM-T88V, que es una termica de recibos de verdad, exponiendo su
    /// interfaz como clase 0xFF (vendor-specific) en lugar de 07. No es raro: la
    /// mayoria de las termicas POS lo hacen para poder usar drivers propios.
    ///
    /// El criterio correcto no es la clase declarada sino la CAPACIDAD: una
    /// interfaz con un endpoint bulk de salida es a la que se le puede escribir un
    /// flujo ESC/POS. Se prefiere la clase 07 cuando existe, y si no, se acepta la
    /// vendor-specific.
    ///
    /// Lo que SI se excluye es HID (clase 03): en este mismo terminal hay un lector
    /// de codigo de barras (SM-2D PRODUCT HID KBW) y mandarle bytes de impresion
    /// seria, en el mejor caso, inutil.
    /// </summary>
    private static UsbDevice? FindPrinter(out UsbInterface? iface, out UsbEndpoint? endpoint)
    {
        iface = null;
        endpoint = null;

        var manager = UsbManagerOrNull();
        if (manager?.DeviceList is null) return null;

        // Candidato de respaldo: interfaz vendor-specific con bulk OUT. Se guarda
        // por si no aparece ninguna clase 07, que es lo preferible.
        UsbDevice? respaldoDevice = null;
        UsbInterface? respaldoIface = null;
        UsbEndpoint? respaldoEndpoint = null;

        foreach (var device in manager.DeviceList.Values)
        {
            for (var i = 0; i < device.InterfaceCount; i++)
            {
                var candidate = device.GetInterface(i);

                var esImpresora = candidate.InterfaceClass == UsbClass.Printer;
                var esVendorSpecific = candidate.InterfaceClass == UsbClass.VendorSpec;

                if (!esImpresora && !esVendorSpecific) continue;

                var ep = BuscarBulkOut(candidate);
                if (ep is null) continue;

                if (esImpresora)
                {
                    AppLogger.I("UsbEscPosPrinter",
                        $"Impresora por clase 07: '{device.ProductName}' " +
                        $"VID=0x{device.VendorId:X4} PID=0x{device.ProductId:X4}.");
                    iface = candidate;
                    endpoint = ep;
                    return device;
                }

                if (respaldoDevice is null)
                {
                    respaldoDevice = device;
                    respaldoIface = candidate;
                    respaldoEndpoint = ep;
                }
            }
        }

        if (respaldoDevice is not null)
        {
            AppLogger.I("UsbEscPosPrinter",
                $"Impresora por interfaz vendor-specific (0xFF): '{respaldoDevice.ProductName}' " +
                $"VID=0x{respaldoDevice.VendorId:X4} PID=0x{respaldoDevice.ProductId:X4}. " +
                "No declara clase 07, pero tiene endpoint bulk de salida.");
            iface = respaldoIface;
            endpoint = respaldoEndpoint;
            return respaldoDevice;
        }

        // Sin candidatos: se listan los dispositivos vistos. Sin esto, diagnosticar
        // "no disponible" obliga a leer sysfs por adb, que es lo que hubo que hacer.
        var vistos = string.Join(", ", manager.DeviceList.Values.Select(d =>
            $"'{d.ProductName}' VID=0x{d.VendorId:X4} PID=0x{d.ProductId:X4} " +
            $"({d.InterfaceCount} interfaces)"));
        AppLogger.W("UsbEscPosPrinter",
            $"Ninguna interfaz con bulk OUT. Dispositivos en el bus: " +
            (string.IsNullOrEmpty(vistos) ? "(ninguno)" : vistos));

        return null;
    }

    /// <summary>Primer endpoint bulk de SALIDA de la interfaz, o null.</summary>
    private static UsbEndpoint? BuscarBulkOut(UsbInterface candidate)
    {
        for (var e = 0; e < candidate.EndpointCount; e++)
        {
            var ep = candidate.GetEndpoint(e);
            // GetEndpoint esta declarado como nullable en el binding de Android. En
            // la practica devuelve el endpoint, pero sin esta guarda un null seria
            // una NullReferenceException a mitad de un comprobante.
            if (ep is null) continue;
            if (ep.Type != UsbAddressing.XferBulk) continue;
            if (ep.Direction != UsbAddressing.Out) continue;
            return ep;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Permiso
    // ------------------------------------------------------------------

    /// <summary>
    /// Garantiza el permiso de acceso al dispositivo USB.
    ///
    /// Android exige permiso explícito del usuario para hablarle a un dispositivo
    /// USB. La primera vez el cajero verá un diálogo; si marca "usar de forma
    /// predeterminada para este dispositivo", no vuelve a aparecer.
    ///
    /// NOTA de diseño: existe la alternativa de declarar un intent-filter
    /// USB_DEVICE_ATTACHED con un device_filter.xml, que concede el permiso sin
    /// diálogo. Se descartó a propósito: en un POS con la impresora conectada de
    /// forma permanente, ese filtro hace que Android **lance esta app** cada vez
    /// que se detecta el dispositivo (incluido el arranque del terminal), lo que
    /// interrumpiría a HioPos.
    /// </summary>
    private static async Task<bool> EnsurePermissionAsync(UsbManager manager, UsbDevice device)
    {
        if (manager.HasPermission(device))
        {
            AppLogger.I("UsbEscPosPrinter", "Permiso de USB ya concedido.");
            return true;
        }

        var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ApplicationContext;
        if (context is null) return false;

        AppLogger.I("UsbEscPosPrinter", "Solicitando permiso de USB al cajero...");

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new PermissionReceiver(completion);

        try
        {
            // ReceiverFlags.NotExported: el broadcast es interno de la app. En
            // Android 13+ registrar un receiver sin declarar la exportación lanza.
            context.RegisterReceiver(receiver, new IntentFilter(PermissionAction),
                ReceiverFlags.NotExported);

            // PendingIntentFlags.Mutable es obligatorio: el sistema completa el
            // Intent con el dispositivo y el resultado.
            var pending = PendingIntent.GetBroadcast(
                context, 0, new Intent(PermissionAction).SetPackage(context.PackageName),
                PendingIntentFlags.Mutable | PendingIntentFlags.UpdateCurrent);

            manager.RequestPermission(device, pending);

            var completed = await Task.WhenAny(
                completion.Task, Task.Delay(PermissionTimeout));

            if (completed != completion.Task)
            {
                AppLogger.W("UsbEscPosPrinter", "El permiso de USB no se respondio a tiempo.");
                return false;
            }

            return await completion.Task;
        }
        finally
        {
            try { context.UnregisterReceiver(receiver); }
            catch (Exception ex)
            {
                AppLogger.W("UsbEscPosPrinter", $"No se pudo desregistrar el receiver: {ex.Message}");
            }
        }
    }

    private sealed class PermissionReceiver : BroadcastReceiver
    {
        private readonly TaskCompletionSource<bool> _completion;

        public PermissionReceiver(TaskCompletionSource<bool> completion) => _completion = completion;

        public override void OnReceive(Context? context, Intent? intent)
        {
            var granted = intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false;
            AppLogger.I("UsbEscPosPrinter", $"Permiso de USB: {(granted ? "CONCEDIDO" : "DENEGADO")}.");
            _completion.TrySetResult(granted);
        }
    }

    // ------------------------------------------------------------------
    // Escritura
    // ------------------------------------------------------------------

    /// <summary>
    /// Escribe el flujo ESC/POS en el endpoint bulk, en bloques del tamaño de
    /// paquete que declara el endpoint (64 bytes en esta impresora).
    ///
    /// Devuelve true solo si TODOS los bloques se escribieron. Un
    /// <c>bulkTransfer</c> que devuelve un valor negativo es un fallo real, y en
    /// ese caso se reporta false para que [CompositeReceiptPrinter] use el
    /// siguiente medio.
    /// </summary>
    private static bool Write(
        UsbManager manager, UsbDevice device, UsbInterface iface, UsbEndpoint endpoint, byte[] payload)
    {
        UsbDeviceConnection? connection = null;
        var claimed = false;
        try
        {
            connection = manager.OpenDevice(device);
            if (connection is null)
            {
                AppLogger.E("UsbEscPosPrinter", "No se pudo abrir la conexion con la impresora.");
                return false;
            }

            claimed = connection.ClaimInterface(iface, true);
            if (!claimed)
            {
                AppLogger.E("UsbEscPosPrinter",
                    "No se pudo reclamar la interfaz de la impresora. " +
                    "Puede estar en uso por otra aplicacion (por ejemplo HioPos imprimiendo).");
                return false;
            }

            var chunk = Math.Max(endpoint.MaxPacketSize, 1);
            var sent = 0;

            while (sent < payload.Length)
            {
                var size = Math.Min(chunk, payload.Length - sent);
                var buffer = new byte[size];
                Array.Copy(payload, sent, buffer, 0, size);

                var written = connection.BulkTransfer(endpoint, buffer, size, WriteTimeoutMs);
                if (written < 0)
                {
                    AppLogger.E("UsbEscPosPrinter",
                        $"bulkTransfer fallo en el byte {sent} de {payload.Length} (retorno {written}).");
                    return false;
                }
                sent += written > 0 ? written : size;
            }

            AppLogger.I("UsbEscPosPrinter", $"Comprobante enviado a la impresora ({sent} bytes).");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.E("UsbEscPosPrinter", "Error escribiendo en la impresora USB", ex);
            return false;
        }
        finally
        {
            if (claimed && connection is not null)
            {
                try { connection.ReleaseInterface(iface); } catch { /* best-effort */ }
            }
            connection?.Close();
        }
    }
}
