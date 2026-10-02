namespace SistecreditoTEF.Maui.Common;

/// <summary>
/// COMO SE MUESTRA, EN PANTALLA, CON QUE TIENDA OPERA ESTA CAJA.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POR QUE ESTO EXISTE
/// ─────────────────────────────────────────────────────────────────────────────
/// El modulo se puede instalar en 76 tiendas y todas se ven IGUAL: mismo icono,
/// mismo nombre, misma pantalla. Lo unico que las distingue es el StoreId, que
/// llega por CloudLicense y —antes— no se mostraba en ningun lado.
///
/// El modo de fallo era el mas caro del modulo y no dejaba ninguna senal: una caja
/// de Suba sin provisionar tomaba el StoreId que traia horneado el APK (el de la
/// 037) y REPORTABA SUS CREDITOS A LA 037. La venta salia bien, el cobro salia
/// bien, y el error se descubria conciliando en la plataforma de Sistecredito
/// semanas despues.
///
/// Ponerlo a la vista convierte eso en algo que se nota en el momento: el
/// instalador compara lo que ve con la hoja STOREID-SISTECREDITO, y si no
/// corresponde a esa tienda, lo sabe antes de cobrar la primera venta.
///
/// ESTO ES DIAGNOSTICO, NO UNA BARRIERA
/// La barrera de rechazar la venta esta en [ApiConfig.Validate]. Esta clase solo
/// describe. Describir en pantalla no debe romper nunca el arranque: una caja que
/// no puede mostrar su tienda tiene que poder seguir operando igual de visible que
/// antes, no peor.
///
/// SIN DEPENDENCIA DE ApiConfig A PROPOSITO
/// Recibe texto plano. Asi el formateo —que es la parte con reglas— se prueba en
/// xUnit sin montar nada, y el servicio que lee la configuracion queda aparte.
/// </summary>
public static class TextoDeTienda
{
    /// <summary>
    /// Nombre de la tienda, o un marcador si no vino. Nunca cadena vacia: un pie
    /// de pantalla en blanco se lee como "no hay nada", no como "no hay nombre".
    /// </summary>
    public static string Nombre(string? storeName)
    {
        var v = storeName?.Trim();
        return string.IsNullOrEmpty(v) ? "(sin nombre de tienda)" : v;
    }

    /// <summary>
    /// El StoreId tal cual, para cotejarlo con la hoja. Sin recortar: el valor
    /// recortado no sirve para comparar contra los 24 caracteres de la hoja, y el
    /// proposito de este renglon es justamente comparar.
    ///
    /// En el log si se recorta —ver [ApiConfigProvider]— porque logcat lo lee
    /// cualquiera con un cable USB. En pantalla no: la pantalla la ve el personal
    /// de la tienda.
    /// </summary>
    public static string StoreId(string? storeId)
    {
        var v = storeId?.Trim();
        return string.IsNullOrEmpty(v) ? "(sin StoreId)" : v;
    }

    /// <summary>
    /// Linea de una fila para el pie de pantalla: nombre y StoreId juntos.
    ///
    /// "nombre · storeId" y no al reves: el nombre lo reconoce el instalador de
    /// un vistazo, y el StoreId es el que se coteja.
    /// </summary>
    public static string Linea(string? storeName, string? storeId) =>
        $"{Nombre(storeName)}  ·  {StoreId(storeId)}";

    /// <summary>
    /// Aviso cuando todavia no se eligio la tienda. Vacio cuando ya esta, para que
    /// la pantalla pueda enlazarlo tal cual sin código de "ocultar si vacio".
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// POR QUE ESTE TEXTO CAMBIO
    /// ─────────────────────────────────────────────────────────────────────────
    /// Decia "ESTA CAJA NO ESTA PROVISIONADA COMO TIENDA… no cobre hasta que
    /// sistemas lo habilite". Describia un estado y no dejaba salida: el
    /// instalador leia que estaba bloqueado por algo que no dependia de el.
    ///
    /// Ahora la tienda se elige en el propio terminal, asi que el aviso es una
    /// INSTRUCCION y no un diagnostico. Es la diferencia entre "no se puede" y
    /// "toque aqui".
    ///
    /// El mismo texto en los dos ambientes: elegir la tienda es obligatorio
    /// tambien en pruebas. Si en sandbox se pudiera vender sin elegirla, el paso
    /// se descubriria recien en produccion, con la caja ya instalada.
    /// </summary>
    public static string Aviso(string? storeId, bool esProduccion)
    {
        if (!string.IsNullOrWhiteSpace(storeId)) return string.Empty;

        return "Falta elegir la tienda de esta caja. Sin eso no se pueden registrar "
             + "créditos. Toque «Elegir la tienda de esta caja» y búsquela por código "
             + "o por nombre.";
    }
}
