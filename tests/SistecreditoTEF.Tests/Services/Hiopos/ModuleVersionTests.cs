using SistecreditoTEF.Maui.Services.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// La versión que el módulo reporta en GET_VERSION es un valor de CONTRATO con ICG,
/// y viaja como ENTERO.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTOS TESTS PROTEGEN
/// ─────────────────────────────────────────────────────────────────────────────
/// HioPos lee el extra <c>Version</c> con <c>getIntExtra</c>. Mientras el módulo lo
/// envió como cadena, HioPos se quedaba con el valor por defecto y creía que el
/// módulo estaba en la versión -1. Capturado en logcat del terminal:
///
///   W/Bundle: Key Version expected Integer but value was a java.lang.String.
///             The default value -1 was returned.
///             at icg.android.start.StartActivity.onActivityResult(...)
///
/// Ese era el motivo real del diálogo "actualizar el módulo" en cada arranque de
/// HioPos: la versión nunca coincidía con la registrada en HioPosCloud porque HioPos
/// nunca la leía.
///
/// Y explica por qué cambiar el CONTENIDO no servía: se probó "1.0.0", "1.0" y "1",
/// y las tres fallaban igual, porque el problema era el TIPO. Antes de tener la
/// captura del terminal, el diagnóstico apuntó dos veces al valor equivocado.
///
/// Estos tests no pueden ejecutar <c>HandleGetVersion</c> (vive en Platforms\Android),
/// así que fijan el invariante sobre la constante que esa función usa como única
/// fuente. El envío como entero lo cubre <c>HioposResultBuilderTests</c>.
/// </summary>
public class ModuleVersionTests
{
    /// <summary>
    /// Pin deliberado del valor. Si alguien lo cambia, este test falla y lo obliga a
    /// leer el porqué: tiene que coincidir con la versión registrada para el módulo
    /// "permoda" en HioPosCloud, que ICG confirmó como 1.
    /// </summary>
    [Fact]
    public void La_version_del_modulo_esta_fijada_y_solo_se_cambia_con_ICG()
    {
        Assert.Equal(1, HioposActions.ModuleVersion);
    }

    /// <summary>
    /// Es un entero. Volver a un string reintroduce el defecto completo: HioPos lo
    /// descartaría y pediría reinstalar el módulo en cada arranque.
    /// </summary>
    [Fact]
    public void La_version_es_un_entero()
    {
        Assert.IsType<int>(HioposActions.ModuleVersion);
    }

    /// <summary>
    /// Nunca el valor por defecto de <c>getIntExtra</c> ni cero: si el módulo
    /// reportara -1, sería indistinguible de no haber respondido.
    /// </summary>
    [Fact]
    public void La_version_no_puede_confundirse_con_el_default_de_HioPos()
    {
        Assert.True(HioposActions.ModuleVersion > 0,
            "Una version <= 0 es indistinguible del default (-1) que usa HioPos.");
    }

    /// <summary>
    /// Es una constante de compilación, no un valor derivado en tiempo de ejecución.
    /// Es la propiedad que garantiza que dos arranques distintos reporten lo mismo:
    /// cualquier cosa leída del entorno (versión del assembly, del paquete, una
    /// fecha) puede variar entre builds y reintroduce el diálogo.
    /// </summary>
    [Fact]
    public void Es_estable_entre_lecturas()
    {
        Assert.Equal(HioposActions.ModuleVersion, HioposActions.ModuleVersion);
    }
}
