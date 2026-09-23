using Microsoft.Extensions.DependencyInjection;
using SistecreditoTEF.Maui.Services.Hiopos;

namespace SistecreditoTEF.Maui.Views;

/// <summary>
/// Base de las pantallas del flujo que pueden devolverle el control a HioPos:
/// consulta de cliente, cliente validado, simulacion de cuotas, lista de creditos
/// y abono.
///
/// ───────────────────────────────────────────────────────────────────────────────
/// QUE APORTA
/// ───────────────────────────────────────────────────────────────────────────────
/// El boton fisico "atras" del POS hace lo MISMO que el boton "Volver a HioPos"
/// del header. Sin esto los dos gestos se comportaban distinto en la misma
/// pantalla: el del header devolvia el control al POS y el de abajo hacia pop
/// dentro del flujo, o peor, se salia sin responderle a HioPos y la venta quedaba
/// colgada esperando un resultado que nunca llegaba.
///
/// Las pantallas donde el cobro YA esta en curso (OTP, confirmacion, recibo) NO
/// heredan de aca a proposito: ahi irse no es gratis.
///
/// El servicio se resuelve del contenedor en vez de inyectarse en el constructor
/// para no tocar la firma de las cinco paginas (todas se construyen por DI con sus
/// propios parametros). Es el mismo patron que usa [AppShell].
/// </summary>
public abstract class HioposFlowPage : ContentPage
{
    private static IHioposExit? Salida =>
        IPlatformApplication.Current?.Services.GetService<IHioposExit>();

    /// <summary>
    /// True si el modulo esta atendiendo una operacion de HioPos. Las paginas lo
    /// usan para decidir que mostrar; el boton lo resuelve por si mismo.
    /// </summary>
    protected static bool EnHiopos => Salida?.Disponible == true;

    protected override bool OnBackButtonPressed()
    {
        // Volver devuelve false si no hay nada que devolverle al POS (abonos por el
        // icono) o si algo fallo. En los dos casos el "atras" normal es lo correcto.
        if (Salida?.Volver(GetType().Name) == true)
            return true;

        return base.OnBackButtonPressed();
    }
}
