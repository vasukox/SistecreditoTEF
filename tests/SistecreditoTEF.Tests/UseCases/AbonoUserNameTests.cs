using SistecreditoTEF.Maui.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.UseCases;

/// <summary>
/// El nombre del cajero NUNCA puede quedar vacio: es el <c>userName</c> del abono.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// EL DEFECTO QUE ESTE ARCHIVO PROTEGE
/// ─────────────────────────────────────────────────────────────────────────────────
/// En la terminal, TODOS los abonos empezaron a fallar con:
///
///     HTTP 400  [REP-E-003] El campo UserName es obligatorio en la peticion
///               POST https://api.credinet.co/pos/payCredit
///
/// La caja quedo sin poder recaudar, y el error parecia del proveedor.
///
/// La causa era nuestra y estaba en un <c>??</c>. Desde el rediseño del ingreso, un
/// cajero se da de alta con USUARIO obligatorio y NOMBRE opcional, asi que un cajero
/// creado solo con usuario tiene <c>Nombre = ""</c>. Y "" no es null:
///
///     sesion.Actual?.Nombre ?? ExtractSellerName(...) ?? "Cajero Permoda"
///     //                    ↑ nunca se dispara con cadena vacia
///
/// El nombre vacio atravesaba los tres respaldos y llegaba al POST. La correccion es
/// leer [Cajero.NombreVisible] y filtrar por BLANCO, no por null.
///
/// La otra mitad de la defensa —que el servicio nunca mande un userName vacio a
/// Credinet— se prueba en [AbonoIdempotenciaTests].
/// </summary>
public class AbonoUserNameTests
{
    /// <summary>
    /// Este es el caso exacto de la terminal: el cajero "felipe" existia solo con
    /// usuario, sin nombre cargado.
    /// </summary>
    [Fact]
    public void Un_cajero_sin_nombre_igual_tiene_nombre_visible()
    {
        var cajero = CajeroDe(usuario: "felipe", nombre: "");

        Assert.False(string.IsNullOrWhiteSpace(cajero.NombreVisible));
        Assert.Equal("felipe", cajero.NombreVisible);
    }

    [Fact]
    public void Un_cajero_con_nombre_en_blanco_cae_al_usuario()
    {
        var cajero = CajeroDe(usuario: "jperez", nombre: "   ");

        Assert.Equal("jperez", cajero.NombreVisible);
    }

    [Fact]
    public void Un_cajero_con_nombre_lo_conserva()
    {
        var cajero = CajeroDe(usuario: "jperez", nombre: "Juana Perez");

        Assert.Equal("Juana Perez", cajero.NombreVisible);
    }

    /// <summary>
    /// La traza de auditoria tenia el mismo <c>??</c> sobre <c>Nombre</c>, asi que
    /// habria quedado vacia para el mismo cajero.
    /// </summary>
    [Fact]
    public void La_traza_de_auditoria_no_queda_vacia_con_un_cajero_sin_nombre()
    {
        var sesion = new SesionCajero();
        sesion.Iniciar(CajeroDe(usuario: "felipe", nombre: ""));

        Assert.Equal("felipe", sesion.NombreParaAuditoria);
    }

    [Fact]
    public void La_traza_de_auditoria_sin_sesion_dice_que_no_hay_cajero()
    {
        var sesion = new SesionCajero();

        Assert.False(string.IsNullOrWhiteSpace(sesion.NombreParaAuditoria));
    }

    private static Cajero CajeroDe(string usuario, string nombre) =>
        new(Id: "id-1", Usuario: usuario, Nombre: nombre,
            ClaveHash: "hash", Activo: true, CreadoEn: DateTime.UtcNow);
}
