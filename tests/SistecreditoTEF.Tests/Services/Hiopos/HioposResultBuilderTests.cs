using SistecreditoTEF.Maui.Services.Hiopos;
using HioposResultCodes = SistecreditoTEF.Maui.Services.Hiopos.Hiopos;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services.Hiopos;

/// <summary>
/// Tests del HioposResultBuilder.
/// B2/B3/B4/B5/B6: verifica que el resultado respeta el wire format
/// exacto que HioPosCloud espera.
/// </summary>
public class HioposResultBuilderTests
{
    private readonly HioposResultBuilder _builder;

    public HioposResultBuilderTests()
    {
        _builder = new HioposResultBuilder(new ReceiptBuilder());
    }

    [Fact]
    public void BuildBehavior_retorna_todos_los_flags_de_capacidad()
    {
        var response = _builder.BuildBehavior(HioposActions.GetBehavior);

        Assert.Equal(HioposResultCodes.Result.OkValue, response.ResultCode);

        // Van en BoolExtras, no en StringExtras: HioPos las lee con
        // getBooleanExtra y como cadena se quedaba con el default de cada una.
        Assert.NotNull(response.BoolExtras);
        Assert.Contains(HioposExtras.SupportsCredit, response.BoolExtras!.Keys);
        Assert.Contains(HioposExtras.HasCustomParams, response.BoolExtras.Keys);
        Assert.Contains(HioposExtras.CanAudit, response.BoolExtras.Keys);
        Assert.Contains(HioposExtras.OnlyUseDocumentPath, response.BoolExtras.Keys);

        // Valores correctos segun HioposCapabilities (doc §3).
        Assert.True(response.BoolExtras[HioposExtras.SupportsCredit]);
        Assert.True(response.BoolExtras[HioposExtras.HasCustomParams]);
        Assert.True(response.BoolExtras[HioposExtras.CanAudit]);
        Assert.False(response.BoolExtras[HioposExtras.CanChargeCard]);
        Assert.False(response.BoolExtras[HioposExtras.CanPrint]);

        // QA C-2: este test estaba DESACTUALIZADO y era el unico que fallaba de
        // los 73. Esperaba OnlyUseDocumentPath=true, pero produccion lo cambio a
        // FALSE a proposito: con true la app tendria que leer el XML de la venta
        // desde una ruta de disco, y en Android 13+ el scoped storage lo impide
        // (Permission denied -> ActiveDocument quedaba null). Con false HioPos lo
        // manda inline en DocumentData. El cambio de produccion es correcto; el
        // test se quedo atras porque la suite no se podia ejecutar.
        Assert.False(response.BoolExtras[HioposExtras.OnlyUseDocumentPath]);
    }

    /// <summary>
    /// EL DEFECTO QUE DEJABA AL MODULO SIN CAPACIDADES.
    ///
    /// HioPos lee cada bandera con <c>getBooleanExtra</c>
    /// (ExternalModule.getNormalizedBooleanBehaviour, APK 15.9.0.0). Un extra de
    /// texto hace que se quede con el valor por defecto, sin error: el mismo
    /// defecto que ya habia pasado con <c>Version</c> y <c>getIntExtra</c>.
    ///
    /// El sintoma no era "no compila" sino "el POS ignora al modulo": con
    /// <c>supportsCashTransaction</c> en false, una entrada de caja se despachaba
    /// por la rama de VENTA y llegaba como <c>SALE</c> en vez de <c>CASH_IN</c>.
    /// </summary>
    [Fact]
    public void BuildBehavior_no_manda_ninguna_capacidad_como_texto()
    {
        var response = _builder.BuildBehavior(HioposActions.GetBehavior);

        Assert.Empty(response.StringExtras);
    }

    /// <summary>
    /// La bandera de la que depende que el recaudo entre por
    /// <c>executePaymentGatewayCashTransaction()</c>, que es la unica rama que
    /// arma el request con <c>TransactionType=CASH_IN</c>.
    /// </summary>
    [Fact]
    public void BuildBehavior_declara_que_atiende_transacciones_de_caja()
    {
        var response = _builder.BuildBehavior(HioposActions.GetBehavior);

        Assert.True(response.BoolExtras![HioposExtras.SupportsCashTransaction]);
    }

    [Fact]
    public void BuildBehavior_declara_exactamente_los_flags_del_contrato()
    {
        // QA M-14: el nombre anterior de este test decia "los 18 flags", igual que
        // el docstring de BuildBehavior y el comentario de MainActivity, pero
        // HioposCapabilities define 17 y se emiten 17. Nadie lo notaba porque no
        // se asertaba la cantidad. Ahora si: si alguien agrega o quita una
        // capacidad, el test lo dice.
        var response = _builder.BuildBehavior(HioposActions.GetBehavior);

        // Son 21: las 17 del manual mas cuatro que HioPos reconoce y no estaban
        // declaradas. Esas cuatro NO salen del manual de Cobro Electronico 4.0
        // —no aparecen ahi— sino del APK de HioPos instalado en el terminal, donde
        // viven en el mismo bloque de cadenas que las otras:
        //
        //   SupportsCashTransaction  SupportsSale  SupportsVoid  SupportOverPayment
        //
        // La que motivo el cambio es SupportsCashTransaction: es la unica pista
        // concreta hallada para el flujo de entrada de caja (HioPos tiene todo un
        // subsistema icg.android.cashTransaction). Ver la nota en [HioposExtras].
        Assert.Equal(21, response.BoolExtras!.Count);

        Assert.True(response.BoolExtras[HioposExtras.SupportsCashTransaction]);
        Assert.True(response.BoolExtras[HioposExtras.SupportsSale]);
        Assert.False(response.BoolExtras[HioposExtras.SupportsVoid]);
        Assert.False(response.BoolExtras[HioposExtras.SupportOverPayment]);
    }

    [Fact]
    public void BuildVersion_retorna_extra_Version_no_TransactionResult()
    {
        var response = _builder.BuildVersion(HioposActions.GetVersion, 7);

        // B4: debe usar HioposExtras.Version
        Assert.NotNull(response.IntExtras);
        Assert.True(response.IntExtras!.ContainsKey(HioposExtras.Version));
        Assert.Equal(7, response.IntExtras[HioposExtras.Version]);
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.TransactionResult));
    }

    [Fact]
    public void BuildVersion_manda_la_version_como_ENTERO_no_como_cadena()
    {
        // HioPos lee este extra con getIntExtra. Mientras se envio como cadena, se
        // quedaba con el default (-1) y pedia reinstalar el modulo en cada arranque:
        //
        //   W/Bundle: Key Version expected Integer but value was a
        //             java.lang.String. The default value -1 was returned.
        //
        // Este test es la barrera contra volver a mandarla como texto.
        var response = _builder.BuildVersion(HioposActions.GetVersion, 1);

        Assert.False(response.StringExtras.ContainsKey(HioposExtras.Version),
            "Version NO debe ir en los extras de cadena: HioPos la descarta.");
        Assert.Equal(1, response.IntExtras?[HioposExtras.Version]);
    }

    [Fact]
    public void BuildVersion_usa_el_valor_registrado_en_HioPosCloud()
    {
        var response = _builder.BuildVersion(
            HioposActions.GetVersion, HioposActions.ModuleVersion);

        Assert.Equal(1, response.IntExtras?[HioposExtras.Version]);
    }

    [Fact]
    public void BuildTransactionAccepted_usa_wire_format_mayusculas()
    {
        var response = _builder.BuildTransactionAccepted(
            merchantReceiptXml: "<Receipt/>",
            customerReceiptXml: "<Receipt/>",
            authorizationId: "credit-1");

        // B2: "ACCEPTED" en MAYUSCULAS, no "Accepted"
        Assert.Equal("ACCEPTED", response.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("credit-1", response.StringExtras[HioposExtras.AuthorizationId]);
        Assert.Equal("<Receipt/>", response.StringExtras[HioposExtras.MerchantReceipt]);
        Assert.Equal("<Receipt/>", response.StringExtras[HioposExtras.CustomerReceipt]);
        Assert.Equal("Sistecredito", response.StringExtras[HioposExtras.CardType]);
    }

    [Fact]
    public void BuildTransactionFailed_incluye_ErrorMessage_no_ErrorCode()
    {
        var response = _builder.BuildTransactionFailed("Cliente no encontrado");

        // B5: solo ErrorMessage + ErrorMessageTitle; sin ErrorCode
        Assert.Equal("FAILED", response.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("Cliente no encontrado", response.StringExtras[HioposExtras.ErrorMessage]);
        Assert.False(response.StringExtras.ContainsKey("ErrorCode"));
    }

    [Fact]
    public void BuildCustomParams_incluye_Logo_como_binario()
    {
        var logo = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var response = _builder.BuildCustomParams(HioposActions.GetCustomParams, logo);

        Assert.Equal("Sistecredito", response.StringExtras[HioposExtras.Name]);
        Assert.NotNull(response.BinaryExtras);
        Assert.Equal(logo, response.BinaryExtras![HioposExtras.Logo]);
    }

    [Fact]
    public void BuildCanceled_retorna_resultCode_canceled()
    {
        var response = _builder.BuildCanceled(HioposActions.ReadCard);

        Assert.Equal(HioposResultCodes.Result.CanceledValue, response.ResultCode);
    }

    // ------------------------------------------------------------------
    // REFUND de una venta en curso: se acepta para soltar la linea
    // ------------------------------------------------------------------

    /// <summary>
    /// El POS solo necesita el ACCEPTED para habilitar el desmarcado de la linea.
    /// </summary>
    [Fact]
    public void BuildPaymentLineRelease_responde_ACCEPTED_en_RESULT_OK()
    {
        var r = _builder.BuildPaymentLineRelease("REFUND", "8000000", "5686486347");

        Assert.Equal("ACCEPTED", r.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal(HioposResultCodes.Result.OkValue, r.ResultCode);
    }

    /// <summary>
    /// El importe se hace ECO tal cual vino. Viene en la escala del contrato —los dos
    /// ultimos digitos son decimales, $80.000 llega como "8000000"— y recalcularlo es
    /// una oportunidad gratuita de equivocarse en un factor de 100.
    /// </summary>
    [Fact]
    public void BuildPaymentLineRelease_hace_eco_del_importe_sin_reconvertirlo()
    {
        var r = _builder.BuildPaymentLineRelease("REFUND", "8000000", "ref-1");

        Assert.Equal("8000000", r.StringExtras[HioposExtras.Amount]);
    }

    /// <summary>
    /// La referencia devuelta es la MISMA de la operacion que se esta soltando, en
    /// AuthorizationId y en TransactionData: es con la que el POS cruza las dos al
    /// conciliar. Una referencia nueva rompe esa conciliacion.
    /// </summary>
    [Fact]
    public void BuildPaymentLineRelease_devuelve_la_referencia_original_en_los_dos_campos()
    {
        var r = _builder.BuildPaymentLineRelease("REFUND", "8000000", "5686486347");

        Assert.Equal("5686486347", r.StringExtras[HioposExtras.AuthorizationId]);
        Assert.Equal("5686486347", r.StringExtras[HioposExtras.TransactionData]);
    }

    [Theory]
    [InlineData("REFUND")]
    [InlineData("refund")]
    public void BuildPaymentLineRelease_hace_eco_literal_del_tipo(string tipo)
    {
        var r = _builder.BuildPaymentLineRelease(tipo, "8000000", "ref-1");

        Assert.Equal(tipo, r.StringExtras[HioposExtras.TransactionType]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildPaymentLineRelease_nunca_deja_el_tipo_vacio(string? tipo)
    {
        var r = _builder.BuildPaymentLineRelease(tipo, "8000000", "ref-1");

        Assert.Equal("REFUND", r.StringExtras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// Sin comprobantes: no se imprimio nada y no se movio un peso. Si se colaran,
    /// el POS sacaria el respaldo de una devolucion que no ocurrio.
    /// </summary>
    [Fact]
    public void BuildPaymentLineRelease_no_manda_comprobantes()
    {
        var r = _builder.BuildPaymentLineRelease("REFUND", "8000000", "ref-1");

        Assert.False(r.StringExtras.ContainsKey(HioposExtras.MerchantReceipt));
        Assert.False(r.StringExtras.ContainsKey(HioposExtras.CustomerReceipt));
        Assert.False(r.StringExtras.ContainsKey(HioposExtras.ModifyDocumentResult));
    }

    /// <summary>
    /// Las dos respuestas al MISMO intent tienen que diferir exactamente en el
    /// veredicto, no en el codigo de resultado: las dos van en RESULT_OK. Si alguna
    /// cayera en RESULT_CANCELED, el POS la leeria como "el cajero se salio".
    /// </summary>
    [Fact]
    public void Las_dos_respuestas_al_REFUND_van_en_RESULT_OK()
    {
        var suelta  = _builder.BuildPaymentLineRelease("REFUND", "8000000", "ref-1");
        var rechazo = _builder.BuildRefundNotSupported("REFUND");

        Assert.Equal(HioposResultCodes.Result.OkValue, suelta.ResultCode);
        Assert.Equal(HioposResultCodes.Result.OkValue, rechazo.ResultCode);

        Assert.Equal("ACCEPTED", suelta.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("FAILED",   rechazo.StringExtras[HioposExtras.TransactionResult]);
    }

    // ------------------------------------------------------------------
    // Nota de credito (REFUND): se rechaza
    // ------------------------------------------------------------------
    //
    // El sintoma que protegen estos tests: HioPos no mostraba NINGUN mensaje y el
    // cajero veia una ventana en blanco. Cada uno de los tres campos de abajo, si
    // se rompe, reproduce exactamente eso.

    /// <summary>
    /// El campo se llama <c>TransactionResult</c>. En la API TEF 4.0 no existe un
    /// extra <c>Result</c>: con ese nombre el POS no reconoce la respuesta como
    /// transaccion fallida, descarta el ErrorMessage y no muestra nada.
    /// </summary>
    [Fact]
    public void BuildRefundNotSupported_usa_TransactionResult_no_Result()
    {
        var response = _builder.BuildRefundNotSupported("REFUND");

        Assert.Equal("FAILED", response.StringExtras[HioposExtras.TransactionResult]);
        Assert.False(response.StringExtras.ContainsKey("Result"),
            "El extra se llama TransactionResult; 'Result' no existe en el contrato " +
            "y hace que HioPos no muestre el mensaje.");
    }

    /// <summary>
    /// RESULT_OK, no RESULT_CANCELED. El codigo de la Activity solo dice "te
    /// respondi"; el rechazo viaja en TransactionResult. Con RESULT_CANCELED el POS
    /// entiende "el cajero se salio" y vuelve a su pantalla sin mostrar nada.
    /// </summary>
    [Fact]
    public void BuildRefundNotSupported_responde_OK_no_canceled()
    {
        var response = _builder.BuildRefundNotSupported("REFUND");

        Assert.Equal(HioposResultCodes.Result.OkValue, response.ResultCode);
        Assert.NotEqual(HioposResultCodes.Result.CanceledValue, response.ResultCode);
    }

    /// <summary>
    /// El eco del TransactionType es obligatorio: si el POS recibe un tipo distinto
    /// del que pidio, no da la operacion por cerrada y RELANZA el Intent — se ve
    /// como si el modulo se abriera en bucle.
    /// </summary>
    [Theory]
    [InlineData("REFUND")]
    [InlineData("refund")]
    [InlineData("NEGATIVE_SALE")]
    public void BuildRefundNotSupported_hace_eco_literal_del_tipo(string tipo)
    {
        var response = _builder.BuildRefundNotSupported(tipo);

        Assert.Equal(tipo, response.StringExtras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// Si el POS no mandara el tipo, se responde "REFUND" en lugar de dejar el campo
    /// vacio: un TransactionType ausente tiene el mismo efecto que uno equivocado.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildRefundNotSupported_nunca_deja_el_tipo_vacio(string? tipo)
    {
        var response = _builder.BuildRefundNotSupported(tipo);

        Assert.Equal("REFUND", response.StringExtras[HioposExtras.TransactionType]);
    }

    [Fact]
    public void BuildRefundNotSupported_lleva_titulo_y_mensaje_para_el_cajero()
    {
        var response = _builder.BuildRefundNotSupported("REFUND");

        Assert.Equal("Forma de pago no valida",
            response.StringExtras[HioposExtras.ErrorMessageTitle]);
        Assert.Equal(
            "Sistecredito no admite notas de credito. Usa otro medio de pago.",
            response.StringExtras[HioposExtras.ErrorMessage]);
    }

    /// <summary>
    /// Nada de comprobantes ni de autorizacion: no hubo operacion. Si se colaran,
    /// HioPos imprimiria el respaldo de una nota de credito que nunca se aplico.
    /// </summary>
    [Fact]
    public void BuildRefundNotSupported_no_manda_comprobantes_ni_autorizacion()
    {
        var response = _builder.BuildRefundNotSupported("REFUND");

        Assert.False(response.StringExtras.ContainsKey(HioposExtras.MerchantReceipt));
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.CustomerReceipt));
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.AuthorizationId));
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.ModifyDocumentResult));
    }

    /// <summary>
    /// Coherencia con lo que declaramos en GET_BEHAVIOR: si algun dia se soportaran
    /// reversos, este rechazo deja de tener sentido y hay que quitarlo. El test lo
    /// deja atado para que no queden las dos cosas contradiciendose.
    /// </summary>
    [Fact]
    public void El_rechazo_es_coherente_con_las_capacidades_declaradas()
    {
        Assert.False(HioposCapabilities.SupportsTransactionVoid);
        Assert.False(HioposCapabilities.SupportsPartialRefund);
        Assert.False(HioposCapabilities.SupportsNegativeSales);
    }

    /// <summary>
    /// ExecuteVoidWhenAvailable tiene que quedarse en FALSE, y no es un detalle
    /// cosmetico: con esa bandera en true el POS manda VOID_TRANSACTION en lugar de
    /// REFUND para un abono del total en la Z actual. El rechazo de notas de credito
    /// vive en el caso REFUND, asi que dejaria de ejecutarse — y las notas de credito
    /// se empezarian a aceptar solas, sin mensaje y sin que nada falle a la vista.
    /// </summary>
    [Fact]
    public void ExecuteVoidWhenAvailable_debe_seguir_en_false()
    {
        Assert.False(HioposCapabilities.ExecuteVoidWhenAvailable,
            "Con true, los abonos llegan como VOID_TRANSACTION y el rechazo de notas " +
            "de credito (que vive en el caso REFUND) deja de correr.");
    }

    /// <summary>
    /// OnlyUseDocumentPath tiene que quedarse en FALSE: es lo que hace que el
    /// documento llegue inline en DocumentData. Con true habria que leer un archivo
    /// del disco, el scoped storage de Android 13+ lo impide, y sin documento no se
    /// puede distinguir un desmarcado de una nota de credito.
    /// </summary>
    [Fact]
    public void OnlyUseDocumentPath_debe_seguir_en_false()
    {
        Assert.False(HioposCapabilities.OnlyUseDocumentPath,
            "Con true no llega DocumentData inline y el REFUND no se puede clasificar.");
    }

    // ------------------------------------------------------------------
    // "Volver a HioPos": el cajero se sale sin cobrar
    // ------------------------------------------------------------------

    /// <summary>
    /// El detalle que importa de este test: RESULT_OK, no RESULT_CANCELED. Con
    /// RESULT_CANCELED y sin extras HioPos mostraba un error de modulo externo,
    /// porque para el POS eso es indistinguible de un modulo que se murio.
    /// </summary>
    [Fact]
    public void BuildTransactionCanceledByUser_responde_OK_no_canceled()
    {
        var response = _builder.BuildTransactionCanceledByUser();

        Assert.Equal(HioposResultCodes.Result.OkValue, response.ResultCode);
        Assert.NotEqual(HioposResultCodes.Result.CanceledValue, response.ResultCode);
        Assert.Equal(HioposActions.Transaction, response.Action);
    }

    /// <summary>
    /// FAILED y no UNKNOWN_RESULT: UNKNOWN_RESULT le dice al POS que el cobro pudo
    /// haber ocurrido, y por este camino se sale ANTES de llamar a Credinet.
    /// Confundirlos manda a conciliar un cobro que no existe.
    /// </summary>
    [Fact]
    public void BuildTransactionCanceledByUser_reporta_FAILED_no_UNKNOWN()
    {
        var response = _builder.BuildTransactionCanceledByUser();

        Assert.Equal("FAILED", response.StringExtras[HioposExtras.TransactionResult]);
    }

    /// <summary>
    /// El cartel que ve el cajero tiene que estar escrito para el: que paso y que
    /// puede hacer. Antes decia "Modulo cerrado", que describe nuestra
    /// implementacion y no le sirve a nadie en una caja.
    /// </summary>
    [Fact]
    public void BuildTransactionCanceledByUser_lleva_mensaje_para_el_cajero()
    {
        var response = _builder.BuildTransactionCanceledByUser();

        var titulo  = response.StringExtras[HioposExtras.ErrorMessageTitle];
        var mensaje = response.StringExtras[HioposExtras.ErrorMessage];

        Assert.Equal("Pago cancelado", titulo);
        Assert.False(string.IsNullOrWhiteSpace(mensaje));

        // Dice que NO se cobro —aca si lo sabemos con certeza, porque este camino
        // sale antes de llamar a Credinet— y ofrece la salida.
        Assert.Contains("sin realizar el cobro", mensaje!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("otro medio de pago", mensaje!, StringComparison.OrdinalIgnoreCase);

        // Y no le habla de modulos ni de nuestra estructura interna.
        Assert.DoesNotContain("modulo", (titulo! + mensaje!), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTransactionCanceledByUser_hace_eco_del_tipo_de_operacion()
    {
        var response = _builder.BuildTransactionCanceledByUser("SALE");

        Assert.Equal("SALE", response.StringExtras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// El eco NUNCA se omite, ni cuando no vino el tipo.
    ///
    /// Antes se omitia, y [HioposExit] lo lee de ActiveTransaction: con el estado ya
    /// limpio la respuesta salia sin TransactionType. Para el POS eso es igual de
    /// malo que un tipo equivocado —no da la operacion por cerrada— y el cajero veia
    /// el error generico de modulo externo justo despues de tocar "Volver a HioPos",
    /// que es exactamente el cartel que este camino existe para evitar.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTransactionCanceledByUser_nunca_omite_el_tipo(string? tipo)
    {
        var response = _builder.BuildTransactionCanceledByUser(tipo);

        Assert.True(response.StringExtras.ContainsKey(HioposExtras.TransactionType));
        Assert.Equal("SALE", response.StringExtras[HioposExtras.TransactionType]);
    }

    // ------------------------------------------------------------------
    // Fail-safe: un handler lanzo
    // ------------------------------------------------------------------

    /// <summary>
    /// Este camino devolvia RESULT_CANCELED sin extras, que es literalmente lo que
    /// HioPos muestra como "Error en modulo externo".
    /// </summary>
    [Fact]
    public void BuildUnexpectedFailure_responde_OK_con_FAILED_y_eco()
    {
        var r = _builder.BuildUnexpectedFailure("SALE");

        Assert.Equal(HioposResultCodes.Result.OkValue, r.ResultCode);
        Assert.Equal("FAILED", r.StringExtras[HioposExtras.TransactionResult]);
        Assert.Equal("SALE", r.StringExtras[HioposExtras.TransactionType]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildUnexpectedFailure_nunca_omite_el_tipo(string? tipo)
    {
        var r = _builder.BuildUnexpectedFailure(tipo);

        Assert.Equal("SALE", r.StringExtras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// A diferencia de "Volver a HioPos", aca la excepcion pudo ocurrir en cualquier
    /// punto —incluso despues de crear el credito— asi que el mensaje NO puede
    /// prometer que no se cobro. Tiene que mandar a verificar.
    /// </summary>
    [Fact]
    public void BuildUnexpectedFailure_no_promete_que_no_hubo_cobro()
    {
        var r = _builder.BuildUnexpectedFailure("SALE");
        var mensaje = r.StringExtras[HioposExtras.ErrorMessage]!;

        Assert.Contains("verificalo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no se realizo ningun cobro", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Toda respuesta de TRANSACTION lleva el eco del tipo, incluido el rechazo por
    /// configuracion invalida: es el campo sin el cual el POS no cierra la operacion.
    /// </summary>
    [Fact]
    public void BuildTransactionFailed_tambien_hace_eco_del_tipo()
    {
        var conTipo = _builder.BuildTransactionFailed("boom", "titulo", "REFUND");
        var sinTipo = _builder.BuildTransactionFailed("boom");

        Assert.Equal("REFUND", conTipo.StringExtras[HioposExtras.TransactionType]);
        Assert.Equal("SALE",   sinTipo.StringExtras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// No se devuelven comprobantes: no hubo cobro, asi que no hay nada que
    /// imprimir. Si se colaran, HioPos imprimiria un recibo de una venta que no
    /// existio.
    /// </summary>
    [Fact]
    public void BuildTransactionCanceledByUser_no_manda_comprobantes()
    {
        var response = _builder.BuildTransactionCanceledByUser("SALE");

        Assert.False(response.StringExtras.ContainsKey(HioposExtras.MerchantReceipt));
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.CustomerReceipt));
        Assert.False(response.StringExtras.ContainsKey(HioposExtras.AuthorizationId));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ENTRADA DE CAJA: EL IMPORTE DEL ABONO EN LA PANTALLA DEL POS
    // ══════════════════════════════════════════════════════════════════════════
    // El cajero llega por Caja > Entradas de caja, elige Sistecredito y escribe
    // "1" en importe solo para habilitar el boton. Al volver, el movimiento tiene
    // que quedar con el valor realmente abonado.
    //
    // El contrato de este flujo NO esta en el manual: se midio desensamblando el
    // HioPos del terminal (icg.android.start 15.9.0.0). En
    // CashTransactionActivity.onExternalModuleResult():
    //
    //     paymentMean.setAmount(response.getAmount());          // siempre
    //     if (type.equals("CASH_IN") || type.equals("CASH_OUT"))
    //     {
    //         paymentMean.setNetAmount(response.getAmount());    // el importe REAL
    //         controller.sendDocumentChange();
    //     }
    //
    // Amount es lo ENTREGADO y NetAmount lo APLICADO: el vuelto es la resta. Con
    // TransactionType=SALE solo corria el primero, y por eso el cajero veia
    // importe $1, entregado $99.900 y un cambio de $99.899.
    //
    // Los FixedPaymentMean* del manual no entran aca: los unicos que los leen son
    // TotalizationActivity, KioskSaleController y DocumentApiBase. El paquete
    // icg.android.cashTransaction no los menciona ni una vez.

    /// <summary>Respuesta de abono ya construida, antes de ajustarla al flujo.</summary>
    private static Dictionary<string, string?> ExtrasDeRespuestaTipicos() => new()
    {
        [HioposExtras.TransactionResult] = "ACCEPTED",
        [HioposExtras.TransactionType]   = "SALE",
        [HioposExtras.Amount]            = "9990000",
        [HioposExtras.TipAmount]         = "0",
        [HioposExtras.TaxAmount]         = "0",
        [HioposExtras.SurchargeAmount]   = "0",
        [HioposExtras.AuthorizationId]   = "ABC123"
    };

    /// <summary>
    /// LA LINEA QUE HACE TODO EL TRABAJO. Es la unica respuesta del modulo que no
    /// hace eco del TransactionType que pidio el POS, y sin ella HioPos registra el
    /// importe como plata entregada en vez de aplicarlo.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_responde_CASH_IN_y_no_el_tipo_que_pidio_el_POS()
    {
        var extras = ExtrasDeRespuestaTipicos();

        HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(extras, 9990000L);

        Assert.Equal("CASH_IN", extras[HioposExtras.TransactionType]);
    }

    /// <summary>
    /// El importe viaja en CENTAVOS y sin separadores: el manual lo dice con un
    /// ejemplo ("50€ : Amount = 5000"). Equivocarse aca es un factor de 100 sobre
    /// plata que entra al cajon.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_manda_el_importe_en_centavos_y_sin_separadores()
    {
        var extras = ExtrasDeRespuestaTipicos();

        // $99.900 en pesos colombianos, pasados por la unica conversion valida.
        HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(
            extras, SistecreditoTEF.Maui.Common.Money.ToCents(99_900m));

        Assert.Equal("9990000", extras[HioposExtras.Amount]);
    }

    /// <summary>
    /// De los 15 campos que lee la pantalla de total, la entrada de caja lee cinco.
    /// Los FixedPaymentMean* no estan entre ellos y ademas conviven mal con el
    /// Amount, que es el que este flujo si necesita.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_no_manda_el_medio_fijado_que_este_flujo_no_lee()
    {
        var extras = ExtrasDeRespuestaTipicos();
        extras[HioposExtras.FixedPaymentMeanId]     = "1";
        extras[HioposExtras.FixedPaymentMeanAmount] = "9990000";

        HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(extras, 9990000L);

        Assert.False(extras.ContainsKey(HioposExtras.FixedPaymentMeanId));
        Assert.False(extras.ContainsKey(HioposExtras.FixedPaymentMeanAmount));
    }

    /// <summary>
    /// Tip/Tax/Surcharge tampoco los lee este flujo. Se omiten para que la
    /// respuesta diga exactamente lo que la entrada de caja entiende.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_omite_los_modificadores_que_este_flujo_no_lee()
    {
        var extras = ExtrasDeRespuestaTipicos();

        HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(extras, 9990000L);

        Assert.False(extras.ContainsKey(HioposExtras.TipAmount));
        Assert.False(extras.ContainsKey(HioposExtras.TaxAmount));
        Assert.False(extras.ContainsKey(HioposExtras.SurchargeAmount));
    }

    /// <summary>
    /// Los otros tres campos que la entrada de caja SI lee —TransactionResult y
    /// TransactionData— y todo lo demas de la respuesta quedan intactos.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_no_toca_el_resto_de_la_respuesta()
    {
        var extras = ExtrasDeRespuestaTipicos();

        HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja(extras, 9990000L);

        Assert.Equal("ACCEPTED", extras[HioposExtras.TransactionResult]);
        Assert.Equal("ABC123",   extras[HioposExtras.AuthorizationId]);
    }

    /// <summary>
    /// El valor no se escribe a mano en cada llamada: un "CASH-IN" o un "CashIn"
    /// serian descartados por el equals() de HioPos sin ningun error visible.
    /// </summary>
    [Fact]
    public void EntradaDeCaja_usa_la_constante_del_contrato()
    {
        Assert.Equal("CASH_IN", HioposTransactionTypes.CashIn);
        Assert.Equal("SALE",    HioposTransactionTypes.Sale);
    }
}
