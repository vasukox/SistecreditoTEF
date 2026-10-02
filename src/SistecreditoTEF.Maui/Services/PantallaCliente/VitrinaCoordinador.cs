using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui.Services.PantallaCliente;

/// <summary>
/// El unico punto de la app que decide CUANDO se repinta la pantalla del cliente.
///
/// Se engancha a <c>Shell.Navigated</c> —o sea, a "cambio la pantalla del
/// asesor"— y traduce con [GuionDeLaVitrina]. Ningun ViewModel lo conoce ni lo
/// llama: ese es todo el punto. Ver el encabezado del guion.
///
/// La otra mitad la pone [MainActivity]: cuando la app se va a segundo plano
/// suelta la pantalla del cliente, y al volver la vuelve a pedir. Sin eso, un
/// "¡Compra aprobada!" quedaria colgado mirando al salon mientras el asesor ya
/// esta cobrando la siguiente factura en HioPos.
/// </summary>
public sealed class VitrinaCoordinador(IPantallaCliente pantalla, ITransactionStateStore estado)
{
    private Shell? _shell;

    /// <summary>
    /// Empieza a seguir a este Shell. Se puede llamar varias veces: si es el mismo
    /// no hace nada, y si es otro se despega del anterior. Que no se acumulen
    /// suscripciones importa porque el Shell se recrea cuando Android recrea la
    /// Activity.
    /// </summary>
    public void Enganchar(Shell shell)
    {
        try
        {
            if (ReferenceEquals(_shell, shell)) return;

            if (_shell is not null) _shell.Navigated -= OnNavegado;

            _shell = shell;
            _shell.Navigated += OnNavegado;

            AppLogger.I("Vitrina",
                $"Pantalla del cliente enganchada al Shell. Segunda pantalla: " +
                $"{(pantalla.Disponible ? "detectada" : "no detectada")}.");

            Refrescar();
        }
        catch (Exception ex)
        {
            AppLogger.W("Vitrina", $"No se pudo enganchar la pantalla del cliente: {ex.Message}");
        }
    }

    /// <summary>
    /// Vuelve a calcular y pintar lo que corresponde a la pantalla actual. La
    /// llama [MainActivity] al volver del segundo plano.
    /// </summary>
    public void Refrescar()
    {
        try
        {
            // Auto-reparacion. El enganche del arranque ocurre en [App.CreateWindow],
            // que corre una sola vez y va envuelto en un try: si ahi el contenedor
            // todavia no resolvia, sin esto el cartel se quedaria sordo a la
            // navegacion para siempre y mostrando la marca. Como [MainActivity] llama
            // aca en cada vuelta al frente, se recupera solo.
            if (_shell is null && Shell.Current is { } actual) Enganchar(actual);

            var ruta = _shell?.CurrentState?.Location?.OriginalString;
            pantalla.Mostrar(GuionDeLaVitrina.Para(ruta, estado));
        }
        catch (Exception ex)
        {
            // Que la vitrina no se pueda calcular no puede costar una venta.
            AppLogger.W("Vitrina", $"No se pudo refrescar la pantalla del cliente: {ex.Message}");
        }
    }

    /// <summary>Suelta la pantalla del cliente (app en segundo plano).</summary>
    public void Soltar()
    {
        try
        {
            pantalla.Ocultar();
        }
        catch (Exception ex)
        {
            AppLogger.W("Vitrina", $"No se pudo soltar la pantalla del cliente: {ex.Message}");
        }
    }

    private void OnNavegado(object? sender, ShellNavigatedEventArgs e) => Refrescar();
}
