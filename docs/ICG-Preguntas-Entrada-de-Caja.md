# Consulta a ICG — Entrada de caja con módulo de Cobro Electrónico

**Cliente:** Permoda · **Módulo:** TEF Sistecrédito
**Fecha de las mediciones:** 17/09/2026
**Terminal:** `B5AC009H02300571` (modelo C9H, Android 13) · caja 037 Punto Calle 18

---

## 1. Qué tenemos hoy

| Dato | Valor |
|---|---|
| Tipo de módulo | **API de Cobro Electrónico 4.0** (`icg.actions.electronicpayment.*`) |
| `apk_name` | `permoda` |
| Package | `com.permoda.sistecreditotef` |
| Versión reportada en `GET_VERSION` | `1` |
| Acciones declaradas | 12 (`INITIALIZE`, `GET_BEHAVIOR`, `GET_CUSTOM_PARAMS`, `GET_VERSION`, `TRANSACTION`, `VOID_TRANSACTION`, `FINALIZE`, `SHOW_SETUP_SCREEN`, `GET_CARD_DATA`, `CHARGE_CARD`, `READ_CARD`, `GET_PRINT_INFO`) |

El módulo funciona correctamente en venta y en abono. Lo que sigue es **exclusivamente** sobre el
flujo de **Caja → Entradas de caja**.

---

## 2. Qué necesitamos lograr

El cajero cobra un abono de un crédito Sistecrédito. La plata **entra en efectivo al cajón**.

Al volver del módulo a la pantalla de entrada de caja, necesitamos que quede:

1. **Medio de pago** → efectivo (hoy queda en `TEF SISTECREDITO`)
2. **Importe** → el valor realmente abonado (hoy queda en `1`)
3. **Descripción** → nº de crédito, forma de pago, cliente y fecha (hoy queda vacía)

El `1` existe porque HioPos exige importe > 0 para habilitar el botón que levanta el módulo, y el
crédito y el monto se eligen **dentro** del módulo.

---

## 3. Qué probamos, y qué midió el terminal

Devolvimos en el `setResult` de `TRANSACTION` estos cinco extras:

```
FixedPaymentMeanId = 1
FixedPaymentMeanAmount = 9990000
Description = "Credito 000123 - Sistecredito - <cliente> - 17/09/2026"
Comment = (idem)
Concept = (idem)
```

**Resultado medido: HioPos ignoró los cinco.** El medio siguió en `TEF SISTECREDITO`, el importe en
`$1` y la descripción vacía. Log del módulo:

```
setResult(RESULT_OK) action=…permoda.TRANSACTION TransactionResult=ACCEPTED TransactionType=SALE
extras=[TransactionResult,TransactionType,Amount,TipAmount,TaxAmount,SurchargeAmount,
        TransactionData,AuthorizationId,CardType,CardHolder,
        FixedPaymentMeanId,FixedPaymentMeanAmount,Description,Comment,Concept]
```

### Efecto colateral que encontramos y ya corregimos

Al devolver `Amount` con el total del abono sobre una entrada de `$1`, HioPos lo interpretó como
plata **entregada** por el cliente:

```
TEF SISTECREDITO      $ 1
Entregado:            $ 99.900
Cambio:               $ 99.899
```

Eso deja un vuelto de `$99.899` que el arqueo espera que salga del cajón. Ya lo corregimos
devolviendo el importe que pidió el POS. **Lo mencionamos porque puede afectar a otros módulos de
cobro que devuelvan un importe distinto al solicitado.**

---

## 4. Lo que leímos en la documentación

Del manual **HPCL.025 — API Document 0.5.11**:

- `FixedPaymentMeanId` y `FixedPaymentMeanAmount` están documentados como **`HeaderField` del XML
  `ModifyDocumentResult`** (sección *ModifyDocumentResult*), no como extras planos del Intent.
- Ese XML se devuelve en la acción **`icg.actions.document.<apk_name>.MODIFY_DOCUMENT`**, que
  pertenece a la **API Documento** — otro tipo de módulo. El nuestro es de Cobro Electrónico y no
  recibe esa acción.
- Los `TriggerEvent` que disparan `ModifyDocument` son: `TOTALIZE_SALE`, `TOTALIZE_PURCHASE`,
  `TOTALIZE_ORDER`, `STOCK_ADJUSTMENT`, `FINALIZE_TRANSFER`, `SET_SALE_ON_HOLD`, `PRINT`,
  `MANUAL_EXECUTION`, `CUSTOMER_SELECTION`, `AFTER_CREATE`, `CALL_ON_SUBTOTAL`, `REMOVE_CUSTOMER`,
  `KIOSK_RECEIPT`, `UNKNOWN`. **Ninguno corresponde a una entrada de caja.**

De ahí nuestra duda: el mecanismo existe, pero no encontramos el camino para el flujo de entrada de
caja desde un módulo de cobro.

### 4.1 Revisamos además TODOS los puntos de llamada de la API Documento

`GetBehaviour` (HPCL.025 §GetBehaviour) enumera cada momento en que HioPos puede invocar a un
módulo de documento:

```
CallBeforeTotalizeSale      CallBeforeTotalizePurchase   CallBeforeTotalizeOrder
CallBeforeFinalizeStockAdjustment                        CallBeforeFinalizeTransfer
CallBeforeSetOnHold         CallOnTotalizationCanceled   CallOnDocumentCanceled
CallBeforePrint             CallOnCustomerSelection      CallAfterCreate
CallVoidDocument            CallOnSubtotal               RemoveCustomer
CallOnCashCount             CallBeforeKioskReceipt       ReadScaleCodes
CallOnPrintCopy             CanShowAsSaleHeaderOption    CreateUpdateCustomer
```

**Ninguno corresponde a una entrada de caja.** El más cercano, `CallOnCashCount`, es al generar el
**arqueo Z**, no un movimiento de caja individual. La lista de valores del tag `Action` (18 valores)
lo confirma.

### 4.2 Probamos también las capacidades no documentadas de Cobro Electrónico

Leyendo el APK de HioPos instalado en el terminal encontramos cuatro capacidades de `GET_BEHAVIOR`
que el manual de Cobro Electrónico no menciona: `SupportsCashTransaction`, `SupportsSale`,
`SupportsVoid`, `SupportOverPayment`. Nos llamó la atención `SupportsCashTransaction`, porque HioPos
tiene un subsistema `icg.android.cashTransaction` completo.

**Las declaramos y el comportamiento no cambió:** el `TRANSACTION` siguió llegando con
`TransactionType=SALE` y `Amount=100`, sin acciones ni extras nuevos.

### 4.3 Conclusión a la que llegamos

Con lo anterior, **no encontramos ningún camino** para que un módulo externo modifique el medio de
pago, el importe o la descripción de una entrada de caja. Queremos confirmarlo con ustedes: si
existe y se nos escapó, díganos la clave exacta, la estructura y la versión; si no existe, lo
cerramos y documentamos el procedimiento manual.

---

## 5. Preguntas concretas

1. **¿Puede un módulo de Cobro Electrónico modificar el `PaymentMeanId`, el `Amount` y la
   `Description` de una entrada de caja al devolver el resultado de `TRANSACTION`?**
   Si sí: ¿con qué clave exacta, en qué estructura (extra plano o XML) y desde qué versión de la API?

2. **¿`FixedPaymentMeanId` / `FixedPaymentMeanAmount` son válidos como extras planos en la respuesta
   de `TRANSACTION`?** Nuestra medición dice que no se aplican. ¿Son exclusivos del
   `ModifyDocumentResult` de la API Documento?

3. **¿Existe un `TriggerEvent` de entrada/salida de caja para `ModifyDocument`?**
   Si implementáramos además un módulo de API Documento, ¿se dispararía en este flujo?

4. **Los campos libres que creamos en la dimensión "Entrada Caja"** (16 campos: `errorCode`,
   `message`, `country`, `paymentId`, `paymentNumber`, `creditId`, `typeDocument`, `idDocument`,
   `creditValuePaid`, `interestValuePaid`, `arrearsValuePaid`, `assuranceValuePaid`,
   `chargeValuePaid`, `balance`, `nextDueDate`, `nextMinimumPayment`):
   **¿por qué canal los debe poblar un módulo de cobro en una entrada de caja?**
   Los `CustomPaymentMeanFields` que vemos documentados aparecen en el `<Document>` que HioPos nos
   envía, y en una entrada de caja no hay documento.

5. **¿Se puede marcar el medio de pago `TEF SISTECREDITO` como efectivo (`IsCash`) en HioPosCloud**,
   manteniendo el módulo TEF asociado, para que el recaudo cuente como efectivo en el arqueo?
   Es la alternativa que vemos sin cambios de código; queremos confirmar que es soportada y que no
   rompe nada.

6. **¿Existe en HioPos un tipo de documento de recaudo / cobro de cartera** más apropiado que
   "entrada de caja" para este caso (cobrar la cuota de un crédito de un tercero)?
   Si existe, preferimos usar el mecanismo correcto antes que forzar el de entrada de caja.

7. **¿Hay forma de que HioPos pida el importe al módulo antes de habilitar Aceptar**, o de lanzar el
   módulo sin importe previo? Así el cajero no tendría que escribir `1`.

---

## 6. Qué pedimos de la reunión

- Confirmación de **cuál de los tres campos es alcanzable y cómo**, o confirmación explícita de que
  no lo son, para cerrar el tema y documentar el procedimiento manual.
- Si la respuesta es la API Documento: confirmar si conviven dos módulos del mismo cliente
  (`electronicpayment` + `document`) en la misma terminal, y con qué `apk_name`.
- Confirmación del **`apk_name` registrado** para este módulo (hoy `permoda`) y del **SHA-1** del
  certificado dado de alta: `7D:14:24:27:58:49:08:1E:1F:56:9C:DE:C6:75:D5:27:D7:CC:CB:CE`.
