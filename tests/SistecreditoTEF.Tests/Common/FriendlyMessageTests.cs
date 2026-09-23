using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// El cajero nunca puede leer un codigo ni un nombre en ingles.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// LOS DOS DEFECTOS QUE ESTO PROTEGE
/// ─────────────────────────────────────────────────────────────────────────────────
/// 1. En pantalla aparecia texto crudo del proveedor: "CREDINET: CreditsNotFound".
///    Dos ViewModels asignaban [ApiError.UserMessage] directo, sin traducir.
///
/// 2. Peor: DOS codigos estaban mapeados al mensaje equivocado y mandaban al cajero
///    a arreglar lo que no era. Se corrigieron contra la tabla oficial del manual
///    (M-SCL-03 v05, §4.2.4):
///
///      225 StoreNotFound       decia "el documento no es valido"  -> es la TIENDA
///      231 CreditsNotFound     decia "el monto excede el limite"  -> no hay creditos
///
///    El de cupo insuficiente es el 221, que no estaba mapeado.
/// </summary>
public class FriendlyMessageTests
{
    private static string Mensaje(int code, string nombre = "") =>
        FriendlyMessage.FromApiError(new ApiError.Business(code, nombre));

    // ------------------------------------------------------------------
    // Los dos mapeos que estaban mal
    // ------------------------------------------------------------------

    /// <summary>
    /// 231 es CreditsNotFound. Decirle "prueba con un monto menor" a un cliente que
    /// no tiene creditos activos manda al cajero a cambiar un numero que no tiene
    /// nada que ver.
    /// </summary>
    [Fact]
    public void El_231_habla_de_creditos_no_de_monto()
    {
        var m = Mensaje(231, "CreditsNotFound");

        Assert.Contains("no tiene creditos activos", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monto menor", m, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 225 es StoreNotFound: configuracion del POS. Decirle que el documento no es
    /// valido hacia que reescribiera la cedula del cliente para siempre.
    /// </summary>
    [Fact]
    public void El_225_habla_de_la_tienda_no_del_documento()
    {
        var m = Mensaje(225, "StoreNotFound");

        Assert.Contains("tienda", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documento ingresado no es valido", m, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El de cupo es el 221, y ahora si esta mapeado.</summary>
    [Fact]
    public void El_221_es_el_de_cupo_insuficiente()
    {
        var m = Mensaje(221, "NotAvailableCreditLimit");

        Assert.Contains("cupo", m, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // La tabla completa: nada en ingles, nada de codigos
    // ------------------------------------------------------------------

    public static TheoryData<int, string> CodigosDelManual() => new()
    {
        { 220, "InvalidAmountCredit" },
        { 221, "NotAvailableCreditLimit" },
        { 222, "MonthsNumberNotValid" },
        { 223, "RequestValuesInvalid" },
        { 224, "CustomerNotFound" },
        { 225, "StoreNotFound" },
        { 226, "CustomerProfileInvalid" },
        { 227, "SourceNotFound" },
        { 228, "AuthMethodNotFound" },
        { 229, "TokenIsNotValid" },
        { 230, "TokenAlreadyUsed" },
        { 231, "CreditsNotFound" },
        { 232, "CustomerNotActive" },
        { 233, "CreditNotActive" },
        { 245, "CustomerIsDefaulter" },
        { 555, "NotControlledException" },
        { 1104, "NoOfferAvailable" }
    };

    [Theory]
    [MemberData(nameof(CodigosDelManual))]
    public void Ningun_codigo_del_manual_se_muestra_en_ingles(int code, string nombre)
    {
        var m = Mensaje(code, nombre);

        Assert.False(string.IsNullOrWhiteSpace(m));

        // Ni el nombre tecnico del error...
        Assert.DoesNotContain(nombre, m, StringComparison.OrdinalIgnoreCase);
        // ...ni el numero del codigo, que al cajero no le dice nada.
        Assert.DoesNotContain(code.ToString(), m);
    }

    /// <summary>
    /// Un codigo que TODAVIA no conocemos tampoco puede mostrar ingles: la red de
    /// seguridad traduce por el nombre del mensaje.
    /// </summary>
    [Theory]
    [InlineData("CreditsNotFound")]
    [InlineData("MonthsNumberNotValid")]
    [InlineData("CustomerIsDefaulter")]
    [InlineData("StoreNotFound")]
    [InlineData("CustomerNotActive")]
    [InlineData("CreditNotActive")]
    [InlineData("NotControlledException")]
    [InlineData("CustomerProfileInvalid")]
    [InlineData("NotAvailableCreditLimit")]
    [InlineData("TokenAlreadyUsed")]
    [InlineData("CustomerNotFound")]
    public void Un_codigo_desconocido_se_traduce_por_el_nombre(string nombre)
    {
        // 9999 no esta mapeado a proposito: fuerza el camino de la traduccion por texto.
        var m = Mensaje(9999, nombre);

        Assert.DoesNotContain(nombre, m, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// REP-E-003 es un campo que falta en NUESTRA peticion. No es algo que el cajero
    /// pueda arreglar, asi que el mensaje no lo manda a revisar los datos del cliente.
    /// </summary>
    [Fact]
    public void El_REP_E_003_no_culpa_al_cajero_ni_al_cliente()
    {
        var m = FriendlyMessage.FromApiError(
            new ApiError.Business(3, "[REP-E-003] El campo UserName es obligatorio en la peticion"));

        Assert.Contains("sistemas", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserName", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REP-E-003", m, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // Nunca se filtra el prefijo del proveedor
    // ------------------------------------------------------------------

    /// <summary>
    /// El sintoma exacto que se vio en la terminal: "CREDINET: CreditsNotFound".
    /// </summary>
    [Theory]
    [InlineData("CREDINET: CreditsNotFound")]
    [InlineData("HTTP 400 - algo raro")]
    [InlineData("228 - AuthMethodNotFound")]
    public void Nunca_se_muestra_el_prefijo_del_proveedor(string crudo)
    {
        var m = FriendlyMessage.FromApiError(new ApiError.Business(9999, crudo));

        Assert.DoesNotContain("CREDINET", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP", m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_error_de_red_dice_que_hacer()
    {
        var m = FriendlyMessage.FromApiError(
            new ApiError.Network(new HttpRequestException("boom")));

        Assert.Contains("conexion", m, StringComparison.OrdinalIgnoreCase);
    }
}
