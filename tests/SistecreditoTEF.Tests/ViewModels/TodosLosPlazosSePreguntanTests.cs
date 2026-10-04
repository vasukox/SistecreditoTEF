using SistecreditoTEF.Maui.ViewModels;
using Xunit;

namespace SistecreditoTEF.Tests.ViewModels;

/// <summary>
/// SE LE PREGUNTA A CREDINET POR TODOS LOS PLAZOS, NO POR UNA SELECCION.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTO FIJA
/// ─────────────────────────────────────────────────────────────────────────────
/// Los candidatos eran una lista escrita a mano:
///
///     { 1, 2, 3, 6, 9, 12, 18, 24 }
///
/// Y lo que la pantalla ofrece son los plazos que Credinet confirmo DE LOS QUE SE
/// LE PREGUNTARON. Un plazo que no estuviera en esa lista no aparecia nunca,
/// aunque Credinet lo aceptara.
///
/// Reportado desde produccion: la pantalla decia "Plazos que Sistecredito acepta
/// para este monto: 1, 2, 3, 6" y en Credinet estaban tambien el 4 y el 5. No
/// faltaba el techo —el 6 se ofrecia—: faltaban los numeros del medio.
///
/// El cajero perdia la venta, o la cerraba en un plazo que no era el que el
/// cliente podia pagar. De los dos, el segundo es peor.
/// </summary>
public class TodosLosPlazosSePreguntanTests
{
    /// <summary>
    /// LA PRUEBA QUE IMPORTA. El 4 y el 5 son los que faltaban en la caja; el
    /// resto estan por el mismo motivo, para que nadie vuelva a "optimizar" la
    /// lista quitando los que parezcan poco frecuentes.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(23)]
    public void Los_plazos_del_medio_tambien_se_preguntan(int meses) =>
        Assert.Contains(meses, SeleccionCuotasViewModel.CandidateMonths);

    /// <summary>
    /// Todos los plazos de 1 a [PlazoMaximo], sin huecos y sin repetidos. Escrito
    /// como una comprobacion de la LISTA ENTERA y no de unos pocos valores: la
    /// forma en que esto se rompio fue justamente que alguien eligiera cuales
    /// merecian estar.
    /// </summary>
    [Fact]
    public void La_lista_es_el_rango_completo_sin_huecos()
    {
        var plazos = SeleccionCuotasViewModel.CandidateMonths;

        Assert.Equal(SeleccionCuotasViewModel.PlazoMaximo, plazos.Length);
        Assert.Equal(plazos.Length, plazos.Distinct().Count());
        Assert.Equal(1, plazos.Min());
        Assert.Equal(SeleccionCuotasViewModel.PlazoMaximo, plazos.Max());

        for (var m = 1; m <= SeleccionCuotasViewModel.PlazoMaximo; m++)
            Assert.Contains(m, plazos);
    }

    /// <summary>
    /// Y van en orden. La pantalla los muestra tal cual llegan —"Plazos que
    /// Sistecredito acepta para este monto: …"— y una lista desordenada se lee
    /// como un error del sistema.
    /// </summary>
    [Fact]
    public void Van_en_orden_ascendente()
    {
        var plazos = SeleccionCuotasViewModel.CandidateMonths;

        Assert.Equal(plazos.OrderBy(m => m).ToArray(), plazos);
    }
}
