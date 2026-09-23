# 05 · Integración con Credinet (Sistecrédito)

> **Documento propietario del contrato con la API de Sistecrédito.** Los valores de URL y
> credencial por ambiente están en [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md).

Referencia del proveedor: *"Manual de interfaces Credinet para sistemas POS"* (Sistecrédito).

---

## 1. Transporte y autenticación

| Aspecto | Valor |
|---|---|
| Protocolo | HTTPS REST, JSON en ambos sentidos |
| Puerta de entrada | Azure API Management |
| Autenticación | Cabecera `Ocp-Apim-Subscription-Key` |
| Compresión | GZip / Deflate |
| Tiempo de espera | Configurable (30 s por defecto), techo total incluidos reintentos |
| Conexiones simultáneas | 4 como máximo por servidor |

La credencial se inyecta en un único punto (`AuthInterceptor`) y **nunca se registra**: el
manejador de trazas la redacta y enmascara el documento del cliente.

---

## 2. Operaciones

Contrato en `Services/Credinet/ICredinetApi.cs`, implementado por `CredinetApiClient`.

| Operación de dominio | HTTP | Recurso | Reintentable |
|---|---|---|---|
| Consultar cupo del cliente | GET | `getCreditLimitClient` | Sí |
| Consultar plazos posibles para un monto | GET | `getSimulatedMonthLimit` | Sí |
| Consultar el detalle del crédito (cuota, tasa, aval) | GET | `getCreditDetails` | Sí |
| Solicitar el código de autorización (envía el OTP) | GET | `getCreditToken` | **No** — cada llamada envía un OTP |
| Crear el crédito | POST | `create` | **No** — escritura |
| Consultar créditos activos del cliente | GET | `getactivecredits` | Sí |
| Registrar el pago de una cuota | POST | `payCredit` | **No** — escritura |

### 2.1 Cuerpos de las escrituras

**`create`** — campos obligatorios: `typeDocument`, `idDocument`, `creditValue`, `frequency`,
`fees`, `Token` (el OTP), `source` (fijo `"2"` = POS) y `authMethod` (fijo `1` = OTP).
Opcionales: `Seller`, `products`, `invoice`, `storeId`.

> El campo **`invoice`** lleva el identificador de la venta del POS. Es la clave de idempotencia
> del lado de Sistecrédito: si la operación se repite, Credinet responde el error de crédito
> duplicado en lugar de crear un segundo crédito.

**`payCredit`** — obligatorios: `creditId`, `totalValuePaid` y `userName`. Opcional: `storeId`.

> **`userName`** lleva el nombre del cajero identificado. Es lo que hace que la traza del recaudo
> en Sistecrédito identifique a la persona que cobró, en lugar de un valor genérico para toda la
> cadena.

### 2.2 Construcción de las consultas

Las cadenas de consulta se arman con un constructor propio que escapa cada valor. No se
concatenan literales: un documento con caracteres inesperados no puede alterar la petición.

---

## 3. Política de reintentos

Implementada con Polly en `CredinetHttpPolicies`. Es **deliberadamente conservadora**:

- Reintenta **solo fallos transitorios de infraestructura**: excepciones de red, `5xx` y `408`.
- Reintenta **solo lecturas seguras**. Excluye `getCreditToken` —reintentarlo enviaría OTP de
  más— y **toda escritura**, que duplicaría el crédito o el cobro.
- **Dos reintentos** con espera corta (250 ms y 500 ms).
- El tiempo de espera del cliente HTTP es el techo de la operación completa, reintentos
  incluidos.

Los **errores de negocio no se reintentan**: no son fallos de transporte, y en varios casos
llegan con HTTP 200 o con HTTP 4xx y un código en el cuerpo.

---

## 4. Manejo de errores

En dos escalones:

1. **`CredinetApiClient`** traduce los fallos de transporte a excepciones tipadas (red / HTTP).
2. **`CredinetRepository`** decide el resultado: si el cuerpo trae un código de error distinto de
   cero o no trae datos, produce un error de **negocio**; si no, un resultado correcto. Las
   excepciones se mapean a errores de **red** o **HTTP**.

El resultado siempre es un `ApiResult<T>` que los ViewModels resuelven con un `switch`
exhaustivo y traducen a un mensaje para el cajero mediante `FriendlyMessage`.

> Los errores de negocio pueden llegar con **HTTP 4xx**, no solo con 200. Clasificarlos por el
> código HTTP hacía que un mensaje accionable ("ese código ya se usó") se mostrara como un fallo
> genérico de comunicación.

### 4.1 Códigos de negocio traducidos al cajero

| Código | Significado de Credinet | Qué se le dice al cajero |
|---|---|---|
| `220` / `1104` | `InvalidAmountCredit` / `NoOfferAvailable` | Sistecrédito **no financia ese monto**: no hay plan de crédito para ese valor. No es el cupo del cliente y cambiar el plazo no lo resuelve; probar un monto menor u otro medio de pago |
| `224` | `CustomerNotFound` | No se encontró un cliente con ese documento: verificar el número o el tipo de documento |
| `225` | Documento inválido | El documento ingresado no es válido |
| `229` | Token inválido o expirado | El código no es válido o ya expiró: solicitar uno nuevo |
| `230` | `TokenAlreadyUsed` | Ese código ya fue usado: pedir uno nuevo con *Reenviar código* |
| `231` | Monto sobre el límite del cliente | El monto excede el límite del cliente: probar con uno menor |
| `252` | `DuplicatedCredit` | La solicitud ya fue procesada: revisar la pantalla de confirmación |
| `404` | Crédito no encontrado | Verificar el número de crédito |
| *otros* | — | Se traduce el mensaje del proveedor al español antes de mostrar el texto original |

Dos precisiones que se verificaron contra el ambiente de pruebas y que evitan diagnósticos
equivocados en caja:

- **`220` y `1104` son la misma causa con dos nombres.** El plazo no interviene: el corte cae en
  el mismo monto exacto para cualquier documento, y la operación que lo decide
  (`getSimulatedMonthLimit`) ni recibe la cédula. Atribuirlo al cupo del cliente mandaba al
  cajero a revisar algo que estaba bien.
- **`230` no tiene relación con el cupo.** Se traducía como "el cliente no tiene cupo
  disponible", cuando lo único que hacía falta era pedir otro código.

### 4.2 Errores de transporte

| Situación | Mensaje al cajero |
|---|---|
| Sin conexión | Verificar la red e intentar de nuevo |
| `401` | La credencial del servicio no es válida: contactar al supervisor |
| `403` | Acceso denegado |
| `429` | Demasiadas solicitudes: esperar unos segundos |
| `5xx` | El servidor no responde: intentar en un momento |

Los mensajes evitan jerga, códigos HTTP y prefijos técnicos, y siempre indican qué hacer.

---

## 5. El código de autorización (OTP)

| Aspecto | Comportamiento |
|---|---|
| Emisión | Cada llamada a `getCreditToken` envía un código nuevo al cliente |
| Canal | **WhatsApp** a la línea registrada del cliente, en pruebas y en producción |
| Persistencia | **Nunca se guarda.** Es de un solo uso |
| Reenvíos | Espera mínima entre reenvíos y tope por transacción, configurables |
| Verificación | Espera mínima entre intentos y tope de intentos por transacción |
| Contadores | Viven fuera de la pantalla: navegar atrás no los reinicia |
| Reutilización | Si Credinet devuelve el mismo código que ya se había enviado, el módulo lo advierte en lugar de dejar al cajero esperando uno nuevo |
| Reinicio | Los contadores se reinician al comenzar cada operación nueva del POS: una venta no puede quedar bloqueada por la anterior |

Un fallo de red durante la verificación **no** se cuenta como código incorrecto.

---

## 6. Idempotencia

Doble barrera, porque cada una cubre un escenario distinto:

| Barrera | Alcance | Mecanismo |
|---|---|---|
| **Local — créditos** | Reintento del POS sobre la misma venta | Antes de crear, se consulta el almacén local por el identificador de venta. Si ya existe, se devuelve el crédito completo guardado, sin volver a crearlo |
| **Local — abonos** | Doble toque, reinicio de la terminal | Un abono del mismo crédito y el mismo monto dentro de una ventana de **30 minutos** se trata como reintento, no como cobro nuevo |
| **Remota — créditos** | Reintento que llega a Credinet | El campo `invoice` con el identificador de venta; Credinet responde crédito duplicado |

El resultado de un reintento de crédito devuelve **todos los datos financieros** guardados: un
comprobante reconstruido con importes en cero sería un documento de crédito falso.

> **Dependencia externa pendiente:** `payCredit` no tiene un campo de idempotencia equivalente a
> `invoice`. La barrera local cubre el reintento en la misma terminal; un campo del lado
> servidor cubriría además dos terminales cobrando el mismo crédito de forma simultánea. Depende
> de Sistecrédito.

---

## 7. Auditoría de las operaciones

`SistecreditoService` registra cada operación relevante en los dos destinos: el broadcast de
auditoría hacia el POS y la base local cifrada. Incluye la creación de crédito (con marca
explícita cuando es un reintento recuperado del almacén local), el pago de cuota, el rechazo de
notas de crédito y los errores de configuración.
