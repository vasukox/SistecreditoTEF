# 04 · Integración con HioPosCloud (ICG)

> **Documento propietario de los identificadores del módulo y del contrato con el POS.**

Referencia del proveedor: *"API de desarrollo de un Módulo de Cobro Electrónico para
HioPosCloud"* (ICG Software).

---

## 1. Identidad del módulo

| Concepto | Valor | Dónde se define |
|---|---|---|
| **Package Name** (Android) | `com.permoda.sistecreditotef` | `SistecreditoTEF.Maui.csproj` → `ApplicationId` |
| **APK Name** (ICG) | `permoda` | `HioposConstants.cs` → `HioposActions.ApkName` |
| **Versión de contrato** (`GET_VERSION`) | `1` (entero) | `HioposConstants.cs` → `HioposActions.ModuleVersion` |
| Nombre visible | `Sistecredito` | `ApplicationTitle`, `android:label`, `GET_CUSTOM_PARAMS` |
| `versionName` / `versionCode` | `1.0.0` / `1` | `ApplicationDisplayVersion` / `ApplicationVersion` |
| Plataforma mínima / objetivo | `minSdk 24` / `targetSdk 36` | `SupportedOSPlatformVersion` |
| Arquitecturas nativas | `arm64-v8a`, `x86_64` | `AndroidSupportedAbis` |
| Prefijo de acciones | `icg.actions.electronicpayment.permoda.` | Derivado del APK Name |
| Acción de auditoría | `icg.actions.externalApi.AUDIT` | `HioposActions.ExternalAudit` |

### 1.1 El APK Name es la llave de enrutamiento

HioPos despacha los Intents con el APK Name **embebido en la acción**. Si el valor de la
constante no coincide con el alta en HioPosCloud, el módulo nunca recibe la acción: el POS espera
hasta agotar su tiempo, interpreta que el módulo no respondió y **cierra la venta sin cobrar**.

Está en un solo lugar y las once acciones se derivan de él. Cambiarlo es una línea, pero
**solo debe cambiarse cuando ICG confirme el alta con el nombre nuevo**.

> **Convivencia con otros módulos:** otro módulo del mismo cliente
> (`com.permoda.tefogloba`) declara las **mismas** acciones
> `icg.actions.electronicpayment.permoda.*`. Con los dos instalados, cuál atiende el Intent lo
> resuelve Android de forma impredecible. En una terminal solo puede estar uno de los dos.

### 1.2 Por qué la versión de contrato es un entero fijo

HioPos lee la versión con `getIntExtra` y la compara contra lo que tiene registrado. Dos
consecuencias que se verificaron en terminal:

- Enviada como cadena, HioPos se queda con su valor por defecto (`-1`) y cree que el módulo está
  desactualizado. En el registro del sistema aparece
  `Key Version expected Integer but value was a java.lang.String`.
- Derivada del `versionName` del APK, cambia en cada publicación y el POS ofrece "actualizar el
  módulo" en **cada arranque**, sin poder resolverlo nunca: el módulo se instala de forma
  lateral, HioPos no tiene de dónde descargarlo.

Por eso es un entero, fijo, coordinado con ICG e independiente del `versionCode` y del
`versionName`, que pueden subir libremente.

---

## 2. Las once acciones

Prefijo `icg.actions.electronicpayment.permoda.` más:

| Acción | Qué hace el módulo |
|---|---|
| `INITIALIZE` | Persiste el token de sesión del POS, interpreta y guarda los parámetros de CloudLicense, reconstruye la configuración y lanza el mantenimiento de la base local |
| `FINALIZE` | Limpia el token de sesión |
| `GET_VERSION` | Devuelve la versión de contrato (§1.2) |
| `GET_BEHAVIOR` | Declara sus capacidades (§3) |
| `GET_CUSTOM_PARAMS` | Devuelve el nombre visible y el logotipo del medio de pago |
| `GET_PRINT_INFO` | Devuelve información de impresión |
| **`TRANSACTION`** | Inicia la operación: interpreta los extras, lee el documento de venta si existe, guarda el estado y navega a la pantalla correspondiente |
| `SHOW_SETUP_SCREEN` | Pantalla de configuración (respuesta correcta sin contenido) |
| `READ_CARD` · `CHARGE_CARD` · `GET_CARD_DATA` | No aplican a un medio de crédito: responde *cancelado* |

Se declaran con atributos `[IntentFilter]` en `Platforms/Android/MainActivity.cs`, con
`LaunchMode = SingleTask` y `Exported = true`. La actividad declara además el filtro de
lanzador, que habilita el modo de abono autónomo.

### 2.1 Por qué el Intent se procesa en `OnResume`

El Intent se **guarda** en `OnCreate` / `OnNewIntent` y se **procesa en `OnResume`**, porque solo
entonces el contenedor de dependencias de MAUI está garantizado. Procesarlo antes falla en
arranque en frío.

La única excepción es el **contexto de arranque**, que se registra antes de `base.OnCreate`
porque la pantalla raíz del Shell se construye ahí y necesita saber si la aplicación la abrió
HioPos (ver [`02-Arquitectura.md`](02-Arquitectura.md) §8).

### 2.2 Nunca dejar al POS esperando

Cualquier excepción que escape de un manejador se captura y se responde al POS con una
cancelación de la acción. Una actividad que se cae se muestra en caja como "error en módulo
externo" y deja la venta en un estado ambiguo.

---

## 3. Capacidades declaradas (`GET_BEHAVIOR`)

El módulo emite **17 flags**. Definidas en `HioposConstants.cs → HioposCapabilities`:

| Flag | Valor | Flag | Valor |
|---|---|---|---|
| `SupportsCredit` | **true** | `SupportsDebit` | false |
| `HasCustomParams` | **true** | `SupportsEBTFoodstamp` | false |
| `canAudit` | **true** | `SupportsTransactionVoid` | false |
| `CanPrint` | false | `SupportsTransactionQuery` | false |
| `ReadCardFromApi` | false | `SupportsNegativeSales` | false |
| `CanChargeCard` | false | `SupportsPartialRefund` | false |
| `OnlyUseDocumentPath` | false | `SupportsBatchClose` | false |
| `ExecuteVoidWhenAvailable` | false | `SupportsTipAdjustment` | false |
| `SaveLoyaltyCardNum` | false | | |

Dos valores merecen justificación:

- **`CanPrint = false`.** En una venta imprime el POS, a partir del XML de comprobante que el
  módulo devuelve. La impresión local existe solo para los recaudos sin documento
  (ver [`06-Flujos-Funcionales.md`](06-Flujos-Funcionales.md)).
- **`OnlyUseDocumentPath = false`.** HioPos envía el documento **en línea** en el extra
  `DocumentData`. Con `true`, el documento llega como ruta de archivo y en Android 13 o superior
  el almacenamiento delimitado impide leerla: el documento quedaba nulo. Según la documentación
  del proveedor, la ruta solo hace falta si el XML supera 1 MB, lo que una factura normal no
  alcanza. Si llegara por ruta, el módulo también sabe leerla.

---

## 4. La operación (`TRANSACTION`)

### 4.1 Extras de entrada relevantes

`TransactionType`, `TenderType`, `CurrencyISO`, `Amount`, `TipAmount`, `TaxAmount`, `TaxDetail`,
`TransactionId`, `TransactionData`, `ReceiptPrinterColumns`, `ShopData`, `SellerData`,
`DocumentData`, `DocumentPath`, `IsAdvancedPayment`, `OverPaymentType`, `SurchargeAmount`.

Los nombres exactos viven en `HioposExtras` y se interpretan en `HioposIntentParser`. Al recibir
la acción, el módulo registra una descripción de los extras con los datos personales
enmascarados: sin eso no había forma de saber qué campo distingue cada tipo de operación.

### 4.2 Cómo se distingue una venta de un recaudo

El POS usa la **misma acción** para una venta y para una entrada de caja, y en ambos casos envía
`TransactionType=SALE`. Comparación capturada en terminal:

```
venta            TransactionType=SALE  TenderType=CREDIT  DocumentData=(11540 caracteres)
entrada de caja  TransactionType=SALE  TenderType=(vacío) DocumentData ausente
```

**La presencia del documento de venta es el único indicador confiable.** Sin documento
(`DocumentData` y `DocumentPath` vacíos) es un recaudo y el módulo enruta a la lista de créditos
activos; con documento es una venta y se respeta el `TransactionType` recibido.

`IsAdvancedPayment` y `OverPaymentType` valen lo mismo en ambos casos y no sirven para decidir.

### 4.3 Los `REFUND` se rechazan

Credinet no soporta reversos. Un `TransactionType=REFUND` es una nota de crédito y el módulo la
rechaza de entrada, con un resultado fallido explicativo para el cajero y un evento de auditoría.
No se enruta al flujo de recaudo: hacerlo abriría la lista de créditos del cliente ante una nota
de crédito.

### 4.4 Respuesta al POS

Se construye en `HioposResultBuilder` y se entrega mediante `ITransactionResultHandler`, que en
Android hace `setResult` y cierra la actividad.

| Extra | Contenido |
|---|---|
| `TransactionResult` | `ACCEPTED` · `FAILED` · `UNKNOWN_RESULT` |
| `TransactionType` | **El mismo que pidió el POS.** Responder otro tipo hace que HioPos relance la operación con un identificador nuevo en lugar de darla por cerrada |
| `Amount` | Importe realmente cobrado |
| `TipAmount` · `TaxAmount` · `SurchargeAmount` | Se devuelven como corresponde a la operación |
| `AuthorizationId` | Identificador del crédito en Credinet |
| `TransactionData` | JSON compacto con el crédito y la venta (máximo 250 caracteres) |
| `MerchantReceipt` · `CustomerReceipt` | Comprobantes en XML, cuando el POS tiene documento al cual anexarlos |
| `CardHolder` · `CardType` · `CardNum` | Titular, `Sistecredito` y documento enmascarado |
| `ErrorMessage` · `ErrorMessageTitle` | Solo en fallo, redactados para el cajero |
| `FixedPaymentMeanId` · `FixedPaymentMeanAmount` | **No se envían.** HioPos no los lee en una entrada de caja; ver §4.6.2 |

### 4.5 Modificación del documento fiscal

`ModifyDocumentResultBuilder` enriquece el medio de pago Sistecrédito del documento con los datos
del crédito.

> **Regla crítica:** solo se tocan los medios de pago (`PaymentMeans`). **Nunca** se agregan
> líneas de producto, datos de empresa ni totales: si el resultado se parece a un documento
> nuevo, HioPosCloud lo reenvía a la DIAN y se produce un doble envío.

El identificador y el importe del medio de pago se toman del documento real leído del POS o de la
configuración; no se inventan. Los campos de texto se sanean antes de emitirse
(`DianFieldSanitizer`).

### 4.6 Entrada de caja: el importe del abono en la pantalla del POS

En `Caja → Entradas de caja` el cajero elige `TEF SISTECREDITO` y escribe `1` en importe, porque
HioPos exige importe > 0 para habilitar el botón que levanta el módulo. El crédito y el monto se
eligen **dentro** del módulo, así que al volver la entrada tiene que quedar con el valor abonado.

> **Este apartado no sale del manual.** Se midió desensamblando el HioPos instalado en el terminal
> (`icg.android.start` 15.9.0.0). Lo que el manual documenta para esto **no funciona en este flujo**,
> y el porqué está en §4.6.2.

#### 4.6.1 Lo que funciona: `TransactionType = CASH_IN`

De los 15 campos de la respuesta que lee la pantalla de total, la entrada de caja lee **cinco**:
`TransactionResult`, `TransactionType`, `TransactionData`, `ErrorMessageTitle` y `Amount`.

Qué hace con el importe lo decide el tipo que devolvamos —
`icg.android.cashTransaction.CashTransactionActivity.onExternalModuleResult()`:

```java
paymentMean.setAmount(response.getAmount());          // siempre
if (type.equals("CASH_IN") || type.equals("CASH_OUT"))
{
    paymentMean.setNetAmount(response.getAmount());   // el importe REAL
    controller.sendDocumentChange();                  // refresca la pantalla
}
```

`Amount` es el importe **entregado** y `NetAmount` el **aplicado**; el vuelto es la resta. Con
`SALE` —que es lo que el POS pregunta y lo que se le contestaba— solo corre el primero, y de ahí
salía la pantalla que veía el cajero:

```
TEF SISTECREDITO      $ 1
Entregado:            $ 99.900
Cambio:               $ 99.899      <- vuelto que el arqueo espera del cajón
```

Por eso **esta es la única respuesta del módulo que no hace eco del `TransactionType` recibido**.
`CASH_IN` no figura en el manual, pero el guard de ese mismo método lo acepta explícitamente.
Verificado en terminal el 17/09/2026: importe y cambio quedan correctos, y HioPos **no** relanza la
operación con un `TransactionId` nuevo.

La regla vive en `HioposResultBuilder.AplicarRespuestaDeEntradaDeCaja` —no en el ViewModel— porque
el proyecto de pruebas compila `Services\Hiopos` pero no `ViewModels\`. Solo se aplica cuando **no
hay documento de venta** (§4.2): en una venta el importe es de la factura y pisarlo la descuadra.

#### 4.6.2 Por qué `FixedPaymentMeanId` no sirve acá

El manual (v6.1, 09/09/2025, requiere HioPos 12.34.0.0) documenta `FixedPaymentMeanId` y
`FixedPaymentMeanAmount` como salidas de `TRANSACTION` que fijan el medio de pago y su importe. Se
implementaron al pie de la letra —los dos como cadena, sin devolver el `Amount`— y **HioPos los
ignoró**, sin una sola advertencia en el log.

Los únicos que leen esos campos en HioPos 15.9.0.0 son:

- `icg.android.totalization.TotalizationActivity.processPaymentGatewayResponse()`
- `icg.android.kiosk.controller.KioskSaleController.initialize*Payment()`
- `icg.android.external.module.DocumentApiBase.applyChangesToDocument()`

El paquete `icg.android.cashTransaction` **no los menciona ni una vez**. La frase del manual
*"modificará el medio de pago en la pantalla de total"* es literal, no una forma de hablar.

Por eso el módulo ya no los envía en este flujo: nadie los lee, y conviven mal con el `Amount` que
el mecanismo de §4.6.1 sí necesita.

#### 4.6.3 Que el recaudo cuente como efectivo: es configuración, no código

El medio de pago de la entrada de caja lo elige el cajero **antes** de que el módulo exista, y
ningún campo de la respuesta lo cambia. Pero el objetivo real —que la plata entre al cajón y el
arqueo la cuente como efectivo— se consigue en HioPosCloud, sobre la entidad `PaymentMean`:

| Campo | Qué gobierna | Dónde se lee |
|---|---|---|
| `isCash` | Abre el cajón al totalizar la entrada, y la declara como efectivo | `CashTransactionActivity.doTotalization()`, `cashCount.CurrencyDeclaration.setDeclarationList()` |
| `openCashDrawer` | Apertura del cajón por medio de pago | `paymentMeanEditor.mustOpenCashDrawer()` |
| `zDeclarationType` | Cómo se declara en el arqueo Z | `PaymentMeanCashCountZ`, `CurrencyDeclaration` |

```java
// CashTransactionActivity.doTotalization()
if (paymentMean.isCash && …) openCashDrawer();
controller.contabilizeCashCount();
```

Los tres son editables (`PaymentMeanEditor.setIsCash` / `setOpenCashDrawer` / `setZDeclarationType`),
así que es un cambio de back office.

> **Cuidado: no marcar `isCash` sobre `TEF SISTECREDITO`.** Ese mismo medio cobra las **ventas** a
> crédito, donde no entra plata al cajón; marcarlo descuadraría el arqueo en cada venta.
>
> Lo correcto es **un segundo medio de pago** —p. ej. `SISTECREDITO RECAUDO`— apuntando al mismo
> módulo (`paymentGatewayName`), con `isCash = true`, `openCashDrawer = true` y el `zDeclarationType`
> del efectivo, y usar **ese** en `Caja → Entradas de caja`. El módulo no necesita ningún cambio:
> distingue el recaudo por la ausencia de documento (§4.2), no por el medio de pago.

> **Pendiente:** la **descripción** de la entrada de caja. La columna existe (`CashIn.Description`)
> y las claves `Description`, `Comment` y `Concept` se probaron sin efecto. Coherente con lo medido:
> este flujo solo lee cinco campos y ninguno es de texto libre.

### 4.7 Auditoría hacia el POS

El módulo emite un broadcast `icg.actions.externalApi.AUDIT` en cada operación relevante, con la
acción, un comentario, el token de sesión y el identificador de documento. La misma información
se persiste en la base local cifrada. Los datos personales van enmascarados: el broadcast sale
del recinto de la aplicación.

---

## 5. Alta del módulo en CloudLicense

Para que el módulo aparezca como medio de pago en la caja, ICG debe darlo de alta.

### 5.1 Lo que se entrega a ICG

| # | Dato | Valor |
|---|---|---|
| 1 | Package Name | `com.permoda.sistecreditotef` |
| 2 | APK Name | `permoda` |
| 3 | Versión de contrato | `1` |
| 4 | `versionName` / `versionCode` | `1.0.0` / `1` |
| 5 | Huella del certificado de firma | SHA-1 y SHA-256 del keystore vigente (ver [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md)) |
| 6 | APK firmado de producción | Artefacto verificado con `apksigner` |
| 7 | Nombre y logotipo del medio de pago | Los expone el propio módulo en `GET_CUSTOM_PARAMS` |
| 8 | Capacidades | Las declara el propio módulo en `GET_BEHAVIOR` (§3) |

### 5.2 Lo que se solicita a ICG

- Registro del módulo con el Package Name y el APK Name de §5.1.
- Asignación del módulo a cada terminal que deba ofrecerlo.
- **Provisión de los parámetros de configuración por terminal** en CloudLicense
  (ver [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md) §2.1). Es el
  mecanismo por el cual la credencial de producción no viaja dentro del artefacto.
- Confirmación del procedimiento de validación y aceptación del APK.
- Contacto de soporte y acuerdo de servicio para cambios en CloudLicense.
