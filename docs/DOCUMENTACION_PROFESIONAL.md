# SistecreditoTEF.Maui — Documentación Profesional Integral

> **Módulo TEF Sistecrédito para HioPosCloud**
> App .NET MAUI Android · `com.permoda.sistecreditotef` · `apk_name=permoda` · v1.0.0
> Historia de Usuario: **HU8‑973** · Equipo de Arquitectura — DOPE
>
> Este documento es la **referencia técnica, funcional y operativa exhaustiva** del
> proyecto. Cubre la integración con HioPosCloud (ICG), el contrato con la API de
> Credinet (Sistecrédito), los tres modos de operación, los ambientes, la
> seguridad, la idempotencia, la auditoría, la operación, el despliegue y los
> runbooks de incidente. Cualquier persona que lo lea debe poder operar, auditar,
> depurar y evolucionar el módulo sin tener que abrir el código.

---

## Índice

1. [Resumen ejecutivo](#1-resumen-ejecutivo)
2. [Actores y sistemas involucrados](#2-actores-y-sistemas-involucrados)
3. [Glosario](#3-glosario)
4. [Identidad del artefacto y claves de enrutamiento](#4-identidad-del-artefacto-y-claves-de-enrutamiento)
5. [Topología y fronteras de integración](#5-topología-y-fronteras-de-integración)
6. [Contrato con HioPosCloud (ICG)](#6-contrato-con-hioposcloud-icg)
7. [Contrato con Credinet (Sistecrédito)](#7-contrato-con-credinet-sistecrédito)
8. [Modos de operación y escenarios funcionales](#8-modos-de-operación-y-escenarios-funcionales)
9. [Ambientes (Sandbox, Staging, Producción)](#9-ambientes-sandbox-staging-producción)
10. [Configuración y precedencia](#10-configuración-y-precedencia)
11. [Arquitectura interna y capas](#11-arquitectura-interna-y-capas)
12. [Patrones de diseño aplicados](#12-patrones-de-diseño-aplicados)
13. [Inyección de dependencias y ciclo de vida](#13-inyección-de-dependencias-y-ciclo-de-vida)
14. [Modelo de datos (DTO · Dominio · HioPos)](#14-modelo-de-datos-dto--dominio--hiopos)
15. [Persistencia local cifrada](#15-persistencia-local-cifrada)
16. [Idempotencia de créditos y abonos](#16-idempotencia-de-créditos-y-abonos)
17. [Auditoría y trazabilidad](#17-auditoría-y-trazabilidad)
18. [OTP — Solicitud, throttle y verificación](#18-opp--solicitud-throttle-y-verificación)
19. [Manejo de dinero y redondeo](#19-manejo-de-dinero-y-redondeo)
20. [Comprobantes, voucher y módulo fiscal DIAN](#20-comprobantes-voucher-y-módulo-fiscal-dian)
21. [Impresión local (abonos standalone)](#21-impresión-local-abonos-standalone)
22. [Manejo de errores y mensajes al cajero](#22-manejo-de-errores-y-mensajes-al-cajero)
23. [Resiliencia y reintentos (Polly)](#23-resiliencia-y-reintentos-polly)
24. [Seguridad del transporte (HTTPS, certificate pinning)](#24-seguridad-del-transporte-https-certificate-pinning)
25. [Protección de datos personales (Habeas Data)](#25-protección-de-datos-personales-habeas-data)
26. [Cifrado en reposo (SQLCipher)](#26-cifrado-en-reposo-sqlcipher)
27. [Gestión de credenciales](#27-gestión-de-credenciales)
28. [Aislamiento de plataforma Android](#28-aislamiento-de-plataforma-android)
29. [Resolución de transacciones en duda](#29-resolución-de-transacciones-en-duda)
30. [Fail-safe: nunca dejar al POS esperando](#30-fail-safe-nunca-dejar-al-pos-esperando)
31. [Estrategia de pruebas](#31-estrategia-de-pruebas)
32. [Build, firma y empaquetado](#32-build-firma-y-empaquetado)
33. [Despliegue, alta y distribución](#33-despliegue-alta-y-distribución)
34. [Inventario de datos sensibles y retenciones](#34-inventario-de-datos-sensibles-y-retenciones)
35. [Cadena de suministro y dependencias](#35-cadena-de-suministro-y-dependencias)
36. [Riesgos, hallazgos heredados y deuda técnica](#36-riesgos-hallazgos-heredados-y-deuda-técnica)
37. [Runbooks operativos](#37-runbooks-operativos)
38. [Checklist de despliegue](#38-checklist-de-despliegue)
39. [Pendientes que dependen de terceros](#39-pendientes-que-dependen-de-terceros)
40. [Decisiones de arquitectura (ADR)](#40-decisiones-de-arquitectura-adr)
41. [Convenciones de código y archivos clave](#41-convenciones-de-código-y-archivos-clave)

---

## 1. Resumen ejecutivo

**SistecreditoTEF.Maui** es un **módulo de pago TEF (Transferencia Electrónica de
Fondos)** para **HioPosCloud**, el software POS de ICG Software que opera en las
tiendas Permoda / KOAJ. Permite a los cajeros ofrecer a los clientes la opción
de pagar o financiar una compra con **Sistecrédito (Credinet)**, un crédito de
consumo operado por la marca **Sistecrédito** sobre la plataforma de **Credinet**.

Cuando el cajero selecciona *"Sistecredito"* como medio de pago en HioPos, esta
app toma el control, guía un flujo guiado de **cédula → cupo → cuotas → OTP →
crédito**, habla con la **API de Credinet** por HTTPS, y devuelve el resultado al
POS (aceptado/fallido + comprobantes) para que HioPos imprima y cierre la venta.

La misma app también soporta el flujo de **abono / recaudo** de cuotas de créditos
ya existentes, tanto disparado desde HioPos como invocado de forma **standalone**
(por el ícono del launcher del APK, sin venta abierta).

| Aspecto | Valor |
|---|---|
| Tipo | App móvil .NET MAUI (Android) |
| Plataforma | Android 13+ (minSdk 24 · targetSdk 36) |
| Package Name | `com.permoda.sistecreditotef` |
| `apk_name` (HioPos) | `permoda` |
| Versión de contrato | `1.0.0` (versión ICG = `1`) |
| ABIs | `arm64-v8a`, `x86_64` |
| Backend | Credinet API (Azure APIM) vía HTTPS REST |
| Stack | .NET 10 · C# · MAUI · MVVM · HttpClient + Polly · SQLite/SQLCipher |
| HU | HU8‑973 (Equipo de Arquitectura — DOPE) |
| Estado | Apto para despliegue (veredicto QA‑Seguridad) |

---

## 2. Actores y sistemas involucrados

```
┌──────────────────────┐  Intents Android    ┌───────────────────────┐  HTTPS + APIM   ┌──────────────┐
│     HioPosCloud       │ ───────────────────▶│      ESTE MÓDULO       │ ───────────────▶│  Credinet    │
│  (POS · Android 13)   │  icg.actions...     │   com.permoda...      │  Ocp-Apim-Key    │  Azure APIM  │
│   · Arma la factura   │ ◀───────────────────│                       │ ◀───────────────│              │
│   · Dispara el pago   │   setResult(...)    │   v1.0.0 (ICG v=1)    │   JSON          │  /pos/  (sb) │
│   · Imprime recibo    │                     │                       │                 │  /posprod/(pr)│
│   · Cierra la venta   │                     │                       │                 └──────────────┘
└──────────────────────┘                     └────────────┬──────────┘
           ▲                                              │
           │  Broadcast auditoría                        ▼
           │  icg.actions.externalApi.AUDIT   SQLite cifrado con SQLCipher
           │                              (idempotencia + auditoría, llave en Keystore)
```

### 2.1 Los tres actores

| Actor | Rol | Tecnología | Contacto |
|---|---|---|---|
| **HioPosCloud** | POS de la tienda (ICG Software). Inicia el flujo de pago, imprime el comprobante, cierra la factura. | Android 13+, app nativa ICG | `icg.actions.electronicpayment.permoda.*` |
| **Esta app (SistecreditoTEF.Maui)** | Módulo TEF: orquesta la UI, la lógica de negocio, el HTTP a Credinet, la idempotencia y la auditoría. | .NET MAUI Android | `com.permoda.sistecreditotef` |
| **Credinet API (Sistecrédito)** | Valida al cliente, calcula cuotas, envía el OTP, crea el crédito y registra pagos. | Azure APIM, REST/JSON | `https://api.credinet.co/pos/` (sb) · `https://api.credinet.co/posprod/` (pr) |

### 2.2 Actores humanos

| Actor | Rol |
|---|---|
| **Cajero** | Selecciona el medio de pago, completa el flujo con el cliente, entrega el voucher. |
| **Cliente** | Titular del crédito. Recibe el OTP por WhatsApp y confirma el código. |
| **Operador de TI de Permoda** | Configura la terminal, gestiona la firma del APK y la base local. |
| **Equipo DOPE (Arquitectura)** | Dueño del módulo. Aprueba cambios de arquitectura y excepciones al stack. |
| **ICG (proveedor de HioPos)** | Da de alta el módulo en CloudLicense, mantiene la versión de contrato. |
| **Sistecrédito** | Opera la API de Credinet. Emite credenciales, claves y pines SPKI. |

---

## 3. Glosario

| Término | Significado |
|---|---|
| **TEF** | Transferencia Electrónica de Fondos. Categoría del módulo en HioPos. |
| **apk_name** | Nombre con el que ICG identifica el módulo en CloudLicense. Hoy: `permoda`. |
| **SaleId** | UUID único de la venta en HioPos. Se usa como `invoice` para idempotencia. |
| **DocumentData** | XML de la venta que HioPos envía *inline* en el Intent (no por ruta). |
| **DocumentPath** | Ruta alternativa al XML de venta. Solo se usa si supera 1 MB. |
| **creditId** | GUID del crédito en Credinet. Se devuelve a HioPos como `AuthorizationId`. |
| **paymentId** | Identificador del pago (abono) en Credinet. |
| **OTP** | One-Time Password. Código de 6 dígitos que Credinet envía al cliente. |
| **TEA** | Tasa Efectiva Anual. Obligatoria en el voucher. |
| **AVAL** | Seguro obligatorio incluido en cada cuota. |
| **SPKI** | Subject Public Key Info. Lo que se "pinea" en certificate pinning. |
| **Idempotencia** | Que reintentar una operación no la duplique. Local + remota. |
| **Doble barrera** | Idempotencia local (SQLite cifrado) + remota (`invoice=SaleId` en Credinet). |
| **CloudLicense** | Portal de ICG donde se provisionan los parámetros de runtime del módulo. |
| **ICG** | ICG Software. Proveedor del POS HioPosCloud. |
| **HioPos** | Abreviatura de HioPosCloud, el POS. |
| **DOPE** | Equipo de Arquitectura dueño del módulo. |
| **DIAN** | Dirección de Impuestos y Aduanas Nacionales de Colombia. |
| **SIAT** | Sistema de Información Aduanera y Tributaria (DIAN). |
| **Habeas Data** | Ley 1581 de 2012 (Colombia) — protección de datos personales. |
| **Polly** | Librería .NET de políticas de resiliencia (reintentos, circuit breaker). |
| **SQLCipher** | Build de SQLite con cifrado AES-256 nativo. |
| **SecureStorage** | API MAUI respaldada por Android Keystore. |
| **AOT** | Ahead-Of-Time compilation. Desactivada por defecto en este proyecto. |
| **R8/d8** | Minificadores/compiladores DEX de Android. Se usa d8 sin minificación. |
| **AIDL** | Android Interface Definition Language. Para IPC con servicios de otras apps (ej: Sunmi). |
| **CSP** | Content Security Policy. |
| **PKCS12** | Estándar de almacenamiento de llave privada + certificado. Usa una sola contraseña. |

---

## 4. Identidad del artefacto y claves de enrutamiento

| Concepto | Valor | Dónde se define |
|---|---|---|
| **Package Name** (Android) | `com.permoda.sistecreditotef` | `.csproj` → `ApplicationId` |
| **APK Name** (ICG) | `permoda` | `Services/Hiopos/HioposConstants.cs` → `HioposActions.ApkName` |
| Nombre visible | `Sistecredito` | `ApplicationTitle`, `android:label`, `GET_CUSTOM_PARAMS` |
| `versionCode` | `1` | `ApplicationVersion` |
| `versionName` | `1.0.0` | `ApplicationDisplayVersion` |
| Versión reportada a HioPos (GET_VERSION) | `1` (int) | `HioposActions.ModuleVersion` |
| Action prefix | `icg.actions.electronicpayment.permoda.` | derivado de `ApkName` |
| Broadcast de auditoría | `icg.actions.externalApi.AUDIT` | `HioposActions.ExternalAudit` |

> ⚠️ **El `apk_name` es la llave de enrutamiento.** Si no coincide con el alta en
> HioPosCloud, la app NUNCA recibe el Intent, HioPos hace timeout y la venta sale
> sin cobrar. Se cambia en **una sola constante** (`HioposActions.ApkName`); todas
> las 11 acciones se recalculan solas.

### 4.1 Histórico de rotación de certificados (firmas)

| Versión | SHA-1 | Estado |
|---|---|---|
| `permoda-release.keystore` (v1) | `f81119a4…` | **OBSOLETO** — conservar como registro histórico. |
| `permoda-release-v2.keystore` (v2) | `7d142427…` | **VIGENTE** desde 14/07/2026. |

Android no permite actualizar un paquete con una firma distinta. En terminales
que tengan la versión v1 instalada, **hay que desinstalar antes de instalar la v2**.

### 4.2 Historial de `versionCode`

`1` primera instalación piloto · `2` resolución del contenedor de DI · `3`
diagnóstico XML de Parameters · `4` cliente genérico de HioPos · `5` impresión
USB ESC/POS · `6` formato de log y Android Print · `7` comprobante de abonos ·
`8` clasificación de 4xx y desbloqueo del botón cobrar · `9` separador de miles ·
`10` cursor del monto y comprobante al abrir · `11` GET_VERSION de contrato fijo.

---

## 5. Topología y fronteras de integración

### 5.1 Diagrama lógico

```
┌──────────────────────────────────────────────────────────────────────────┐
│  PRESENTACIÓN (MVVM)                                                       │
│  Views/*.xaml  ──bindings──▶  ViewModels/*  (CommunityToolkit.Mvvm)        │
│  Controls: ScaffoldView, HeroCardView · Navegación: Shell + AppRoutes      │
└───────────────────────────────┬───────────────────────────────────────────┘
                                │  llama a
┌───────────────────────────────▼───────────────────────────────────────────┐
│  DOMINIO / CASOS DE USO                                                    │
│  UseCases/SistecreditoService  (fachada: orquesta 7 operaciones +          │
│                                 idempotencia + auditoría transversal)      │
│  Models/*  (Client, Credit, Payment, CreditDetails, ...) — records        │
└───────────────────────────────┬───────────────────────────────────────────┘
                                │  depende de contratos (interfaces)
┌───────────────────────────────▼───────────────────────────────────────────┐
│  ACCESO A DATOS / INTEGRACIONES                                            │
│  Services/Credinet:  ICredinetRepository → CredinetRepository             │
│                      ICredinetApi → CredinetApiClient (HttpClient)         │
│                      Mappers (DTO↔Dominio) · ApiConfig · Polly · Pinning   │
│  Services/Hiopos:    IntentParser · ResultBuilder · ReceiptBuilder ·      │
│                      Constants                                             │
│  Services/Platform:  SQLite cifrado · TokenStore · Idempotencia ·         │
│                      Auditoría · Navegación · Impresión · Logging         │
└───────────────────────────────┬───────────────────────────────────────────┘
                                │
┌───────────────────────────────▼───────────────────────────────────────────┐
│  PLATAFORMA (Android)                                                       │
│  Platforms/Android: MainActivity (recibe Intents) · AndroidTransactionResult│
│                     Handler (setResult) · BroadcastAuditLogger              │
└──────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Las dos fronteras

| Frontera | Mecanismo | Quién puede invocarla | Control |
|---|---|---|---|
| **Entrada desde HioPos** | Intents Android sobre `MainActivity` (`exported=true`, obligatorio) | Cualquier app del dispositivo | El módulo no expone operaciones privilegiadas: crear un crédito exige OTP; un abono exige seleccionar un crédito activo previamente validado |
| **Salida hacia Credinet** | HTTPS REST + `Ocp-Apim-Subscription-Key` | Solo el módulo | Ver §24 y §27 |
| **Canal lateral de auditoría** | `sendBroadcast` con `icg.actions.externalApi.AUDIT` | El módulo emite; HioPos consume | Requiere `Token` (entregado en INITIALIZE). Datos personales enmascarados |

### 5.3 Regla de oro

Cada capa habla con la de abajo **a través de interfaces**
(`ICredinetRepository`, `ITokenStore`, `IIdempotencyStore`, `INavigationService`,
`ITransactionResultHandler`, `IAuditLogger`, `IReceiptPrinter`, `IDocumentReader`).
Esto permite testear el dominio con fakes y sustituir la implementación de
Android por un `NoOp` en tests u otras plataformas.

---

## 6. Contrato con HioPosCloud (ICG)

### 6.1 Las 11 acciones (intent-filters)

Prefijo `icg.actions.electronicpayment.permoda.` + :

| # | Acción | Handler | Efecto |
|---|---|---|---|
| 1 | `INITIALIZE` | `HandleInitialize` | Persiste `Token`; parsea y guarda `Parameters` de CloudLicense (`CloudConfigStore`); recarga `ApiConfig`; purga datos locales viejos. |
| 2 | `FINALIZE` | `HandleFinalize` | Limpia el token de sesión. **No** borra la config de Credinet (los abonos standalone la necesitan). |
| 3 | `GET_VERSION` | `HandleGetVersion` | Devuelve `Version = 1` (int, valor de contrato con ICG). |
| 4 | `GET_BEHAVIOR` | `HandleGetBehavior` | Declara las **17 flags** de capacidad (`HioposCapabilities`). |
| 5 | `GET_CUSTOM_PARAMS` | `HandleGetCustomParams` | Nombre ("Sistecredito") + logo PNG (placeholder hoy). |
| 6 | `GET_PRINT_INFO` | `HandleGetPrintInfo` | Info de impresión (aunque `CanPrint=false`). |
| 7 | **`TRANSACTION`** ⭐ | `HandleTransaction` | Inicia el flujo: parsea extras, lee documento, navega a `CapturaCedula` o `CreditosActivos`. |
| 8 | `SHOW_SETUP_SCREEN` | `HandleShowSetupScreen` | Pantalla de setup (MVP: OK vacío). |
| 9 | `READ_CARD` | `HandleUnsupported` | No soportada → `Canceled`. |
| 10 | `CHARGE_CARD` | `HandleUnsupported` | No soportada → `Canceled`. |
| 11 | `GET_CARD_DATA` | `HandleUnsupported` | No soportada → `Canceled`. |

### 6.2 Capacidades declaradas (GET_BEHAVIOR)

17 flags (fijadas por test):

| Flag | Valor | Notas |
|---|---|---|
| `SupportsCredit` | **true** ✅ | Único medio de pago soportado. |
| `HasCustomParams` | **true** | Para mostrar logo y nombre "Sistecredito". |
| `CanAudit` | **true** | Emite broadcasts `icg.actions.externalApi.AUDIT`. |
| `OnlyUseDocumentPath` | **false** | HioPos manda el documento *inline* en `DocumentData`. |
| `SupportsTransactionVoid` | false | Credinet no soporta reversos. |
| `SupportsTransactionQuery` | false | |
| `SupportsNegativeSales` | false | |
| `SupportsPartialRefund` | false | Las notas de crédito se rechazan. |
| `SupportsBatchClose` | false | |
| `SupportsTipAdjustment` | false | |
| `SupportsDebit` | false | |
| `SupportsEBTFoodstamp` | false | |
| `CanChargeCard` | false | |
| `ExecuteVoidWhenAvailable` | false | |
| `SaveLoyaltyCardNum` | false | |
| `CanPrint` | false | Imprime HioPos (con el XML que devolvemos). |
| `ReadCardFromApi` | false | |

### 6.3 Extras del Intent (TRANSACTION)

**Entrada (HioPos → App):**

| Extra | Tipo | Notas |
|---|---|---|
| `TransactionType` | string | `SALE` (venta) / `REFUND` (nota de crédito, se rechaza) |
| `TenderType` | string | `CREDIT` en ventas de crédito |
| `Amount` | long | Monto en **centavos** |
| `TipAmount` | long | Propina en centavos |
| `TaxAmount` | long | Impuestos en centavos |
| `TaxDetail` | string | |
| `TransactionId` | string | |
| `TransactionData` | string | JSON libre ≤200 chars (lo usa el módulo fiscal) |
| `DocumentData` | string | XML de la venta, **inline** |
| `DocumentPath` | string | Solo si XML >1 MB |
| `ShopData` | string | |
| `SellerData` | string | XML con `<name>` del cajero |
| `IsAdvancedPayment` | bool | |
| `OverPaymentType` | int | |
| `SurchargeAmount` | long | Recargos |
| `ReceiptPrinterColumns` | int | Ancho del rollo del POS |
| `Parameters` | string | XML con parámetros de CloudLicense (en INITIALIZE) |
| `Token` | string | Token de sesión de HioPos (en INITIALIZE) |

**Salida (App → HioPos, en setResult):**

| Extra | Tipo | Notas |
|---|---|---|
| `TransactionResult` | string | `ACCEPTED` / `FAILED` / `UNKNOWN_RESULT` |
| `TransactionType` | string | Eco al `TransactionType` recibido |
| `Amount` | long | Monto efectivamente financiado/pagado en centavos |
| `TipAmount`, `TaxAmount`, `SurchargeAmount` | long | |
| `TransactionData` | string | JSON `{creditId, creditNumber, saleId}` ≤250 chars |
| `MerchantReceipt` | string | XML del voucher para el comercio |
| `CustomerReceipt` | string | XML del voucher para el cliente |
| `AuthorizationId` | string | = `creditId` de Credinet |
| `CardHolder` | string | Nombre del titular |
| `CardType` | string | `"Sistecredito"` |
| `CardNum` | string | Cédula enmascarada |
| `ErrorMessage` | string | Si falló |
| `ErrorMessageTitle` | string | Título del error (ej: "Configuración inválida") |
| `BatchNumber`, `BatchReceipt`, `ReceiptFailed` | string | Opcionales |
| `ModifyDocumentResult` | string | XML que enriquece `PaymentMeans` para el módulo fiscal DIAN |
| `FixedPaymentMeanId` | string | Reservado para pagos parciales |
| `FixedPaymentMeanAmount` | long | |

### 6.4 Ciclo de vida crítico del Intent

El Intent se **guarda** en `OnCreate`/`OnNewIntent` (`_pendingIntent`) y se
**procesa en `OnResume`**, porque solo entonces `IPlatformApplication.Current.Services`
( el contenedor DI de MAUI) está garantizado disponible. Procesarlo antes
provoca `NullReferenceException` en cold-start.

### 6.5 Guard de colisión de modos

Con `launchMode=singleTask`, el launcher y HioPos comparten la misma Activity.
La bandera **`HioposTransactionActive`** marca una factura de HioPos como viva
entre el `TRANSACTION` y el `setResult`. Mientras está activa, un Intent del
launcher se descarta para no pisar la venta en curso.

---

## 7. Contrato con Credinet (Sistecrédito)

### 7.1 Los 7 endpoints

| Operación dominio | HTTP | Endpoint | Reintentable | Notas |
|---|---|---|---|---|
| `GetCreditLimitClientAsync` | GET | `getCreditLimitClient` | ✅ | Valida cliente + cupo disponible |
| `GetSimulatedMonthLimitAsync` | GET | `getSimulatedMonthLimit` | ✅ | Barrera de oferta (rechaza `1104 NoOfferAvailable`) |
| `GetCreditDetailsAsync` | GET | `getCreditDetails` | ✅ | Calcula cuota, TEA, aval |
| `GetCreditTokenAsync` | GET | `getCreditToken` | ❌ | Dispara OTP |
| `CreateAsync` | POST | `create` | ❌ | Efecto de escritura |
| `GetActiveCreditsAsync` | GET | `getactivecredits` | ✅ | Lista créditos activos |
| `PayCreditAsync` | POST | `payCredit` | ❌ | Efecto de escritura |

### 7.2 Header y autenticación

Cada request lleva `Ocp-Apim-Subscription-Key: <key>`, inyectado por el
`AuthInterceptor` (DelegatingHandler). El `Accept` es `application/json`. La
compresión `gzip,deflate` se negocia automáticamente.

### 7.3 Manejo de errores

**Dos escalones:**

1. **`CredinetApiClient.ExecuteAsync`** traduce fallos de transporte a
   `ApiException` (`Network`/`Http`). Detecta el sobre de error de Credinet
   aunque llegue como HTTP 4xx (`errorCode != 0` en el cuerpo JSON).
2. **`CredinetRepository.ProcessResponse`** clasifica: `errorCode != 0` o
   `data == null` → `ApiError.Business`. Si no, `Ok`. `HandleApiException`
   mapea `ApiException` a `ApiError.Network`/`ApiError.Http`.

El resultado siempre es un `ApiResult<T>` que los ViewModels consumen con un
`switch` exhaustivo y traducen a mensajes amables con `FriendlyMessage`.

### 7.4 Códigos de error relevantes

| `errorCode` | Significado | Comportamiento |
|---|---|---|
| `0` | OK | — |
| `220` | `InvalidAmountCredit` (sin oferta en `getCreditToken`) | Mensaje claro al cajero |
| `222` | `MonthsNumberNotValid` | El plazo no aplica; se filtra de la lista |
| `223` | `RequestValuesInvalid` | Típicamente `idDocument` vacío (inyección de Cédula en el servicio) |
| `224` | `CustomerNotFound` | Cédula no válida (chequear sanitización del teclado numérico) |
| `230` | `TokenAlreadyUsed` | OTP ya consumido |
| `252` | `DuplicatedCredit` | Doble barrera (local + `invoice`) ya cubre esto |
| `1104` | `NoOfferAvailable` | Sin oferta para el monto; la barrera de oferta ya lo evita |

---

## 8. Modos de operación y escenarios funcionales

### 8.1 Modos posibles

| Modo | Origen | Pantalla inicial | Imprime | Cierre |
|---|---|---|---|---|
| **CRÉDITO** | `TRANSACTION(SALE)` con documento | `CapturaCedula` | HioPos | `Finish` + `setResult(ACCEPTED)` |
| **ABONO desde HioPos** (entrada de caja) | `TRANSACTION` sin documento | `CreditosActivos` | HioPos (con el XML) | `Finish` + `setResult(ACCEPTED)` |
| **REFUND (nota de crédito)** | `TRANSACTION(REFUND)` | — | — | `Failed` inmediato ("Credinet no soporta reversos") |
| **Standalone del launcher** | ícono del APK | `CreditosActivos` | Local (`IReceiptPrinter`) | `FinishAffinity` (sin devolver nada a HioPos) |

> **HU‑134:** un recaudo (entrada de caja) y una venta llegan ambos como
> `TRANSACTION(SALE)`. La diferencia es que el recaudo no trae `DocumentData`.
> Esta es la única señal inequívoca para enrutar: si NO hay documento → `CreditosActivos`.
> **No se altera el `TransactionType`** en la respuesta (eso provocaba que HioPos
> reintentara con un `TransactionId` nuevo).

### 8.2 Escenario A — CRÉDITO (venta a crédito)

```
1. Cajero selecciona "Sistecredito" en HioPos
2. HioPos → Intent TRANSACTION(SALE, DocumentData)
3. App: state.Clear() + HioposTransactionActive=true + OtpThrottle.Reset()
4. App: parsea extras → HioposTransaction
5. App: XmlDocumentReader.Parse(DocumentData) → SaleDocument (autocompleta cédula)
6. App: navega a CapturaCedula (con cédula prellenada)
   ── CapturaCedula ──
7. Cajero confirma/edita cédula → GET getCreditLimitClient (valida cupo)
8. → GET getCreditDetails (cuota, TEA, aval)
   ── SeleccionCuotas ──
9. Cajero elige plazo → GET getSimulatedMonthLimit (barrera de oferta)
10. App: ofrece plazos válidos (consulta paralela)
   ── Confirmacion ──
11. Cajero confirma → GET getCreditToken (envía OTP al celular del cliente)
   ── OTP ──
12. Cliente dicta OTP al cajero → POST create (con invoice=SaleId)
13. App: idempotencia hit? devuelve Credit completo; si no, persiste para replay
14. POST create OK → Credit, persiste CachedTransaction con CreditJson
   ── Voucher ──
15. App arma MerchantReceipt/CustomerReceipt (XML)
16. App arma ModifyDocumentResult (PaymentMeans para DIAN)
17. setResult(ACCEPTED) a HioPos con AuthorizationId=creditId, CardType="Sistecredito"
18. HioPos imprime y cierra la factura.
```

**Validaciones que aplican:**
- `ApiConfig.Validate()` antes de empezar (rechaza producción con sandbox, etc.).
- Documento malformado **no cancela la venta**: el documento solo aporta
  conveniencia (autocompletar cédula y `SaleId`), se continúa sin él.
- Idempotencia local (SQLite) + remota (`invoice=SaleId`) en el POST `create`.

### 8.3 Escenario B — PAGO (abono desde HioPos)

```
1. Cajero abre HioPos → "Entrada de caja" / "Recaudo" → "Sistecredito"
2. HioPos → Intent TRANSACTION sin DocumentData
3. App: detecta "sin documento" → standalone.IsStandalone=true, IsRefundFromHioPos=true
4. App navega a CreditosActivos (no pasa por CapturaCedula/Cuotas/OTP/Confirmacion)
5. Cajero digita cédula → GET getactivecredits
6. Cajero selecciona crédito + monto → POST payCredit (con userName)
   ── ReciboPago ──
7. App: idempotencia persistente (Pending → Completed/Failed)
8. POST payCredit OK → Payment, persiste CachedPayment con PaymentJson
9. App arma comprobante; HioPos imprime y cierra.
10. setResult(ACCEPTED) con TransactionType=REFUND
```

**Validaciones:**
- `userName` no puede ir vacío (Credinet rechaza con 400 `[REP-E-003]`). El
  servicio lo reemplaza por un rótulo genérico y registra la incidencia.
- Idempotencia por `creditId + amountCents`, ventana 30 min, sobrevive reinicios.
- Estados: `Pending` → bloquea reintento / `Completed` → reimprime / `Failed` → permite reintentar.

### 8.4 Escenario C — Standalone del launcher

```
1. Cajero toca el ícono del APK
2. Intent MAIN/LAUNCHER → App: IsStandalone=true (no IsRefundFromHioPos)
3. App navega a CreditosActivos
4. Cédula → créditos activos → seleccionar crédito → monto → POST payCredit
5. Imprime comprobante localmente (IReceiptPrinter, cadena con fallback)
6. FinishAffinity (sin devolver nada a HioPos)
```

### 8.5 Escenario D — REFUND (nota de crédito) — RECHAZADO

```
1. Cajero solicita reverso en HioPos
2. Intent TRANSACTION con TransactionType=REFUND
3. App: DETECTA REFUND antes que cualquier otra cosa
4. Responde inmediatamente con TransactionResult=FAILED ("Credinet no soporta reversos")
5. Auditoría: AuditActions.RefundRejected
6. HioPos muestra mensaje al cajero.
```

> Crítico: **una nota de crédito NO abre la pantalla de abonos.** Si el cajero
> llegó a seleccionar un crédito y cobrar, se crearía un abono no solicitado.

### 8.6 Escenario E — Operación degradada

- **Sin red al validar cliente** → mensaje de comunicación; reintentar.
- **Sin red al solicitar OTP** → mensaje; reintentar.
- **`create` timeout** → estado `InDoubt`; cajero debe verificar en Sistecrédito
  antes de reintentar (ver §29).
- **`payCredit` timeout** → estado `Pending`; cajero debe verificar saldo.
- **BD local ilegible** (cifrado perdido) → se recrea vacía (solo caché/auditoría).
- **Configuración inválida en producción** → rechazo inmediato con mensaje.

---

## 9. Ambientes (Sandbox, Staging, Producción)

### 9.1 Tres ambientes

| Ambiente | Endpoint | Clave | `Environment` | Uso |
|---|---|---|---|---|
| **Sandbox (pruebas)** | `https://api.credinet.co/pos/` | `88dec4b8617c4644a239a8af283dc742` (clave pública del manual) | `sandbox` | Desarrollo, QA, piloto |
| **Staging / Pre-producción** | (no provisto por Sistecrédito) | (no provisto) | — | No aplica formalmente |
| **Producción** | `https://api.credinet.co/posprod/` | Key real entregada por Sistecrédito (vía ICG/CloudLicense) | `production` | Operación real |

> El APK embebido trae la configuración de **sandbox**. La de producción llega
> por CloudLicense, lo que significa que la credencial de producción **no está
> dentro del artefacto distribuido**.

### 9.2 Cómo distinguir un APK sandbox de uno de producción

| Señal | Sandbox | Producción |
|---|---|---|
| `Environment` en logs | `sandbox` | `production` |
| `BaseUrl` | `.../pos/` | `.../posprod/` |
| `SubscriptionKey` | `__SANDBOX__` (→ clave pública) | Key real |
| `StoreId` | vacío | obligatorio |
| `OtpDestination` | `1` (WhatsApp) | `1` (WhatsApp) |

### 9.3 Instalación de sandbox vs producción en una misma terminal

El `packageName` es el mismo (`com.permoda.sistecreditotef`). **No pueden
convivir.** Para volver a producción hay que reinstalar el otro APK.

### 9.4 Comportamiento esperado por ambiente

- **Sandbox:** cualquier combinación es aceptada. La barrera `ApiConfig.Validate()`
  solo aplica a producción.
- **Producción:** se rechaza la transacción si falta `SUBSCRIPTION_KEY`, si la URL
  no es HTTPS, si `BaseUrl` apunta a sandbox, si se usa la clave de sandbox, o si
  falta `StoreId`.

---

## 10. Configuración y precedencia

### 10.1 Precedencia (gana el primero que exista)

```
1. CloudLicense (CloudConfigStore)  ← ICG lo entrega en el INITIALIZE, persiste on-device
2. appsettings.json                 ← embebido en el APK (sandbox/dev)
3. User Secrets                      ← solo en DEBUG, para la key real en la máquina de dev
4. Fallback en código               ← valores por defecto seguros
```

Implementado en `ApiConfig.FromConfiguration`. `ApiConfigProvider` recarga en
`INITIALIZE` (no se construye una vez y se queda para toda la vida del proceso).

### 10.2 Claves completas de la sección `Credinet:`

| Clave | Default | CloudLicense key | Tipo | Descripción |
|---|---|---|---|---|
| `Environment` | `sandbox` | `ENVIRONMENT` | string | `sandbox` \| `production` |
| `SubscriptionKey` | `__SANDBOX__` (→ `SandboxSubscriptionKey`) | `SUBSCRIPTION_KEY` | string | API key. `__SANDBOX__` = clave de pruebas. |
| `StoreId` | `""` | `STORE_ID` | string | Identificador de tienda en Sistecrédito. Vacío se omite. |
| `StoreName` | `Permoda` | `STORE_NAME` | string | Nombre de la tienda para el voucher. |
| `BaseUrl` | `https://api.credinet.co/pos/` | `API_BASE_URL` | string | `/pos/` sandbox · `/posprod/` producción. |
| `OtpDestination` | `1` | `OTP_DESTINATION` | int | `0` = SMS, `1` = WhatsApp (confirmado por Sistecrédito). |
| `Frequency` | `30` | `FREQUENCY` | int | `30` = mensual · `14` = quincenal. |
| `Source` | `"2"` | `SOURCE` | string | Canal POS (fijo por manual). |
| `AuthMethod` | `1` | `AUTH_METHOD` | int | Método de autorización = OTP. |
| `TimeoutSeconds` | `30` | `TIMEOUT_SECONDS` | int | Timeout total por request (incluye reintentos). |
| `OtpResendCooldownSeconds` | `60` | `OTP_RESEND_COOLDOWN` | int | Cooldown entre reenvíos. |
| `OtpMaxResends` | `3` | `OTP_MAX_RESENDS` | int | Máximo de reenvíos por transacción. |
| `OtpVerifyCooldownSeconds` | `2` | `OTP_VERIFY_COOLDOWN` | int | Cooldown entre intentos de verificación. |
| `OtpMaxVerifyAttempts` | `3` | `OTP_MAX_VERIFY_ATTEMPTS` | int | Tope de intentos de verificación. |
| `CertificatePins` | `[]` | `CERTIFICATE_PINS` | string lista | Pines SPKI (SHA-256, base64). Cadena separada por `,` o `;`. |

Sección `Printing:`:

| Clave | Default | CloudLicense key | Tipo | Descripción |
|---|---|---|---|---|
| `EnableSunmiNative` | `false` | `ENABLE_SUNMI_NATIVE` | bool | Habilita el binding nativo Sunmi (requiere AIDL). |

### 10.3 Variables de entorno (build y firma)

| Variable | Descripción |
|---|---|
| `PERMODA_KEYSTORE_PASS` | Contraseña del keystore (PKCS12: misma que la llave). |
| `PERMODA_KEY_PASS` | Contraseña de la llave privada. |

Propiedades MSBuild opcionales:

| Propiedad | Default | Descripción |
|---|---|---|
| `PermodaKeystore` | `$(MSBuildThisFileDirectory)..\..\permoda-release-v2.keystore` | Ruta al keystore. |
| `AllowUnsignedRelease` | `false` | Si `true`, permite Release sin firma (solo pruebas locales). |

### 10.4 `appsettings.json` embebido (referencia)

```jsonc
{
  "Credinet": {
    "Environment": "sandbox",
    "SubscriptionKey": "__SANDBOX__",
    "StoreId": "",
    "BaseUrl": "https://api.credinet.co/pos/",
    "StoreName": "Permoda",
    "OtpDestination": 1,
    "Frequency": 30,
    "Source": "2",
    "AuthMethod": 1,
    "TimeoutSeconds": 30,
    "OtpResendCooldownSeconds": 60,
    "OtpMaxResends": 3,
    "OtpVerifyCooldownSeconds": 2,
    "OtpMaxVerifyAttempts": 3,
    "CertificatePins": []
  },
  "Printing": { "EnableSunmiNative": false }
}
```

### 10.5 XML de `Parameters` esperado de CloudLicense

```xml
<Configuration>
  <Param Key="API_BASE_URL">https://api.credinet.co/posprod/</Param>
  <Param Key="SUBSCRIPTION_KEY">xxx</Param>
  <Param Key="STORE_ID">xxx</Param>
  <Param Key="STORE_NAME">Permoda</Param>
  <Param Key="ENVIRONMENT">production</Param>
  <Param Key="OTP_DESTINATION">1</Param>
  <Param Key="CERTIFICATE_PINS">AAAA...=;BBBB...=</Param>
  <Param Key="FREQUENCY">30</Param>
  <Param Key="SOURCE">2</Param>
  <Param Key="AUTH_METHOD">1</Param>
  <Param Key="TIMEOUT_SECONDS">30</Param>
  <Param Key="OTP_MAX_RESENDS">3</Param>
  <Param Key="OTP_RESEND_COOLDOWN">60</Param>
  <Param Key="OTP_VERIFY_COOLDOWN">2</Param>
  <Param Key="OTP_MAX_VERIFY_ATTEMPTS">3</Param>
</Configuration>
```

El parser es **agnóstico al namespace** (compara por `LocalName`) y tolerante a
la capitalización del atributo `Key`.

---

## 11. Arquitectura interna y capas

(Ver §5.1 — diagrama de capas)

Las dependencias apuntan hacia adentro (UI → dominio → contratos; nunca al revés).

### 11.1 Modelo de datos en 3 formas

Un mismo concepto viaja en tres representaciones distintas:

| Forma | Ejemplo | Dónde | Por qué |
|---|---|---|---|
| **DTO** | `ClientDto`, `CreditDto` | `Dtos/` | Espejo del JSON de Credinet |
| **Dominio** | `Client`, `Credit` | `Models/` | Records inmutables limpios, independientes del contrato externo |
| **Resultado a HioPos** | `HioposResponse` | `Services/Hiopos/` | Estructura de extras que espera el POS |

La conversión **DTO → Dominio** se hace en `Mappers/CredinetMapper.cs`. Un cambio
de contrato de Credinet se absorbe en un solo lugar.

---

## 12. Patrones de diseño aplicados

| Patrón | Dónde | Para qué |
|---|---|---|
| **MVVM** | `Views/` + `ViewModels/` | Estado y lógica fuera del XAML |
| **Result / Either tipado** | `Common/Result.cs` → `ApiResult<T>` | Manejo de errores sin excepciones entre capas |
| **Repository** | `ICredinetRepository` / `CredinetRepository` | Aísla el dominio del HTTP crudo |
| **Facade** | `SistecreditoService` | Único punto de entrada al dominio |
| **Adapter / Anti-Corruption Layer** | `Mappers/CredinetMapper`, `HioposIntentParser`, `HioposResultBuilder` | Traduce contratos externos |
| **Strategy** | `CredinetHttpPolicies.Select`, `IReceiptPrinter` | Elige política Polly / impresora |
| **Chain of Responsibility** | Pipeline `DelegatingHandler` | Auth → Logging → Polly → Pinning |
| **Decorator (interceptor)** | `AuthInterceptor` | Inyecta header sin tocar cada llamada |
| **Discriminated Union** | `ApiError` (Network/Http/Business), `PaymentOutcome` (Ok/AlreadyPaid/InDoubt/Failure/NetworkUncertain) | Categorizar fallos exhaustivamente |
| **Dependency Injection** | `AppServicesRegistration` | Inversión de control, testeabilidad |
| **Singleton / Transient** | Servicios singleton; VMs y Pages transient | Estado compartido / instancia fresca por navegación |
| **Idempotencia** | `SistecreditoService.CrearCreditoAsync` + `IIdempotencyStore` por `SaleId` | Evita doble crédito |
| **Guard clauses / fail-safe** | `MainActivity.HandleIntent` (try/catch → `Canceled`) | Nunca crashear al POS |
| **DRY entre APKs** | `AppServicesRegistration` + `RouteRegistrar` | Compartidos vía glob del `.csproj` |

---

## 13. Inyección de dependencias y ciclo de vida

### 13.1 Secuencia de arranque (`MauiProgram.CreateMauiApp`)

1. `SQLitePCL.Batteries_V2.Init()` — inicializa el proveedor **SQLCipher**.
2. `builder.Configuration.LoadAppSettingsFromAsset()` — carga `appsettings.json`
   como **EmbeddedResource** (no `AddJsonFile`, porque en el bootstrap de MAUI
   en Android el `IFileSystem`/`AssetManager` aún no existen).
3. `#if DEBUG` → `AddUserSecrets`.
4. `AddSistecreditoSharedServices()` — pipeline HTTP, stores, dominio, VMs, Pages.
5. Registro específico del APK (result handler, audit logger, standalone tracker).
6. `AppLogger.Init(...)`.

### 13.2 Ciclos de vida

- **Singleton:** `ApiConfig`, `ApiConfigProvider`, todo el pipeline HTTP,
  repositorio, stores (token, idempotencia, estado, auditoría), navegación,
  builders de HioPos, throttle de OTP, impresoras, `SistecreditoService`.
- **Transient:** ViewModels y Pages.

### 13.3 Resolución de DI

El contenedor se construye con `ValidateOnBuild` y `ValidateScopes`. Cada servicio
se resuelve y se verifica estructuralmente que **ningún servicio tenga dos
constructores públicos de la misma aridad** que el contenedor no pueda desempatar.

---

## 14. Modelo de datos (DTO · Dominio · HioPos)

### 14.1 Entidades de dominio (`Models/`)

| Tipo | Campos clave |
|---|---|
| `Client` | `DocumentId`, `DocumentType`, `Name`, `AvailableCreditLimit`, `MaxCreditValue`, `Email`, `Phone` |
| `Credit` | `CreditId`, `CreditNumber`, `CreditValue`, `TotalFeeValue`, `EffectiveAnnualRate`, `TotalDownPayment`, `Months`, `TypeDocument`, `IdDocument` |
| `CreditDetails` | `Months`, `TotalFeeValue`, `EffectiveAnnualRate`, `AvalValue`, `AdminValue` |
| `CreditToken` | `Token` |
| `ActiveCredit` | `CreditId`, `CreditNumber`, `CreditValue`, `Balance`, `Months`, `Frequency` |
| `Payment` | `PaymentId`, `PaymentNumber`, `CreditId`, `CreditValuePaid`, `InterestValuePaid`, `ArrearsValuePaid`, `InsuranceValuePaid`, `ChargesValuePaid`, `DatePaid` |
| `SimulatedMonthLimit` | `Months` |
| `SaleDocument` | `SaleId`, `DocumentId`, `TransactionId`, `IssuerNit`, `SellerName`, `ProductDescriptions`, `Currency`, `AmountCents` |

### 14.2 DTOs (`Dtos/`)

Espejo exacto del JSON de Credinet. Ejemplo `ClientDto`:

```csharp
public sealed record ClientDto {
    public string? typeDocument { get; init; }
    public string? idDocument { get; init; }
    public string? name { get; init; }
    public double? availableCreditLimit { get; init; }
    public double? maxCreditValue { get; init; }
    public string? email { get; init; }
    public string? phone { get; init; }
}
```

### 14.3 Sobre de respuesta `ApiResponse<T>`

```csharp
public sealed record ApiResponse<T> {
    public T? Data { get; init; }
    public int ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? Function { get; init; }
    public string? Country { get; init; }
}
```

### 14.4 Errores `ApiError`

```csharp
public abstract record ApiError {
    public abstract string UserMessage { get; }
    public sealed record Network(string Detail, Exception? Cause) : ApiError;
    public sealed record Http(int Code, string Detail) : ApiError;
    public sealed record Business(int Code, string Detail) : ApiError;
    public sealed record Local(string Detail) : ApiError;
}
```

### 14.5 `PaymentOutcome` (resultado de un abono)

```csharp
public abstract record PaymentOutcome {
    public sealed record Ok(Payment Payment) : PaymentOutcome;
    public sealed record AlreadyPaid(Payment Payment) : PaymentOutcome;
    public sealed record InDoubt(CachedPayment Pending) : PaymentOutcome;
    public sealed record Failure(ApiError Error) : PaymentOutcome;
    public sealed record NetworkUncertain(ApiError Error, string PaymentKey) : PaymentOutcome;
}
```

---

## 15. Persistencia local cifrada

### 15.1 Motor y llave

- **SQLite cifrado con SQLCipher** (`SQLitePCLRaw.bundle_e_sqlcipher`).
- **Llave:** 32 bytes aleatorios generados por `RandomNumberGenerator` en el
  primer arranque.
- **Custodia de la llave:** `SecureStorage` de MAUI → Android Keystore. Nunca
  está en código ni en disco en claro.

### 15.2 Resiliencia ante fallos del Keystore

- Distingue **"no había llave"** de **"no pude leer la llave"** (timeout).
- Si `SecureStorage` tarda >3s, se reintenta con backoff y se opera con una llave
  provisional marcada como **no autoritativa**.
- La BD **no se elimina** ante timeouts. Solo se recrea cuando la llave es
  autoritativa Y el error es compatible con "no descifrable" (`SQLITE_NOTADB`,
  "file is encrypted", "malformed"). Disco lleno o BD bloqueada NO disparan borrado.
- Toda recreación queda registrada como error.

### 15.3 Qué se persiste cifrado

- `cached_transactions` (idempotencia de créditos) — `CreditJson` completo.
- `cached_payments` (idempotencia de abonos) — `PaymentJson` + estado.
- `audit_log` (auditoría financiera) — cédula enmascarada.

### 15.4 Retención

- **Idempotencia y auditoría:** purga a los **180 días** (corre en background al recibir `INITIALIZE`).
- **Comprobantes PDF en caché:** **24 horas**.

---

## 16. Idempotencia de créditos y abonos

### 16.1 Doble barrera en créditos

| Barrera | Mecanismo |
|---|---|
| **Local** | SQLite cifrado, indexado por `SaleId` |
| **Remota** | `invoice=SaleId` enviado a Credinet → evita `errorCode 252 DuplicatedCredit` |

Si HioPos reintenta la misma venta, el módulo devuelve el crédito **ya creado
con todos sus datos financieros** (TEA, cuota inicial, cuota mensual, plazo) —
el comprobante reimpreso es idéntico al original.

### 16.2 Idempotencia persistente en abonos

El intento de abono se persiste **antes** del POST a Credinet, con estados:

| Estado | Significado | Comportamiento ante reintento |
|---|---|---|
| `Pending` | Enviado, resultado desconocido | **Bloquea** el reintento (cajero verifica saldo) |
| `Completed` | Credinet confirmó | **Reimprime** el comprobante; no cobra de nuevo |
| `Failed` | Rechazo de negocio | **Permite** reintentar |

**Ventana:** 30 minutos. **Clave:** `creditId + amountCents`.

**Garantía ante reinicio del POS:** la barrera persiste en SQLite cifrado.
Sobrevive a presión de memoria, reinicio del terminal, cierre de HioPos.

---

## 17. Auditoría y trazabilidad

### 17.1 Doble destino

| Destino | Mecanismo | Privacidad |
|---|---|---|
| **Broadcast al POS** | `sendBroadcast(icg.actions.externalApi.AUDIT)` | Cédula enmascarada (sale del sandbox) |
| **SQLite cifrado** | `SqliteAuditLog` (persistente) | Cédula enmascarada |

### 17.2 Acciones auditadas

`ValidateOk`, `ValidateFail`, `Simulate`, `TokenRequest`, `CreditCreated`,
`CreditFail`, `Payment`, `PaymentFail`, `RefundRejected`, `ConfigError`.

### 17.3 Límites del contrato ICG

- `Action`: ≤50 caracteres.
- `Comment`: ≤1.050 caracteres.

### 17.4 Retención

180 días, purga en background al `INITIALIZE`.

---

## 18. OTP — Solicitud, throttle y verificación

### 18.1 Canal

`OtpDestination`: **1 = WhatsApp** (confirmado por Sistecrédito para test y
producción). `0` = SMS. El texto del voucher y la pantalla de OTP derivan el
canal de la configuración, por lo que siempre coinciden con el canal real.

### 18.2 Throttle (singleton, sobrevive a navegación)

| Control | Default | Configurable |
|---|---|---|
| Cooldown entre reenvíos | 60 s | `OtpResendCooldownSeconds` / `OTP_RESEND_COOLDOWN` |
| Máximo de reenvíos | 3 | `OtpMaxResends` / `OTP_MAX_RESENDS` |
| Máximo de intentos de verificación | 3 | `OtpMaxVerifyAttempts` / `OTP_MAX_VERIFY_ATTEMPTS` |
| Cooldown entre intentos de verificación | 2 s | `OtpVerifyCooldownSeconds` / `OTP_VERIFY_COOLDOWN` |

### 18.3 Reglas verificadas

- El tope de reenvíos se informa de inmediato al alcanzarse.
- Un código nuevo reinicia los intentos de verificación pero **no** el contador
  de reenvíos.
- Los intentos **no bajan de cero**.
- Un fallo de **infraestructura** (red, timeout) **no consume** un intento.
- Thread-safe (verificado con 8 hilos concurrentes).
- El throttle se reinicia al iniciar cada transacción nueva.

### 18.4 Detección de OTP reutilizado por Credinet

Si Credinet reutiliza un código (`errorCode=230 TokenAlreadyUsed`), la app lo
muestra explícitamente al cajero. Ver `OtpTokenReuse`.

---

## 19. Manejo de dinero y redondeo

### 19.1 Reglas

- Toda conversión entre pesos y centavos pasa por **una única clase** (`Common/Money.cs`).
- La aritmética se hace en **`decimal`** (no `double`).
- El redondeo es **`MidpointRounding.AwayFromZero`** (convención contable colombiana).
- El formato hacia el POS y la DIAN usa **cultura invariante**.

### 19.2 Pruebas exhaustivas

20.001 conversiones de ida y vuelta (0..200 pesos) + equivalencia entre todos
los caminos de conversión del código.

### 19.3 Coherencia del monto reportado al POS

El módulo reporta al POS el valor **realmente financiado o pagado**, coherente
con lo que ve la DIAN, y **compara** contra el `Amount` del Intent. Una diferencia
> 1 peso se registra en log y auditoría como descuadre, identificable por el
`SaleId`. No se cancela la operación: el crédito ya existe en Credinet y
cancelarlo dejaría al cliente con una obligación sin factura.

---

## 20. Comprobantes, voucher y módulo fiscal DIAN

### 20.1 Reglas del voucher

- **Sin encabezado de empresa, NIT ni líneas de producto** (es un voucher, no factura).
- **Un único `CUT_PAPER`** al final.
- Toda línea de texto va acompañada del bloque `<Formats>` que exige el parser de HioPos.
- Campos numéricos sin separador de miles, cultura invariante.
- Identificadores normalizados (sin guiones/espacios, truncados a 40, con relleno
  a 6 dígitos si vienen vacíos).
- `TransactionData` ≤200 caracteres (límite del módulo fiscal).

### 20.2 Reglas del `ModifyDocumentResult`

- Solo toca `PaymentMeans` (no líneas de producto, datos de empresa ni totales).
- Si pareciera un documento nuevo, HioPos lo reenviaría a la DIAN → doble envío.

### 20.3 Cédula enmascarada en el comprobante de abonos

El comprobante de abonos lleva la cédula enmascarada (el papel puede quedar en
el mostrador). El voucher de crédito sí lleva la cédula completa, porque es el
documento con el que el titular firma su obligación.

---

## 21. Impresión local (abonos standalone)

### 21.1 Cadena de impresión

`CompositeReceiptPrinter` recorre los printers en orden:

| Orden | Printer | Disponibilidad |
|---|---|---|
| 1 | `SunmiPrinter` | Solo si AIDL real instalado + `EnableSunmiNative=true` |
| 2 | `AndroidPrintPrinter` | Cualquier Android (PDF para rollo 80 mm) |
| 3 | `PdfReceiptPrinter` (fallback) | Application/pdf por FileProvider |

Si uno falla (`false` o lanza), pasa al siguiente. Si todos fallan, se reporta
`false` (no un éxito falso) y se le muestra al cajero un diálogo "Reintentar /
Continuar sin imprimir".

### 21.2 PDF generado (`ThermalPdfWriter`)

- Sin dependencias externas: Type1 Courier + stream.
- Tamaño de papel: rollo 80 mm con márgenes mínimos.
- 42 columnas, alineación derecha para montos.
- Escapado correcto de `(`, `)`, `\` y caracteres acentuados (José Muñoz Ñandú).
- `xref` con offsets reales.

### 21.3 Layout único

Los tres printers consumen el mismo `ReceiptTextBuilder` con las **42 columnas**
del voucher de HioPos.

---

## 22. Manejo de errores y mensajes al cajero

### 22.1 Regla

**Nunca exponer mensajes de excepción crudos** al cajero (revelarían hosts,
rutas o detalles de red). Se traduce a mensajes accionables mediante
`FriendlyMessage`.

### 22.2 Categorías

- **Red:** "No se pudo comunicar con Sistecrédito. Verifica la conexión."
- **Negocio:** texto específico por `errorCode` mapeado.
- **Configuración:** "El módulo no está configurado correctamente. Avisa al
  área de sistemas." (rechaza la transacción en producción).
- **Ambiente:** "Configuración inválida" (rechaza la transacción).
- **Local:** mensaje del propio módulo (e.g., "El crédito X ya fue creado…").

### 22.3 Asimetrías eliminadas

Las pantallas terminales (`ConfirmacionViewModel`, `ReciboPagoViewModel`)
**siempre responden** al POS, con `try/catch` simétrico.

---

## 23. Resiliencia y reintentos (Polly)

### 23.1 Política conservadora

| Regla | Detalle |
|---|---|
| Solo fallos transitorios | `HttpRequestException`, `5xx`, `408` |
| Solo métodos seguros | Únicamente `GET` |
| Exclusión explícita | `getCreditToken` (dispararía OTP), todo `POST` (duplicaría) |
| Volumen | 2 reintentos, backoff 250 ms y 500 ms |

### 23.2 Implementación

`CredinetHttpPolicies.Select(request)` decide la política por request.

### 23.3 Timeout global

30 s por defecto (`Credinet:TimeoutSeconds`), techo de toda la operación
**incluidos los reintentos**.

---

## 24. Seguridad del transporte (HTTPS, certificate pinning)

### 24.1 HTTPS obligatorio

`ApiConfig.Validate()` rechaza cualquier `BaseUrl` que no empiece por `https://`.

### 24.2 Certificate pinning SPKI

- Se pinea el **SubjectPublicKeyInfo** (SHA-256, base64), no el certificado
  completo: la llave pública sobrevive a la rotación del certificado.
- Se acepta pinear el certificado hoja o cualquier elemento de la cadena.
- Lista vacía = TLS estándar (no rompe nada).
- Pines configurables por `CERTIFICATE_PINS` en CloudLicense.

### 24.3 Propiedades verificadas

- Cadena TLS inválida se rechaza aunque el pin coincida.
- Pin incorrecto → rechazo (caso MITM con CA corporativa).
- Múltiples pines soportados (práctica correcta para rotación).
- Lista vacía → TLS estándar.

---

## 25. Protección de datos personales (Habeas Data)

Marco aplicable: **Ley 1581 de 2012** (Colombia).

### 25.1 Enmascarado centralizado (`PiiMask`)

| Dato | Tratamiento | Ejemplo |
|---|---|---|
| Cédula | Últimos 4 dígitos | `1026260942` → `******0942` |
| Documentos ≤4 caracteres | Enmascarado completo | `1234` → `****` |
| Nombre | Iniciales | `Juan Perez` → `J. P.` |
| Cédula en URL | Enmascarada en query | `idDocument=******0942` |

### 25.2 En logs

**Nunca** aparece:
- `Ocp-Apim-Subscription-Key` (sustituida por `***REDACTED***`).
- Header `Authorization`.
- Cédula en URL, cuerpo de request o cuerpo de response.
- OTP (`token` sustituido por `***REDACTED***`).

### 25.3 Trazas de diagnóstico

Compiladas bajo `#if DEBUG` → no existen en producción.

### 25.4 Comprobante

- Abono: cédula enmascarada.
- Crédito: cédula completa (es el documento que firma el titular).

---

## 26. Cifrado en reposo (SQLCipher)

(Ver §15.) Resumen:

- Motor: SQLite con SQLCipher.
- Llave: 32 bytes aleatorios.
- Custodia: `SecureStorage` → Android Keystore.
- Datos cifrados: cédulas, `creditId`, montos.
- Ubicación: directorio privado de la app.
- Resiliencia: ver §15.2.

---

## 27. Gestión de credenciales

### 27.1 `Ocp-Apim-Subscription-Key` (Credinet)

| Aspecto | Implementación |
|---|---|
| Origen en producción | CloudLicense (no está dentro del APK) |
| Almacenamiento | `SecureStorage` (Android Keystore) |
| Disponibilidad | Caché en memoria; persistida en segundo plano |
| Inyección | `AuthInterceptor` (DelegatingHandler) — lee configuración **vigente** |
| En logs | **Nunca** |
| Rotación | Sin recompilar: ICG actualiza y el módulo recarga en el siguiente `INITIALIZE` |

### 27.2 Token de sesión de HioPos

`Preferences` (privado). Se limpia en `FINALIZE`. No es credencial de acceso a Credinet.

### 27.3 Llave de cifrado de BD

`SecureStorage` → Android Keystore. Nunca en código ni disco en claro.

### 27.4 Key de sandbox

`88dec4b8617c4644a239a8af283dc742` (clave pública del manual). Solo aplica al
ambiente de pruebas. El control de §9 impide que un POS de producción la use.

---

## 28. Aislamiento de plataforma Android

### 28.1 Permisos declarados

| Permiso | Justificación |
|---|---|
| `INTERNET` | HTTPS a Credinet |
| `ACCESS_NETWORK_STATE` | Diagnóstico |
| `READ_EXTERNAL_STORAGE` (maxSdk 32) | Residual, no usado |
| `WRITE_EXTERNAL_STORAGE` (maxSdk 29) | Residual, no usado |
| `com.pos2pay.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` | Nivel signature (AndroidX) |

**Sin permisos peligrosos** (cámara, contactos, ubicación, SMS, teléfono).

### 28.2 Otras garantías

| Control | Valor |
|---|---|
| `allowBackup` | `false` |
| `debuggable` (Release) | ausente |
| `FileProvider` | `exported=false`, `grantUriPermissions=true` |
| Rutas compartidas | solo `cache-path` y `files-path` |
| `<queries>` | 2 entradas (servicio de impresora, apps que reciben PDF) |
| `launchMode` | `singleTask` |

---

## 29. Resolución de transacciones en duda

### 29.1 Escenario

`POST /create` no es reintentable. Si responde con timeout **después** de haber
creado el crédito en Sistecrédito, el cliente tiene un crédito real, el cajero
cree que falló y nadie lo detecta hasta la conciliación.

### 29.2 Cómo lo maneja el módulo

- Ante `NetworkException` en `create`, **no** se reporta "código OTP inválido".
- Se le indica al cajero: **"Verifica en Sistecrédito si el crédito se creó"**.
- Se ofrece un flujo de cierre manual (`ResolverAbonoEnDudaAsync`).
- El intento queda registrado en la BD local con su estado final.

### 29.3 Para abonos

- El intento se persiste como `Pending`.
- El siguiente cobro del mismo `creditId + amountCents` se **bloquea** (no se
  reintenta a ciegas).
- El cajero verifica en Sistecrédito y resuelve manualmente.

---

## 30. Fail-safe: nunca dejar al POS esperando

### 30.1 Garantía de respuesta

HioPos espera un `setResult` para cerrar la venta. El módulo garantiza respuesta
en todos los caminos:

- Cada handler de Intent está envuelto en `try/catch` → `Canceled` ante excepción,
  con un segundo `try/catch` para el propio finish.
- Las pantallas terminales **siempre** responden: si no hay operación que reportar
  o armar la respuesta falla, devuelven `Failed` explícito con mensaje accionable.
- Documento malformado **no cancela la venta**: continúa sin autocompletar la cédula.

### 30.2 Degradación segura

| Componente | Si falla |
|---|---|
| BD local de idempotencia | Continúa; registra que corrió sin barrera local |
| Persistencia de auditoría | Best-effort; no interrumpe la operación |
| `SecureStorage` | No destruye datos (§15.2) |
| Impresión local | Cajero recibe aviso; puede reintentar o continuar |
| Inicialización de BD | Reintenta en la siguiente operación |

---

## 31. Estrategia de pruebas

### 31.1 Stack

- **xUnit** + **fakes escritos a mano** (sin Moq).
- TFM `net10.0` (los tests son de lógica pura, no necesitan Android).
- Globs sobre la capa pura (excluye `Platforms/`, `Views/`, printers, SQLite).

### 31.2 Cobertura (228/228)

| Área | Pruebas clave |
|---|---|
| Grafos de DI | Resolución con `ValidateOnBuild`, sin constructores ambiguos |
| `Money` | 20.001 conversiones, equivalencia de caminos |
| `PiiMask` | Cédulas de cualquier longitud |
| Certificate pinning | Cadena inválida, pin incorrecto, lista vacía, múltiples pines |
| Redacción en logs | Key, cédula, OTP |
| Control de ambiente | Precedencia, producción con sandbox, URL, StoreId |
| CloudConfigParser | XML con/sin namespace, capitalización |
| `ApiConfig` | Validación de coherencia |
| `OtpRequestThrottle` | Cooldown, topes, no consumir por red, thread-safety |
| Idempotencia de abonos | Replay, reinicio, red, negocio, abonos legítimos |
| Idempotencia de créditos | Replay completo, cache incompleto |
| Dinero | Redondeo comercial, cultura invariante, ida-vuelta |
| HioPos | 17 flags, voucher no-factura, un solo CUT_PAPER |
| DianFieldSanitizer | Normalización, truncado, relleno |
| Impresión | 19 tests: layout, columnas, PDF estructural, escapado |

### 31.3 Pendientes (requieren hardware)

- Impresión nativa Sunmi (AIDL oficial).
- Ciclo completo `INITIALIZE → GET_BEHAVIOR → TRANSACTION → setResult` en terminal real.
- Matar el proceso entre `create` y voucher; confirmar no duplicación.
- Reinstalación sobre APK del piloto (cambio de firma).
- Sesión de 8 horas con ventas periódicas (rotación de handlers, fugas de CTS).

---

## 32. Build, firma y empaquetado

### 32.1 Compilar

```bash
dotnet build src/SistecreditoTEF.Maui -f net10.0-android
dotnet build src/SistecreditoTEF.Maui -f net10.0-android -t:Run
```

### 32.2 Pruebas

```bash
dotnet test tests/SistecreditoTEF.Tests
```

### 32.3 Dependencias

```bash
dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable
```

### 32.4 Crear keystore (una sola vez)

```bat
keytool -genkeypair -v -keystore permoda-release-v2.keystore ^
  -alias sistecredito -keyalg RSA -keysize 2048 -validity 10000
```

> **PKCS12:** la misma contraseña para keystore y llave privada.

### 32.5 Variables de entorno

```powershell
setx PERMODA_KEYSTORE_PASS "<clave>"
setx PERMODA_KEY_PASS      "<clave>"
```

> `setx` no afecta la terminal actual; abrir una nueva.

### 32.6 Release firmado

```powershell
dotnet publish src/SistecreditoTEF.Maui -c Release -f net10.0-android
```

### 32.7 Verificar la firma (obligatorio)

```powershell
keytool -printcert -jarfile <ruta-al-apk>-Signed.apk
# Propietario debe decir: CN=SistecreditoTEF, OU=Permoda, O=Permoda, ...
# SHA-1 esperado: 7d1424275849081e1f569cdec675d527d7cccbce
# Si dice "CN=Android Debug" -> NO DISTRIBUIR
```

### 32.8 Garantías del proceso de build

- **`ValidarFirmaDeRelease`:** un Release sin firma real **falla** el build.
- **`ForzarRefirmaEnRelease`:** borra el APK firmado previo para que la firma
  se aplique siempre (target `_Sign` de .NET Android es incremental).
- **Escape:** `-p:AllowUnsignedRelease=true` con warning visible.

---

## 33. Despliegue, alta y distribución

### 33.1 Generar el artefacto

(Ver §32.)

### 33.2 Verificar el artefacto

(Ver §32.7.) El SHA-1 debe ser **`7d1424275849081e1f569cdec675d527d7cccbce`**.

### 33.3 Instalar

| Situación | Procedimiento |
|---|---|
| Sin el módulo instalado | `adb install com.permoda.sistecreditotef-Signed.apk` |
| Con APK firmado con otro certificado | `adb uninstall com.permoda.sistecreditotef` y reinstalar |

> Desinstalar elimina los datos locales (idempotencia y auditoría). No hacerlo
> con una venta o un abono en curso.

### 33.4 Configurar en CloudLicense

Provisionar `API_BASE_URL` (`/posprod/`), `SUBSCRIPTION_KEY`, `STORE_ID`,
`STORE_NAME`, `ENVIRONMENT=production`. Cuando estén disponibles:
`CERTIFICATE_PINS`. Si algo falta o es incoherente, el módulo rechaza la
transacción con un mensaje explícito al cajero.

### 33.5 Confirmar en el terminal

1. HioPos lanza el módulo (valida package + APK Name + SHA-1 en ICG).
2. Ciclo de facturación completo, factura cuadra.
3. Abono desde el ícono, comprobante se imprime.
4. Con venta HioPos abierta: ícono de abonos bloqueado.
5. Con venta HioPos cerrada: ícono de abonos accesible.

---

## 34. Inventario de datos sensibles y retenciones

| Dato | En tránsito | En reposo | En logs | Retención |
|---|---|---|---|---|
| Cédula | HTTPS | Cifrada SQLCipher | Enmascarada | 180 días |
| Nombre | HTTPS | Cifrado | Enmascarado/ausente | 180 días |
| Celular | HTTPS | No persistido | No registrado | — |
| Cupo / saldo | HTTPS | Cifrados | Montos agregados | 180 días |
| `creditId` / `paymentId` | HTTPS + extras | Cifrados | Sí (no son PII) | 180 días |
| **OTP** | HTTPS | **No persistido** | **Redactado** | — |
| **API Key** | Header | `SecureStorage` | **Redactada** | Hasta próximo `INITIALIZE` |
| Token HioPos | Extra | `Preferences` | No registrado | Hasta `FINALIZE` |
| Llave cifrado BD | — | `SecureStorage` | Nunca | Permanente |
| Comprobante PDF | — | Caché privada | — | 24 h |

---

## 35. Cadena de suministro y dependencias

### 35.1 Paquetes clave

| Paquete | Versión | Justificación |
|---|---|---|
| `Microsoft.Maui.Controls` | 10.0.20 | UI |
| `CommunityToolkit.Mvvm` | 8.3.2 | MVVM |
| `Microsoft.Extensions.Http` + `.Polly` | 10.0.0 | HTTP + resiliencia |
| `Microsoft.Extensions.Configuration.Json` + `.UserSecrets` | 10.0.0 | Config |
| `sqlite-net-pcl` | 1.9.172 | ORM ligero |
| `SQLitePCLRaw.bundle_e_sqlcipher` | 2.1.11 | Bundle con SQLCipher |
| `SQLitePCLRaw.lib.e_sqlite3.android` | **2.1.12** | Fija vulnerabilidad alta GHSA-2m69-gcr7-jv3q |
| `SQLitePCLRaw.core` | **2.1.12** | Fija vulnerabilidad alta |

### 35.2 Vulnerabilidades

Verificar con `dotnet list package --vulnerable --include-transitive` antes de
cada release. **0 paquetes vulnerables** (estado actual).

### 35.3 Secretos en el repo

`.gitignore` cubre `*.keystore`, `*.jks`, `signing.props`, `appsettings.*.json`.

### 35.4 Recomendación

`dotnet test` y `dotnet list package --vulnerable` como **gates de CI**.

---

## 36. Riesgos, hallazgos heredados y deuda técnica

### 36.1 Críticos (resueltos)

| # | Hallazgo | Resolución |
|---|---|---|
| C-1 | APK firmado con llave de depuración | Ruta corregida, build falla si no firma, refirma forzada |
| C-2 | Tests no se ejecutaban | TFM `net10.0`, 228/228 pasan |
| C-3 | POS en producción operaba en sandbox | `CloudConfigParser` agnóstico a namespace; validación fuerte |
| C-4 | Replay de idempotencia con voucher en ceros | `Credit` completo persistido |
| C-5 | Doble cobro de abonos | Idempotencia persistente con estados `Pending`/`Completed`/`Failed` |

### 36.2 Altos (resueltos)

A-1 PII en logs · A-2 cédula en auditoría · A-3 key en `Preferences` → `SecureStorage`
· A-4 vulnerabilidad SQLitePCLRaw · A-5 pinning activable por CloudLicense · A-6
timeout de `SecureStorage` no destruye BD · A-7 `Finalizar` siempre responde ·
A-8 aritmética única en `decimal` · A-9 `Amount` validado contra Intent · A-10
transacciones en duda · A-11 debounce real · A-12 tope OTP en singleton.

### 36.3 Medios (resueltos)

M-1 a M-19 + B-1 a B-14 — todos corregidos y verificados por tests.

### 36.4 Pendientes menores

- `XA0141` (alineación 16 KB) — depende de SQLitePCLRaw.
- Quitar `<AndroidR8Mode>` (propiedad inexistente, no-op).
- Mover keystore v1 fuera del directorio de build.
- Rotar contraseñas del keystore (compartidas por chat durante la sesión).
- Extraer un proyecto `SistecreditoTEF.Core` (`net10.0`) para tests más limpios.

---

## 37. Runbooks operativos

### 37.1 "La app no se levanta desde HioPos"

1. Verificar en logcat: ¿qué action llegó?
2. Si no llega ningún Intent: **el `apk_name` no coincide** con el alta en HioPosCloud.
   Comparar `HioposActions.ApkName` con la configuración de ICG.
3. Si llega `GET_VERSION` y HioPos ofrece "actualizar el módulo": el
   `ModuleVersion` no coincide con la versión registrada en ICG.
4. Si llega `GET_BEHAVIOR` y luego no se lanza `TRANSACTION`: posiblemente falta
   la asignación del módulo al POS en CloudLicense.

### 37.2 "El cajero ve OTP no llega"

1. Verificar `OtpDestination`: 1=WhatsApp, 0=SMS.
2. Confirmar con Sistecrédito que el celular del cliente es válido.
3. Confirmar que el cliente tiene WhatsApp si `OtpDestination=1`.

### 37.3 "Los abonos no imprimen"

1. Verificar cadena de impresión: `SunmiPrinter` → `AndroidPrintPrinter` → `PdfReceiptPrinter`.
2. Revisar logs: ¿algún printer reportó `false`?
3. Si `AndroidPrintPrinter` falla: ¿el print service del POS está disponible?
4. Si `PdfReceiptPrinter` falla: ¿hay permisos de almacenamiento?
5. El cajero puede "Reintentar" o "Continuar sin imprimir".

### 37.4 "Una transacción queda en duda"

1. NO reintentar a ciegas.
2. Cajero verifica en Sistecrédito si el crédito/pago se creó.
3. Cajero usa el flujo de resolución manual de la app (`ResolverAbonoEnDudaAsync`).

### 37.5 "El APK se instaló pero no arranca"

1. Verificar firma: `keytool -printcert -jarfile`.
2. Si dice `CN=Android Debug`: el build no firmó con el keystore de producción.
   Revisar variables de entorno y permisos del keystore.

### 37.6 "Los tests fallan"

1. Verificar TFM: debe ser `net10.0`.
2. Verificar que `appsettings.json` esté disponible (es EmbeddedResource).
3. Ejecutar `dotnet test tests/SistecreditoTEF.Tests` desde la raíz.

### 37.7 "El POS opera en sandbox en producción"

1. **No debería ocurrir** (barrera `ApiConfig.Validate()`).
2. Si ocurre: revisar `CloudConfigParser` (¿el XML trae namespace?).
3. Verificar que el `count` de parámetros en `CloudConfigStore.SaveFromXml` sea > 0.
4. Revisar logs: `CloudConfigStore` registra ERROR si llegó XML sin parámetros reconocibles.

---

## 38. Checklist de despliegue

### 38.1 Pre-build

- [ ] Keystore `permoda-release-v2.keystore` presente.
- [ ] Variables `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS` definidas (iguales).
- [ ] `dotnet test tests/SistecreditoTEF.Tests` pasa (228/228).
- [ ] `dotnet list package --vulnerable` muestra 0 paquetes.
- [ ] Branch limpio, versionCode actualizado.

### 38.2 Build

```powershell
dotnet publish src/SistecreditoTEF.Maui -c Release -f net10.0-android
```

### 38.3 Post-build (obligatorio)

- [ ] `keytool -printcert -jarfile <apk>` → SHA-1 = `7d142427…`.
- [ ] `apksigner verify --print-certs <apk>` → Verifies, v2+v3.
- [ ] Propietario NO es `CN=Android Debug`.

### 38.4 Alta en ICG (CloudLicense)

- [ ] `packageName` = `com.permoda.sistecreditotef`.
- [ ] `apk_name` = `permoda`.
- [ ] SHA-1 del certificado = `7d142427…`.
- [ ] Módulo asignado al POS.

### 38.5 CloudLicense (parámetros)

- [ ] `API_BASE_URL` = `https://api.credinet.co/posprod/`.
- [ ] `SUBSCRIPTION_KEY` = key real de Sistecrédito.
- [ ] `STORE_ID` = StoreId real de Permoda.
- [ ] `STORE_NAME` = nombre de la tienda.
- [ ] `ENVIRONMENT` = `production`.
- [ ] `OTP_DESTINATION` = `1` (WhatsApp).
- [ ] `CERTIFICATE_PINS` = pines SPKI (cuando estén disponibles).
- [ ] Whitelist de IPs públicas de los POS en Sistecrédito.

### 38.6 Instalación

- [ ] En terminales del piloto (firma v1): desinstalar antes.
- [ ] `adb install -r com.permoda.sistecreditotef-Signed.apk`.

### 38.7 Smoke test

- [ ] HioPos lanza el módulo.
- [ ] Ciclo de facturación completo, factura cuadra.
- [ ] Abono desde el ícono, comprobante se imprime.
- [ ] Ícono de abonos bloqueado con venta HioPos abierta.
- [ ] Reintento de la misma venta: no se duplica crédito.
- [ ] Reintento de un abono reciente: reimprime, no cobra.

---

## 39. Pendientes que dependen de terceros

| # | Elemento | De quién | Estado del módulo |
|---|---|---|---|
| 1 | **Pines SPKI de `api.credinet.co`** | Sistecrédito | Mecanismo listo. Activar con `CERTIFICATE_PINS` |
| 2 | **Campo de idempotencia en `payCredit`** | Sistecrédito | Barrera local cubre reinicio. Doble POS no cubierto |
| 3 | **AIDL oficial Sunmi** | Sunmi / Permoda | Android Print funcional. Nativo opcional |
| 4 | **Alta en CloudLicense por terminal** | ICG | SHA-1 `7d142427…` |
| 5 | **Alineación 16 KB** | Mantenedores SQLitePCLRaw | Android 16 lo exigirá |

---

## 40. Decisiones de arquitectura (ADR)

| # | Decisión | Motivo |
|---|---|---|
| 1 | **MAUI solo Android** | HioPos corre en POS Android; otras TFM son ruido |
| 2 | **`HttpClient` manual, sin Refit** | Control total sobre timeouts, headers, query building |
| 3 | **`ApiResult<T>` en vez de excepciones** | Errores explícitos y exhaustivos; `switch` en VMs |
| 4 | **Idempotencia doble** | Evitar doble crédito/pago si HioPos o la red reintentan |
| 5 | **Reintentos Polly solo en GET seguros** | Nunca duplicar escrituras ni disparar OTP de más |
| 6 | **SQLCipher** | Cédulas y `creditId` cifrados en disco |
| 7 | **Config por precedencia** | API key de producción no queda dentro del APK |
| 8 | **`appsettings.json` como EmbeddedResource** | `IFileSystem`/`AssetManager` no existen en bootstrap MAUI |
| 9 | **AOT desactivado** | Iteración rápida; JIT es suficiente para POS |
| 10 | **Firma condicional + escape** | Keystore y claves nunca se commitean |
| 11 | **DI y rutas compartidas** | Evitar desincronización entre APKs |
| 12 | **Nunca crashear al POS** | Fail-safe degrada a `Canceled` |
| 13 | **Un `apk_name` único** | Evitar conflicto con `com.permoda.tefogloba` |
| 14 | **Módulo único (no dos APKs)** | TEF + Abonos conviviendo, con guard de modos |
| 15 | **Canal OTP = WhatsApp** | Confirmado por Sistecrédito |
| 16 | **No reescribir `TransactionType` en recaudo** | Evita que HioPos relance con nuevo `TransactionId` |

---

## 41. Convenciones de código y archivos clave

### 41.1 Convenciones

- C# `LangVersion=latest`, `Nullable=enable`.
- Records inmutables para DTOs y dominio.
- `[ObservableProperty]` y `[RelayCommand]` (CommunityToolkit.Mvvm).
- `switch` exhaustivo sobre `ApiResult<T>` (no `if/else` encadenados).
- Un solo `Money.cs` para conversión de dinero (no `MoneyConverter` ni `ToCents`).
- Un solo `PiiMask.cs` para enmascarado (no helpers locales).
- `Common/FriendlyMessage` para mensajes al usuario.
- Constantes HioPos siempre vía `HioposActions` (nunca literales).

### 41.2 Archivos clave (mapa rápido)

| Responsabilidad | Archivo |
|---|---|
| Action prefix, flags, extras | `Services/Hiopos/HioposConstants.cs` |
| Entry point Android | `Platforms/Android/MainActivity.cs` |
| SetResult a HioPos | `Platforms/Android/AndroidTransactionResultHandler.cs` |
| Broadcast auditoría | `Platforms/Android/BroadcastAuditLogger.cs` |
| Parseo Intent → HioposTransaction | `Services/Hiopos/HioposIntentParser.cs` |
| Build del `setResult` | `Services/Hiopos/HioposResultBuilder.cs` |
| Voucher XML | `Services/Hiopos/ReceiptBuilder.cs` |
| `ModifyDocumentResult` (DIAN) | `Services/Hiopos/ModifyDocumentResultBuilder.cs` |
| Cliente HTTP Credinet | `Services/Credinet/CredinetApiClient.cs` |
| Configuración con precedencia | `Services/Credinet/ApiConfig.cs` + `ApiConfigProvider.cs` |
| Recarga en runtime | `Services/Credinet/ApiConfigProvider.cs` |
| Polly | `Services/Credinet/CredinetHttpPolicies.cs` |
| Certificate pinning | `Services/Credinet/CertificatePinning.cs` |
| Auth header | `Services/Credinet/AuthInterceptor.cs` |
| Logging con redacción | `Services/Platform/HttpLoggingHandler.cs` |
| SQLite cifrado | `Services/Platform/SecureDb.cs` |
| Idempotencia | `Services/Platform/IIdempotencyStore.cs` (impl: SQLite) |
| Auditoría | `Services/Platform/SqliteAuditLog.cs` + `IAuditLogger.cs` |
| CloudLicense | `Services/Platform/CloudConfigStore.cs` + `CloudConfigParser.cs` |
| Throttle OTP | `Services/Platform/OtpRequestThrottle.cs` |
| Impresión (cadena) | `Services/Platform/CompositeReceiptPrinter.cs` |
| Layout comprobante | `Services/Platform/ReceiptTextBuilder.cs` |
| PDF 80 mm | `Services/Platform/ThermalPdfWriter.cs` |
| Fachada de dominio | `UseCases/SistecreditoService.cs` |
| DI compartido | `AppServicesRegistration.cs` |
| Arranque MAUI | `MauiProgram.cs` |

---

*Documentación profesional integral de la HU8‑973 · Equipo de Arquitectura — DOPE ·
2026-09-03 · Para excepciones al stack oficial o cambios de arquitectura,
contactar al Equipo DOPE.*
