using SistecreditoTEF.Maui.Services.Platform;
using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// Los plazos que se le ofrecen al cajero tienen que ser SOLO los que Sistecrédito
/// confirmó para ese monto.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LOS DOS DEFECTOS QUE ESTOS TESTS PROTEGEN
/// ─────────────────────────────────────────────────────────────────────────────
/// 1. El caso "todavía no se sabe" devolvía la lista COMPLETA de candidatos
///    (1, 2, 3, 6, 9, 12, 18, 24). Y es el estado por defecto: al entrar a la
///    pantalla, durante el debounce y durante la consulta. El cajero podía elegir
///    un plazo no habilitado; si la consulta fallaba, quedaban ofrecidos siempre.
///
/// 2. Aun con el límite confirmado, la lista se derivaba de
///    <c>getSimulatedMonthLimit</c> filtrando <c>m &lt;= MaxMonths</c>. Ese endpoint
///    devuelve un número que NO significa "todo hasta acá es válido": en terminal,
///    con un crédito de $99.900, se ofrecían 6, 3 y 2 meses y Credinet rechazó los
///    tres con <c>errorCode=222 MonthsNumberNotValid</c>. Solo 1 era válido.
///
/// Ahora la lista sale de preguntarle a <c>getCreditDetails</c> plazo por plazo —la
/// única autoridad—, y sin confirmación no se ofrece nada. Esa consulta se testea en
/// <c>SistecreditoServiceTests</c>; acá se cubre que la pantalla falle CERRADO.
/// </summary>
public class PlazosAutorizadosTests
{
    private static SeleccionCuotasViewModel BuildVm() =>
        new(service: null!, state: new TransactionStateStore(), nav: null!);

    // ------------------------------------------------------------------
    // Falla cerrado: sin confirmación, ningún plazo
    // ------------------------------------------------------------------

    [Fact]
    public void Al_entrar_a_la_pantalla_no_se_ofrece_ningun_plazo()
    {
        var vm = BuildVm();

        Assert.Empty(vm.PlazosDisponibles);
        Assert.False(vm.HayPlazos);
    }

    [Fact]
    public void Mientras_se_consultan_los_plazos_no_se_ofrece_ninguno()
    {
        var vm = BuildVm();
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Loading;

        // Es la ventana en la que antes se ofrecían los 8 candidatos.
        Assert.Empty(vm.PlazosDisponibles);
    }

    [Fact]
    public void Si_la_consulta_falla_no_se_ofrece_ningun_plazo()
    {
        var vm = BuildVm();
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Error;

        Assert.Empty(vm.PlazosDisponibles);
    }

    [Fact]
    public void Un_MaxMonths_alto_ya_no_habilita_plazos_por_si_solo()
    {
        // Este es el defecto 2. Antes, con MaxMonths=24 y estado Success, la
        // pantalla ofrecía los 8 plazos aunque Credinet no hubiera confirmado
        // ninguno. Ahora MaxMonths es solo informativo: la lista viene de los
        // plazos realmente confirmados, que acá están vacíos.
        var vm = BuildVm();
        vm.MaxMonths = 24;
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Success;

        Assert.Empty(vm.PlazosDisponibles);
    }

    // ------------------------------------------------------------------
    // No queda un plazo obsoleto seleccionado
    // ------------------------------------------------------------------

    [Fact]
    public void Sin_plazos_confirmados_no_queda_ninguno_seleccionado()
    {
        var vm = BuildVm();
        vm.MesesSeleccionados = 12;

        // Se reconsulta (el cajero cambió el monto): se pierde la confirmación.
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Loading;

        Assert.Equal(0, vm.MesesSeleccionados);
    }

    // ------------------------------------------------------------------
    // El cajero entiende por qué no hay opciones
    // ------------------------------------------------------------------

    [Fact]
    public void Sin_monto_se_explica_que_falta_ingresarlo()
    {
        var vm = BuildVm();

        Assert.Contains("Ingresa el monto", vm.MensajePlazos);
    }

    [Fact]
    public void Mientras_consulta_se_explica_que_esta_consultando()
    {
        var vm = BuildVm();
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Loading;

        Assert.Contains("Consultando", vm.MensajePlazos);
    }

    [Fact]
    public void Si_ningun_plazo_aplica_se_dice_y_se_sugiere_otro_monto()
    {
        var vm = BuildVm();
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Success;

        // Es el caso de $99.900 si ni 1 mes aplicara: hay que decirlo, no dejar la
        // pantalla en blanco.
        Assert.Contains("no acepta", vm.MensajePlazos);
    }

    [Fact]
    public void Si_fallo_la_consulta_se_dice_que_no_se_pudo()
    {
        var vm = BuildVm();
        vm.LimitStatus = SeleccionCuotasViewModel.EstadoLimite.Error;

        Assert.Contains("No pudimos", vm.MensajePlazos);
    }

    // ------------------------------------------------------------------
    // La simulación mostrada no puede quedar desfasada
    // ------------------------------------------------------------------

    [Fact]
    public void Cambiar_el_monto_descarta_la_simulacion_en_pantalla()
    {
        var vm = BuildVm();
        vm.Status = SeleccionCuotasViewModel.EstadoSimulacion.Success;

        vm.Monto = 750000m;

        // Si la tarjeta de resultado sobreviviera al cambio, el cajero podría
        // confirmar un crédito con la cuota del monto anterior.
        Assert.Equal(SeleccionCuotasViewModel.EstadoSimulacion.Idle, vm.Status);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public void Elegir_un_plazo_no_confirmado_no_produce_una_simulacion()
    {
        var vm = BuildVm();
        vm.Status = SeleccionCuotasViewModel.EstadoSimulacion.Success;

        // 9 no está entre los confirmados (no hay ninguno).
        vm.MesesSeleccionados = 9;

        Assert.Equal(SeleccionCuotasViewModel.EstadoSimulacion.Idle, vm.Status);
        Assert.Null(vm.Detalles);
    }
}
