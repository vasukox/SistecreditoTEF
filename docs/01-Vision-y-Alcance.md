# 01 · Visión y alcance

## 1. El problema

Las tiendas Permoda / KOAJ facturan con **HioPosCloud**, el POS de ICG Software que corre sobre
terminales Android. Sistecrédito es un medio de pago a crédito para el consumidor final, y su
plataforma —**Credinet**— se opera por una API REST.

Sin integración, un cliente que quiere financiar su compra obliga al cajero a salir del POS,
operar en otra herramienta y volver a registrar el resultado a mano: más tiempo de caja, más
error humano y ninguna trazabilidad entre la factura y el crédito.

## 2. La solución

Un **módulo de cobro electrónico (TEF)** que HioPos reconoce como un medio de pago más. Cuando
el cajero elige *"Sistecredito"*, el POS entrega la operación a este módulo, que conduce el
flujo con el cliente, resuelve la operación contra Credinet y devuelve el resultado al POS para
que cierre e imprima la factura.

Desde la caja se percibe como una pantalla más del POS. Técnicamente son tres sistemas:

```
┌──────────────────────┐   Intent Android    ┌───────────────────────┐   HTTPS REST    ┌──────────────────┐
│     HioPosCloud      │ ──────────────────▶ │   Módulo TEF          │ ──────────────▶ │   Credinet API   │
│  POS de ICG          │  icg.actions.…      │   Sistecrédito        │  Azure APIM     │   Sistecrédito   │
│                      │ ◀────────────────── │   (.NET MAUI Android) │ ◀────────────── │                  │
│  · Arma la factura   │   setResult(…)      │   · Conduce el flujo  │      JSON       │  · Valida cupo   │
│  · Dispara el cobro  │                     │   · Orquesta negocio  │                 │  · Simula plazos │
│  · Imprime y cierra  │                     │   · Devuelve resultado│                 │  · Envía el OTP  │
└──────────────────────┘                     └───────────────────────┘                 │  · Crea crédito  │
                                                        │                              │  · Registra pago │
                                          SQLite cifrado │ idempotencia y auditoría     └──────────────────┘
                                                        ▼
```

## 3. Alcance funcional

### Incluido

| Capacidad | Descripción |
|---|---|
| **Venta a crédito** | Validar al cliente, simular plazos, autorizar por OTP y crear el crédito que paga la factura abierta en el POS |
| **Recaudo desde el POS** | Cobrar una cuota de un crédito existente cuando el cajero entra por *entrada de caja*, sin factura abierta |
| **Abono autónomo** | Cobrar una cuota abriendo el módulo desde su ícono, fuera de HioPos, con identificación previa del cajero |
| **Comprobantes** | Comprobante fiscal devuelto al POS en las ventas; impresión local en los recaudos sin documento |
| **Trazabilidad** | Auditoría de cada operación hacia el POS y hacia una base local cifrada |
| **Idempotencia** | Un reintento no genera un segundo crédito ni un segundo cobro |
| **Control de ambiente** | El módulo se niega a operar si su configuración de producción es incoherente |

### Fuera de alcance

| No incluido | Motivo |
|---|---|
| Anulaciones y notas de crédito | Credinet no soporta reversos. El módulo rechaza explícitamente los `REFUND` de HioPos con un mensaje al cajero |
| Pago con tarjeta (débito/crédito bancario) | Otro medio de pago del POS; el módulo declara `SupportsCredit` únicamente |
| Lectura de tarjetas | El módulo responde *no soportado* a `READ_CARD`, `CHARGE_CARD` y `GET_CARD_DATA` |
| Cierre de lote, propinas, ventas negativas, devoluciones parciales | No aplican a un medio de pago a crédito |
| iOS, Windows y macOS | HioPosCloud opera sobre terminales Android |
| Backend propio | El módulo es un cliente *on-device*. La fuente de verdad es Credinet |

## 4. Actores

| Actor | Rol |
|---|---|
| **Cajero** | Opera el flujo en la terminal |
| **Cliente** | Aporta su documento y el código OTP que recibe por WhatsApp |
| **HioPosCloud (ICG)** | Origina la operación, imprime y cierra la venta. Provisiona la configuración por terminal vía CloudLicense |
| **Credinet (Sistecrédito)** | Autoriza y registra créditos y pagos |
| **Administrador de la terminal** | Custodia el PIN de administración y da de alta a los cajeros para los abonos autónomos |
| **Área de sistemas** | Firma, instala y diagnostica el módulo |

## 5. Naturaleza técnica

| Atributo | Valor |
|---|---|
| Tipo | Aplicación móvil **.NET MAUI**, solo Android |
| Ejecución | *On-device*, en la terminal POS. No es un servicio de backend |
| Integraciones | Dos: HioPosCloud por Intents de Android, Credinet por HTTPS REST |
| Persistencia | Local, cifrada, de alcance operativo (idempotencia y auditoría). La fuente de verdad es Credinet |
| Disponibilidad | Ligada a la terminal. Sin conexión, el módulo informa y no cobra |

El detalle de identificadores, versiones y firma está en
[`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md) y
[`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md).

## 6. Restricciones y supuestos

1. **El `apk_name` lo asigna ICG.** Si no coincide con el alta en HioPosCloud, el módulo nunca
   recibe la operación y el POS cierra la venta sin cobrar.
2. **La configuración de producción la provisiona ICG en CloudLicense.** El artefacto
   distribuido no contiene credenciales de producción.
3. **Las IP públicas de las terminales deben estar autorizadas** en Sistecrédito.
4. **La terminal necesita conectividad** hacia `api.credinet.co` durante toda la operación.
5. **El cliente debe recibir WhatsApp** en la línea registrada en Sistecrédito: es el canal del
   OTP acordado para pruebas y producción.

## 7. Stack tecnológico

| Capa | Tecnología |
|---|---|
| Lenguaje y runtime | .NET 10 / C# (`net10.0-android`) |
| Interfaz | .NET MAUI · patrón MVVM con `CommunityToolkit.Mvvm` |
| HTTP | `HttpClientFactory` + Polly |
| Persistencia local | SQLite cifrado con SQLCipher |
| Configuración | `Microsoft.Extensions.Configuration` (JSON embebido + User Secrets) |
| Pruebas | xUnit, con dobles escritos a mano |

El stack corresponde al oficial de la organización (.NET 10 / C#). El uso de **SQLite/SQLCipher**
es deliberado y no compite con la norma de "solo SQL Server 2022": esa norma aplica a bases de
datos de servidor. Aquí no hay servidor en la terminal; SQLite es el motor embebido estándar de
Android y se usa exclusivamente como caché local cifrada de idempotencia y auditoría. Cualquier
persistencia de servidor que se agregue en el futuro debe usar SQL Server 2022.
