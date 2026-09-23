namespace SistecreditoTEF.Maui.Services.Hiopos;

/// <summary>
/// POCO inmutable que representa el Intent de respuesta a HioPosCloud.
/// Se construye con [HioposResultBuilder] y se materializa en
/// [Android.Content.Intent] dentro de [MainActivity].
///
/// Mantener este tipo libre de dependencias Android permite:
///   - Tests xUnit puros (sin emulador).
///   - Que la logica de "que responder" viva en C# normal.
/// </summary>
public sealed record HioposResponse(
    string Action,
    IReadOnlyDictionary<string, string?> StringExtras,
    IReadOnlyDictionary<string, byte[]>? BinaryExtras = null,
    int? ResultCode = null,
    // Extras que HioPos lee como ENTERO, no como cadena.
    //
    // (Comentario normal y no /// a proposito: un parametro posicional de un record
    // no admite doc-comment, y como /// el compilador lo descartaba con CS1587, o
    // sea que esta explicacion no llegaba a la documentacion NI dejaba de estorbar.)
    //
    // ─────────────────────────────────────────────────────────────────────────
    // POR QUE EXISTE ESTE TERCER DICCIONARIO
    // ─────────────────────────────────────────────────────────────────────────
    // Todo se enviaba como string, y para el extra "Version" eso rompia el
    // handshake en silencio. HioPos lo lee con getIntExtra, asi que al recibir un
    // String se queda con el valor por defecto. Capturado en logcat del terminal:
    //
    //   W/Bundle: Key Version expected Integer but value was a java.lang.String.
    //             The default value -1 was returned.
    //             at icg.android.start.StartActivity.onActivityResult(...)
    //
    // HioPos concluia que el modulo estaba en la version -1, nunca coincidia con
    // la registrada en HioPosCloud, y ofrecia reinstalar el modulo en CADA
    // arranque. El valor que mandabamos era irrelevante: "1", "1.0" y "1.0.0"
    // fallaban igual porque el problema era el TIPO, no el contenido.
    //
    // Va al final del record a proposito: agregarlo antes de ResultCode habria
    // cambiado el orden posicional de los parametros existentes.
    IReadOnlyDictionary<string, int>? IntExtras = null,

    // Extras que HioPos lee como BOOLEAN. Son las capacidades de GET_BEHAVIOR, y
    // el motivo de este cuarto diccionario es el mismo defecto que el de IntExtras,
    // repetido con otro tipo.
    //
    // ─────────────────────────────────────────────────────────────────────────
    // LAS 21 CAPACIDADES SE MANDABAN COMO CADENA Y NO LLEGABA NINGUNA
    // ─────────────────────────────────────────────────────────────────────────
    // Desensamblado de icg.android.start 15.9.0.0,
    // ExternalModule.getNormalizedBooleanBehaviour():
    //
    //     for (String key : intent.getExtras().keySet())
    //         if (key.equalsIgnoreCase(nombre))
    //             return intent.getBooleanExtra(key, porDefecto);
    //
    // getBooleanExtra sobre un extra String devuelve el DEFAULT. Mandabamos
    // "true"/"false" como texto, asi que HioPos se quedaba con el valor por
    // defecto de cada bandera y el modulo quedaba declarado como si no supiera
    // hacer nada.
    //
    // Consecuencia concreta y medida: con supportsCashTransaction en false,
    // CashTransactionActivity nunca llama a executePaymentGatewayCashTransaction()
    // —que es la que arma el request con TransactionType=CASH_IN— y manda el
    // recaudo por la rama de venta. Por eso el modulo recibia SALE en una entrada
    // de caja.
    //
    // La comparacion de nombres es equalsIgnoreCase, asi que las claves que ya
    // usabamos estan bien: lo unico que estaba mal era el TIPO.
    IReadOnlyDictionary<string, bool>? BoolExtras = null);
