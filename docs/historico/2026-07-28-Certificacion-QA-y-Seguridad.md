# Informe de QA y Seguridad — SistecreditoTEF.Maui

> ⚠️ **DOCUMENTO HISTÓRICO — no vigente.** Refleja el estado del módulo en su fecha de emisión;
> los identificadores y los conteos de pruebas cambiaron después. Ver [README.md](README.md) del
> histórico y la documentación vigente en [../README.md](../README.md).
> **Fecha de emisión:** 2026-07-28
> **Artefacto certificado:** `com.pos2pay` v1.0.1 (versionCode 2)
> **Alcance:** módulo de pago TEF Sistecrédito para HioPosCloud — integración por Intents con el POS
> de ICG y por HTTPS REST con la API de Credinet. Facturación y recaudo (abonos) en un único APK.

---

## Índice

1. [Veredicto](#1-veredicto)
2. [Verificaciones ejecutadas](#2-verificaciones-ejecutadas)
3. [Identidad e integridad del artefacto](#3-identidad-e-integridad-del-artefacto)
4. [Superficie de exposición](#4-superficie-de-exposición)
5. [Aislamiento de plataforma (Android)](#5-aislamiento-de-plataforma-android)
6. [Gestión de credenciales](#6-gestión-de-credenciales)
7. [Seguridad del transporte](#7-seguridad-del-transporte)
8. [Control de ambiente](#8-control-de-ambiente)
9. [Protección de datos personales](#9-protección-de-datos-personales)
10. [Cifrado en reposo](#10-cifrado-en-reposo)
11. [Integridad transaccional del dinero](#11-integridad-transaccional-del-dinero)
12. [Controles anti-abuso del OTP](#12-controles-anti-abuso-del-otp)
13. [Resiliencia y comportamiento ante fallos](#13-resiliencia-y-comportamiento-ante-fallos)
14. [Auditoría y trazabilidad](#14-auditoría-y-trazabilidad)
15. [Integridad del documento fiscal](#15-integridad-del-documento-fiscal)
16. [Cadena de suministro](#16-cadena-de-suministro)
17. [Inventario de datos sensibles](#17-inventario-de-datos-sensibles)
18. [Cobertura de pruebas de seguridad](#18-cobertura-de-pruebas-de-seguridad)
19. [Configuración por ambiente](#19-configuración-por-ambiente)
20. [Elementos que dependen de terceros](#20-elementos-que-dependen-de-terceros)
21. [Checklist de despliegue](#21-checklist-de-despliegue)

---

## 1. Veredicto

> ### ✅ TODAS LAS VERIFICACIONES PASAN

| Verificación | Resultado |
|---|---|
| Suite de pruebas automatizadas | **228 / 228 pasan**, 0 fallos, 0 omitidas |
| Resolución del grafo de inyección de dependencias | **Verificada** con contenedor real y validación estricta |
| Compilación del APK (Debug y Release) | **0 errores** |
| Análisis de dependencias vulnerables | **0 paquetes vulnerables** |
| Firma del artefacto | **Verificada sobre el binario** — esquemas v2 y v3, certificado de producción |
| Arquitecturas nativas incluidas | **arm64-v8a + x86_64** (128 librerías cada una) |
| Compatibilidad de plataforma | minSdk 24 · targetSdk 36 |
| Permisos declarados | **4** (mínimo necesario) + 1 de firma interna de AndroidX |
| Flag `debuggable` en Release | **ausente** |

El módulo queda apto para despliegue. La §20 lista los elementos cuyo cierre depende de entregables
de Sistecrédito e ICG, y la §21 el checklist operativo de instalación.

---

## 2. Verificaciones ejecutadas

Todo lo que sigue se ejecutó sobre este repositorio y sobre el APK generado. No son revisiones de
código: son comandos con su salida.

### 2.1 Suite de pruebas

```
$ dotnet test tests/SistecreditoTEF.Tests

Correctas!  -  Con error: 0, Superado: 228, Omitido: 0, Total: 228
```

La suite corre en `net10.0`, por lo que es ejecutable tanto en la máquina de desarrollo como en un
agente de CI sin emulador Android. Se recomienda configurarla como *gate* del pipeline.

### 2.2 Dependencias

```
$ dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable

El proyecto "SistecreditoTEF.Maui" especificado no tiene paquetes vulnerables
en los origenes actuales.
```

### 2.3 Firma del artefacto

```
$ apksigner verify -v --print-certs com.pos2pay-Signed.apk

Verifies
Verified using v1 scheme (JAR signing): false
Verified using v2 scheme (APK Signature Scheme v2): true
Verified using v3 scheme (APK Signature Scheme v3): true
V3.0 Signer: certificate SHA-1 digest: 7d1424275849081e1f569cdec675d527d7cccbce
```

```
$ keytool -printcert -jarfile com.pos2pay-Signed.apk

Propietario: CN=SistecreditoTEF, OU=Permoda, O=Permoda, L=Bogota, ST=Cundinamarca, C=CO
SHA1:   7D:14:24:27:58:49:08:1E:1F:56:9C:DE:C6:75:D5:27:D7:CC:CB:CE
SHA256: B7:0E:EB:EE:8D:8B:3D:47:A4:B4:EC:8E:D6:7F:5C:65:A1:24:8F:1E:44:05:65:E2:05:43:42:90:49:AF:08:43
```

### 2.4 Composición del paquete

```
$ aapt2 dump badging com.pos2pay-Signed.apk

package: name='com.pos2pay' versionCode='1' versionName='1.0.0'
minSdkVersion:'24'   targetSdkVersion:'36'
application-label:'Sistecredito'
native-code: 'arm64-v8a' 'x86_64'
```

```
$ aapt2 dump permissions com.pos2pay-Signed.apk

uses-permission: android.permission.INTERNET
uses-permission: android.permission.ACCESS_NETWORK_STATE
uses-permission: android.permission.READ_EXTERNAL_STORAGE   maxSdkVersion='32'
uses-permission: android.permission.WRITE_EXTERNAL_STORAGE  maxSdkVersion='29'
uses-permission: com.pos2pay.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION
```

---

## 3. Identidad e integridad del artefacto

### 3.1 Identificadores

| Concepto | Valor | Dónde se define |
|---|---|---|
| **Package Name** (Android) | `com.pos2pay` | `SistecreditoTEF.Maui.csproj` → `ApplicationId` |
| **APK Name** (ICG) | `permoda` | `HioposConstants.cs` → `HioposActions.ApkName` |
| Nombre visible | `Sistecredito` | `ApplicationTitle`, `android:label`, `GET_CUSTOM_PARAMS` |
| versionCode / versionName | `1` / `1.0.0` | `ApplicationVersion` / `ApplicationDisplayVersion` |

El `APK Name` está declarado en **una sola constante**, de la que se derivan las 11 acciones
(`icg.actions.electronicpayment.permoda.*`). Ningún otro archivo escribe el literal completo, lo que
elimina la posibilidad de que un cambio de nombre quede aplicado a medias.

### 3.2 Firma

- **Certificado de producción:** `CN=SistecreditoTEF, OU=Permoda, O=Permoda, L=Bogota,
  ST=Cundinamarca, C=CO`, RSA 2048, validez 10.000 días.
- **Esquemas v2 y v3.** Relevante: Android 11+ rechaza APKs firmados únicamente con el esquema v1
  (JAR) cuando el `targetSdk` es 30 o superior. El esquema v1 está ausente, lo cual es correcto
  porque el `minSdk` es 24 (Android 7.0), versión desde la que el esquema v2 está soportado.
- **Custodia:** el keystore no está versionado (`.gitignore` cubre `*.keystore`, `*.jks`,
  `signing.props`). Las contraseñas se leen de las variables de entorno `PERMODA_KEYSTORE_PASS` y
  `PERMODA_KEY_PASS`; nunca están en el repositorio.
- **El keystore es PKCS12**, formato que usa una única contraseña para el almacén y para la llave
  privada. Ambas variables de entorno deben tener el mismo valor.

### 3.3 Garantías del proceso de build

El `.csproj` incorpora dos controles que hacen imposible entregar un artefacto mal firmado sin
enterarse:

- **`ValidarFirmaDeRelease`** — un build de `Release` **falla** si no encuentra el keystore o si
  faltan las contraseñas, en lugar de degradarse silenciosamente a la llave de depuración. Existe un
  escape explícito (`-p:AllowUnsignedRelease=true`) que emite un warning visible y está pensado solo
  para pruebas locales.
- **`ForzarRefirmaEnRelease`** — el target `_Sign` de .NET Android es incremental y no declara el
  keystone ni las contraseñas como entradas, por lo que un cambio en la configuración de firma no
  invalidaría por sí solo un APK ya firmado. Este target elimina el APK firmado previo antes de que
  corra `_Sign`, garantizando que en `Release` la firma se aplique siempre.

> **Regla operativa:** el log del build no es evidencia de firma. La evidencia es el certificado del
> binario. Verificar siempre con `apksigner verify --print-certs` y comparar el SHA-1 contra el dado
> de alta en ICG (`7d142427…`).

---

## 4. Superficie de exposición

El módulo tiene exactamente **dos fronteras** y un canal lateral.

```
┌──────────────────────┐   Intents Android    ┌───────────────────────┐   HTTPS + APIM   ┌──────────────┐
│     HioPosCloud      │ ───────────────────▶ │      ESTE MÓDULO      │ ───────────────▶ │  Credinet    │
│  (POS · Android)     │  icg.actions...      │       com.pos2pay     │  Ocp-Apim-Key    │  Azure APIM  │
│                      │ ◀─────────────────── │                       │ ◀─────────────── │              │
└──────────────────────┘  setResult(...)      └───────────┬───────────┘   JSON           └──────────────┘
           ▲                                              │
           │  Broadcast de auditoría                      ▼
           └────────────────────────────────  SQLite cifrado con SQLCipher
                icg.actions.externalApi.AUDIT       (llave en Android Keystore)
```

| Frontera | Mecanismo | Quién puede invocarla | Control |
|---|---|---|---|
| **Entrada desde el POS** | Intents Android sobre `MainActivity` (`exported=true`, obligatorio para recibirlos) | Cualquier app del dispositivo puede emitir esos Intents | El módulo no expone operaciones privilegiadas: toda acción requiere que el cajero complete el flujo con la cédula del cliente y el OTP que Credinet envía al celular del titular |
| **Salida hacia Credinet** | HTTPS REST autenticado con `Ocp-Apim-Subscription-Key` | Solo el módulo | Ver §6 y §7 |
| **Canal lateral de auditoría** | `sendBroadcast` con `icg.actions.externalApi.AUDIT` | El módulo emite; HioPos consume | Requiere el `Token` que HioPos entrega en el `INITIALIZE`; sin él no se emite. Los datos personales van enmascarados (§9) |

**Nota sobre `exported=true`:** es un requisito del contrato de ICG — sin él HioPos no puede lanzar el
módulo. La mitigación no es cerrar la Activity sino que no exista ninguna acción con efecto financiero
que se pueda disparar sin la autenticación del titular: crear un crédito exige el OTP, y registrar un
abono exige seleccionar un crédito activo del cliente previamente validado contra Credinet.

### 4.1 Acciones aceptadas

| Acción | Efecto | Requiere autenticación del titular |
|---|---|---|
| `INITIALIZE` | Persiste el token de sesión y la configuración de CloudLicense | — |
| `FINALIZE` | Limpia el token de sesión | — |
| `GET_VERSION`, `GET_BEHAVIOR`, `GET_CUSTOM_PARAMS`, `GET_PRINT_INFO` | Solo lectura, devuelven metadatos | — |
| `SHOW_SETUP_SCREEN` | Sin efecto (responde OK) | — |
| **`TRANSACTION`** | Inicia el flujo de crédito o de abono | **Sí** (OTP para crédito) |
| `READ_CARD`, `CHARGE_CARD`, `GET_CARD_DATA` | **No soportadas** → responden `Canceled` | — |

Las capacidades se declaran en `GET_BEHAVIOR` con **17 flags**, de los cuales solo tres están en
`true`: `SupportsCredit`, `HasCustomParams` y `CanAudit`. Todo lo demás en `false`: sin anulaciones,
sin cierre de lote, sin débito, sin lectura de tarjeta. La cantidad de flags está fijada por un test,
de modo que agregar o quitar una capacidad no puede pasar inadvertido.

---

## 5. Aislamiento de plataforma (Android)

| Control | Estado | Detalle |
|---|---|---|
| **Permisos** | 4, mínimo necesario | `INTERNET` y `ACCESS_NETWORK_STATE` para hablar con Credinet. Los dos de almacenamiento están acotados con `maxSdkVersion` (32 y 29) y son residuales: el documento de venta se recibe *inline* en el extra `DocumentData`, no por ruta de fichero |
| **Sin permisos peligrosos** | ✅ | No se solicita cámara, contactos, ubicación, SMS, teléfono ni almacenamiento en Android 13+ |
| **`allowBackup`** | `false` | Impide extraer los datos de la app con `adb backup` |
| **`debuggable`** | ausente en Release | Verificado con `aapt2 dump badging` |
| **`FileProvider`** | `exported=false`, `grantUriPermissions=true` | Los comprobantes se comparten con permiso puntual por URI, sin exponer el directorio |
| **Rutas de fichero compartidas** | solo `cache-path` y `files-path` | Ambas dentro del sandbox privado de la app |
| **`<queries>`** | 2 entradas | Visibilidad de paquetes acotada a lo que se usa (servicio de impresora y apps que reciben PDF), conforme al modelo de Android 11+ |
| **`launchMode`** | `singleTask` | Evita instancias duplicadas de la Activity cuando el POS relanza el módulo |
| **Permiso interno de AndroidX** | `com.pos2pay.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` | Generado automáticamente por AndroidX, de nivel *signature*: solo apps firmadas con el mismo certificado podrían usarlo |

### 5.1 Convivencia de los dos modos en un solo APK

El APK atiende dos escenarios y el estado nunca se mezcla:

| | Facturación (desde HioPos) | Recaudo standalone (ícono del launcher) |
|---|---|---|
| Disparador | Intent `TRANSACTION` | Intent `MAIN`/`LAUNCHER` |
| Quién imprime el comprobante | HioPos, con el XML que se le devuelve | La app, por `IReceiptPrinter` |
| Cierre | `setResult` + `finish` | `FinishAffinity` |
| Configuración de Credinet | La misma, persistida on-device | La misma |

Con `launchMode=singleTask` ambos escenarios comparten la Activity, por lo que existe una **bandera
de liveness explícita** (`HioposTransactionActive`) que marca una factura de HioPos como viva
únicamente entre el arranque de la `TRANSACTION` y la devolución del resultado. Mientras está activa,
un Intent del launcher se descarta para no pisar la venta que el POS está esperando; y al cambiar de
modo el estado se limpia. La bandera es explícita en lugar de inferirse de datos residuales, de modo
que un flujo abandonado no bloquea el acceso a los abonos.

La configuración de CloudLicense **no se borra en el `FINALIZE`**. Es deliberado: el recaudo
standalone no recibe `INITIALIZE`, así que borrarla dejaría los abonos sin credencial al cerrar
HioPos.

---

## 6. Gestión de credenciales

### 6.1 Credencial de Credinet (`Ocp-Apim-Subscription-Key`)

| Aspecto | Implementación |
|---|---|
| **Origen en producción** | La provisiona ICG en CloudLicense y llega en el extra `Parameters` del `INITIALIZE`. **No está dentro del APK** |
| **Almacenamiento** | `SecureStorage` de MAUI, respaldado por **Android Keystore**. En claro no queda en disco |
| **Disponibilidad** | Se cachea en memoria al recibirla, y se persiste en segundo plano. La primera petición HTTP ocurre mucho después (cuando el cajero ingresa la cédula), así que la caché siempre está lista |
| **Inyección en la petición** | `AuthInterceptor` la agrega por request. Lee la configuración **vigente** mediante un accesor, no una instancia capturada al arrancar, de modo que una credencial provisionada después del arranque se aplica sin reiniciar el proceso |
| **En logs** | **Nunca.** El header se redacta como `***REDACTED***` (§9) |
| **Rotación** | Sin recompilar: ICG actualiza el parámetro y el módulo lo recarga en el siguiente `INITIALIZE` |

La clave de **sandbox** publicada en el manual de Credinet sí está en el código como constante, junto
con el marcador `__SANDBOX__` de `appsettings.json`. Es pública por definición y solo aplica al
ambiente de pruebas; el control de §8 impide que un POS de producción la use.

### 6.2 Token de sesión de HioPos

Se recibe en el `INITIALIZE`, se guarda en `Preferences` (almacenamiento privado de la app) y se
limpia en el `FINALIZE`. Es un identificador de sesión del POS, no una credencial de acceso a
Credinet. Sin él no se emiten broadcasts de auditoría.

### 6.3 Llave de cifrado de la base local

Ver §10.

---

## 7. Seguridad del transporte

| Control | Estado |
|---|---|
| **HTTPS obligatorio** | La configuración se valida y una `BaseUrl` que no empiece por `https://` se reporta como inválida (§8) |
| **Certificate pinning por SPKI** | **Implementado y probado.** Se activa en cuanto haya pines configurados |
| **Compresión** | GZip/Deflate, con `Accept-Encoding` explícito |
| **Timeout global** | 30 s configurable (`Credinet:TimeoutSeconds` o CloudLicense), techo de toda la operación incluidos reintentos |

### 7.1 Diseño del pinning

Se pinea el **SubjectPublicKeyInfo** (SHA-256, base64), no el certificado completo: la llave pública
sobrevive a la rotación del certificado, lo que importa porque `api.credinet.co` está detrás de CDN.
Se acepta pinear el certificado hoja o cualquier elemento de la cadena, lo que permite pinear una CA
intermedia y sobrevivir a rotaciones del hoja.

Propiedades verificadas por pruebas:

- Una cadena TLS **inválida se rechaza siempre**, incluso si el pin coincide. El pinning endurece la
  validación estándar, no la sustituye.
- Con un pin que no corresponde se **rechaza** la conexión — es el caso que el control bloquea: un
  intermediario con certificado técnicamente válido (por ejemplo un proxy de inspección corporativo
  cuyo CA esté instalado en el POS) pero con otra llave pública.
- Se aceptan **múltiples pines**, que es la práctica correcta para rotar sin dejar terminales afuera.
- Con la lista vacía se aplica **validación TLS estándar**, comportamiento seguro por defecto que no
  interrumpe la operación.

**Estado de configuración:** la lista de pines está vacía. El mecanismo está listo y admite carga por
CloudLicense (`CERTIFICATE_PINS`, separados por coma o punto y coma), así que se activa sin
recompilar en cuanto Sistecrédito entregue los valores (§20).

### 7.2 Política de reintentos

Conservadora por diseño, para no duplicar efectos financieros:

| Regla | Detalle |
|---|---|
| Solo fallos transitorios de infraestructura | `HttpRequestException`, `5xx`, `408`. Los errores de negocio llegan como HTTP 200 con `errorCode`, por lo que **no** se reintentan |
| Solo métodos seguros | Únicamente `GET`. **Todo `POST` queda excluido** (`create` y `payCredit` podrían duplicar crédito o cobro) |
| Exclusión explícita | `getCreditToken` no se reintenta aunque sea `GET`: dispararía un OTP adicional |
| Volumen | 2 reintentos, backoff 250 ms y 500 ms |

---

## 8. Control de ambiente

Este control impide el escenario más costoso de una integración de pagos: una terminal de producción
operando contra el ambiente de pruebas, con el cajero convencido de que las operaciones son reales.

### 8.1 Precedencia de configuración

```
1. CloudLicense (ICG, llega en el INITIALIZE)   ← gana
2. appsettings.json (embebido en el APK)
3. User Secrets (solo compilaciones DEBUG)
4. Valores por defecto en código
```

Los parámetros que acepta de CloudLicense son: `API_BASE_URL`, `SUBSCRIPTION_KEY`, `STORE_ID`,
`STORE_NAME`, `ENVIRONMENT`, `OTP_DESTINATION`, `CERTIFICATE_PINS`, `FREQUENCY`, `SOURCE`,
`AUTH_METHOD`, `TIMEOUT_SECONDS`, `OTP_MAX_RESENDS` y las políticas de OTP. La precedencia está
cubierta por pruebas, nivel por nivel.

### 8.2 Parseo robusto de los parámetros

El XML de `Parameters` se interpreta de forma **agnóstica al namespace** (se compara el `LocalName`
del elemento) y tolerante a la capitalización del atributo `Key`. Cubierto por pruebas con XML sin
namespace, con namespace por defecto y con prefijo de namespace.

Si el extra `Parameters` llega pero no se reconoce ningún parámetro, la condición se registra como
**error** y se emite un evento de auditoría `CONFIG_ERROR` hacia el POS. No se continúa en silencio.

### 8.3 Validación de coherencia

`ApiConfig.Validate()` verifica, y `MainActivity` **rechaza la transacción** con un mensaje al cajero
si algo no cuadra:

| Regla | Se rechaza cuando |
|---|---|
| Credencial presente | Falta `SUBSCRIPTION_KEY` |
| URL presente y cifrada | Falta `API_BASE_URL` o no usa HTTPS |
| Producción con credencial real | `ENVIRONMENT=production` y se está usando la clave pública de sandbox |
| Producción contra endpoint de producción | `ENVIRONMENT=production` y la URL apunta a `/pos/` (sandbox) en lugar de `/posprod/` |
| Producción con tienda identificada | `ENVIRONMENT=production` sin `STORE_ID` |

El rechazo se devuelve al POS como `TransactionResult=Failed` con el título *"Configuración
inválida"* y un mensaje que le indica al cajero avisar a sistemas. La validación estricta aplica solo
a producción: el ambiente de pruebas sigue operando con la clave pública de sandbox.

---

## 9. Protección de datos personales

Marco aplicable: **Ley 1581 de 2012** (habeas data, Colombia). Los datos personales que el módulo
maneja son la cédula, el nombre, el celular y el historial de crédito del cliente.

### 9.1 Enmascarado centralizado

Todo el enmascarado pasa por una única utilidad (`PiiMask`), de modo que no depende de que cada punto
de log se acuerde de aplicarlo:

| Dato | Tratamiento | Ejemplo |
|---|---|---|
| Cédula | Últimos 4 dígitos | `1026260942` → `******0942` |
| Documentos de ≤ 4 caracteres | Enmascarado completo | `1234` → `****` |
| Nombre | Iniciales | `Juan Perez` → `J. P.` |
| Cédula en URL | Enmascarada en la query | `idDocument=******0942` |

### 9.2 Registro de peticiones HTTP

Verificado por pruebas que el registro **nunca** contiene:

- la `Ocp-Apim-Subscription-Key` (se sustituye por `***REDACTED***`);
- el header `Authorization`;
- la cédula, ni en la URL, ni en el cuerpo de la petición, ni en el cuerpo de la **respuesta**;
- el **OTP** (`token` se sustituye por `***REDACTED***`) — con el OTP y la cédula se podría crear un
  crédito, así que no queda registrado en ningún lado.

Lo que sí se conserva es lo necesario para diagnosticar: método, endpoint, código de estado,
duración y los headers no sensibles.

El registro en memoria está **apagado por defecto en Release**; solo se enciende en compilaciones
DEBUG. Está cubierto por una prueba condicionada a la configuración.

### 9.3 Trazas de diagnóstico

Las trazas de diagnóstico del flujo de captura de cédula están compiladas bajo `#if DEBUG`, por lo
que **no existen en el APK de producción**. Esto es relevante porque en terminales POS con `adb`
habilitado para soporte, cualquiera con acceso USB puede leer `logcat`.

### 9.4 Comprobante impreso

El comprobante de abono lleva la **cédula enmascarada**: el papel puede quedar en el mostrador y el
cliente ya conoce su documento. El voucher de crédito sí lleva la cédula completa, porque es el
documento con el que el titular firma su obligación.

### 9.5 Retención

Los datos locales se purgan automáticamente a los **180 días** (idempotencia y auditoría), y los
comprobantes en caché a las **24 horas**. La purga corre en segundo plano al recibir el `INITIALIZE`,
sin demorar el arranque del POS.

---

## 10. Cifrado en reposo

| Aspecto | Implementación |
|---|---|
| **Motor** | SQLite cifrado con **SQLCipher** (`SQLitePCLRaw.bundle_e_sqlcipher`) |
| **Llave** | 32 bytes de `RandomNumberGenerator`, generados en el primer arranque |
| **Custodia de la llave** | `SecureStorage`, respaldado por **Android Keystore**. Nunca en el código ni en disco en claro |
| **Qué se cifra** | Idempotencia de créditos y abonos (cédulas, `creditId`, montos) y la traza de auditoría |
| **Ubicación** | Directorio privado de la app, fuera del alcance de otras aplicaciones |
| **Legado** | Cualquier base en claro de versiones anteriores se elimina al inicializar |

### 10.1 Manejo de fallos del Keystore

El acceso a `SecureStorage` puede demorar en terminales sin bloqueo de pantalla o recién arrancadas.
El módulo distingue explícitamente **"no había llave"** de **"no pude leer la llave"**:

- La lectura se reintenta con backoff antes de rendirse.
- Si tras los reintentos no se obtuvo respuesta, se opera con una llave provisional marcada como **no
  autoritativa** y **la base no se elimina**. Se registra como error.
- La base solo se recrea cuando la llave es autoritativa **y** el error es compatible con "archivo no
  descifrable" (`SQLITE_NOTADB`, *file is encrypted*, *malformed*). Un disco lleno o una base
  bloqueada no disparan borrado.
- Toda recreación queda registrada como error, no como detalle.

Esto garantiza que un problema transitorio del Keystore no destruya la barrera de idempotencia ni la
traza financiera local.

---

## 11. Integridad transaccional del dinero

### 11.1 Aritmética

Toda conversión entre pesos y centavos pasa por una **única** implementación:

- La aritmética se hace en **`decimal`**, no en `double`. El binario flotante no representa
  exactamente los decimales de dinero, y truncar en esa representación pierde centavos.
- El redondeo es **`MidpointRounding.AwayFromZero`** (redondeo comercial colombiano), no el `ToEven`
  por defecto de .NET.
- El formato hacia el POS y la DIAN usa **cultura invariante**: el locale del terminal no altera el
  valor transmitido.

Cubierto por pruebas exhaustivas, incluidas las 20.001 conversiones de ida y vuelta entre 0 y 200
pesos y la equivalencia entre todos los caminos de conversión del código.

### 11.2 Idempotencia de créditos

**Doble barrera:**

| Barrera | Mecanismo |
|---|---|
| Local | Registro en la base cifrada indexado por el `SaleId` del documento de venta |
| Remota | Se envía `invoice=SaleId` a Credinet, que deduplica del lado servidor (evita el error 252 `DuplicatedCredit`) |

Si HioPos reintenta la misma venta, el módulo devuelve el crédito ya creado **con todos sus datos
financieros** —tasa efectiva anual, cuota inicial, cuota mensual, plazo— para que el comprobante
reimpreso sea idéntico al original. El crédito completo se persiste serializado; nunca se
reconstruyen importes.

### 11.3 Idempotencia de abonos

El intento de abono se persiste en la base cifrada **antes** de llamar a Credinet, con lo que
sobrevive a un reinicio del proceso, que es el escenario habitual en un POS (presión de memoria,
reinicio del terminal, cierre de HioPos).

Estados y su semántica:

| Estado | Significado | Comportamiento ante un nuevo intento |
|---|---|---|
| `Pending` | Se envió y no se conoce el resultado | **Se bloquea** el reintento. Se le indica al cajero verificar el saldo antes de volver a cobrar |
| `Completed` | Credinet confirmó | Se **reimprime** el comprobante existente; no se cobra de nuevo |
| `Failed` | Credinet rechazó por regla de negocio (no cobró) | Se **permite** reintentar |

La distinción es la clave del control: un error de **red** deja el intento en `Pending` (pudo haberse
aplicado), mientras que un rechazo de **negocio** lo cierra como `Failed` (no se aplicó). La ventana
de idempotencia es de 30 minutos y discrimina por crédito **y** monto, de modo que un abono legítimo
del mismo monto días después se procesa con normalidad.

Verificado por pruebas: el reintento no vuelve a cobrar, la barrera sobrevive al reinicio del
proceso, un error de red bloquea el siguiente intento, los rechazos de negocio permiten reintentar,
los abonos de otro monto o a otro crédito sí se cobran, y un fallo de la base local **no bloquea el
recaudo** (queda registrado que esa operación corrió sin barrera local).

### 11.4 Coherencia del monto reportado al POS

El módulo reporta al POS el valor **realmente financiado o pagado**, coherente con lo que ve la DIAN,
y **compara** ese valor contra el que HioPos solicitó. Una diferencia superior a un peso se registra
en el log y en la auditoría como descuadre, con el identificador de la venta, para que sea rastreable
y conciliable. No se cancela la operación: el crédito ya existe en Credinet y cancelar dejaría al
cliente con una obligación sin factura.

### 11.5 Transacciones en duda

Cuando el resultado de una operación con efecto financiero no se puede determinar (típicamente un
timeout después de que el servidor ya procesó), el módulo:

- **no** reporta el fallo como "código inválido" ni consume un intento de OTP;
- deja el intento registrado como pendiente;
- le indica al cajero que **verifique el saldo antes de reintentar**;
- ofrece una vía explícita para cerrar el pendiente una vez verificado.

---

## 12. Controles anti-abuso del OTP

El OTP es la autenticación del titular del crédito. Los contadores viven en un **servicio singleton**,
no en la pantalla, de modo que navegar hacia atrás y volver a entrar no los reinicia.

| Control | Valor por defecto | Configurable |
|---|---|---|
| Cooldown entre solicitudes de código | 60 s | `OtpResendCooldownSeconds` / CloudLicense |
| Máximo de reenvíos por transacción | 3 | `OtpMaxResends` / CloudLicense |
| Máximo de intentos de verificación | 3 | `OtpMaxVerifyAttempts` |
| Cooldown entre intentos de verificación | 2 s | `OtpVerifyCooldownSeconds` |

Reglas verificadas por pruebas:

- El tope de reenvíos se informa **de inmediato** al alcanzarse, sin obligar a esperar el cooldown.
- Un código nuevo reinicia los intentos de verificación (son intentos *contra ese código*) pero **no**
  el contador de reenvíos.
- Los intentos **no bajan de cero** y el tope sobrevive a la recreación de la pantalla.
- Un fallo de **infraestructura no consume un intento**: tres cortes de red no dejan al cajero sin
  intentos con un código válido.
- Los contadores son **thread-safe** (verificado con 8 hilos concurrentes y 1.600 operaciones, sin
  incrementos perdidos).

El throttle se reinicia al comenzar cada transacción nueva, de modo que una factura no hereda los
límites consumidos por la anterior.

**Canal de entrega:** WhatsApp a la línea del cliente (`OtpDestination=1`), confirmado por
Sistecrédito para pruebas y producción. El texto del voucher y la pantalla de OTP derivan el canal de
la configuración, por lo que siempre coinciden con el canal real.

---

## 13. Resiliencia y comportamiento ante fallos

### 13.1 Nunca dejar al POS esperando

HioPos espera un `setResult` para cerrar la venta. Si no lo recibe, hace timeout e interpreta que el
módulo no respondió. El módulo garantiza una respuesta en todos los caminos:

- Cada handler de Intent está envuelto en `try/catch`; una excepción devuelve `Canceled` al POS, con
  un segundo `try/catch` para el propio finish.
- Las dos pantallas terminales (confirmación de crédito y comprobante de abono) **siempre** responden:
  si no hay operación que reportar o si armar la respuesta falla, devuelven `Failed` explícito con un
  mensaje que le dice al cajero qué verificar.
- Un documento de venta con XML malformado **no cancela la venta**: el documento solo aporta
  conveniencia (autocompletar la cédula, obtener el `SaleId`), así que el flujo continúa sin él.

### 13.2 Degradación segura

| Componente | Si falla |
|---|---|
| Base local de idempotencia | La operación continúa; queda registrado que corrió sin barrera local. La barrera remota (`invoice`) sigue vigente para créditos |
| Persistencia de auditoría | Best-effort; no interrumpe la operación. La fuente de verdad es Credinet y el broadcast al POS |
| `SecureStorage` | Ver §10.1: no se destruyen datos |
| Impresión local del abono | El cajero recibe el aviso y puede reintentar o continuar; la decisión queda auditada. El abono ya está registrado |
| Un fallo de inicialización de la base | Se reintenta en la siguiente operación; no queda inutilizada para el resto de la sesión |

### 13.3 Mensajes al usuario

Los errores se traducen a mensajes accionables para el cajero mediante una única utilidad. **No se
exponen mensajes de excepción crudos** en pantalla, que podrían revelar hosts, rutas o detalles de
red frente al cliente.

---

## 14. Auditoría y trazabilidad

**Doble destino:**

1. **Broadcast al POS** (`icg.actions.externalApi.AUDIT`), con los límites del contrato de ICG
   respetados: 50 caracteres para la acción, 1.050 para el comentario. Requiere el token de sesión.
2. **SQLite cifrado con SQLCipher** on-device, con retención de 180 días.

Eventos auditados: validación de cliente (OK y fallo), simulación, solicitud de OTP con su canal,
creación de crédito, fallo de creación, abono, fallo de abono, abono en duda y su resolución manual,
descuadres de monto y errores de configuración.

Todos los datos personales van **enmascarados** en el comentario de auditoría — relevante porque el
broadcast sale del sandbox de la app hacia el POS.

---

## 15. Integridad del documento fiscal

El módulo enriquece el medio de pago de la factura del POS para el módulo fiscal de ICG y, a través
de él, para la DIAN. Dos restricciones se respetan estrictamente:

| Restricción | Implementación |
|---|---|
| **El `ModifyDocumentResult` solo toca `PaymentMeans`** | No se agregan líneas de producto, datos de empresa ni totales. Si el resultado pareciera un documento nuevo, HioPos lo reenviaría a la DIAN, generando un doble envío |
| **El comprobante es un voucher, no una factura** | Sin encabezado de empresa, sin NIT, sin líneas de producto, sin totales, y un único corte de papel al final. Por el mismo motivo |

Ambas reglas están cubiertas por pruebas, incluida la verificación de que el voucher lleva un solo
`CUT_PAPER` y de que toda línea de texto va acompañada del bloque `<Formats>` que exige el parser de
HioPos.

Los identificadores que viajan al módulo fiscal se normalizan en un único punto: se eliminan guiones
y espacios, se truncan a los 40 caracteres que define el manual de HioPos, y si vienen vacíos se usa
el número consecutivo con relleno a 6 dígitos (la API SIAT/DIAN rechaza valores vacíos). El
`TransactionData` se mantiene por debajo del límite interno de 200 caracteres del módulo fiscal.

Todo campo numérico fiscal se emite sin separadores de miles y con cultura invariante.

---

## 16. Cadena de suministro

| Control | Estado |
|---|---|
| Paquetes con vulnerabilidades conocidas | **0** (verificado, §2.2) |
| Versiones fijadas explícitamente | Las del runtime nativo de SQLite/SQLCipher, para que ganen sobre las transitivas |
| Secretos en el repositorio | Ninguno. `.gitignore` cubre `*.keystore`, `*.jks`, `signing.props`, `appsettings.Production.json`, `appsettings.*.json` |
| Credenciales de producción en el APK | Ninguna. Se provisionan por CloudLicense |

**Recomendación para el pipeline:** incorporar `dotnet list package --vulnerable
--include-transitive` como gate, junto con `dotnet test`. Ambos comandos son rápidos y detectan
regresiones de seguridad antes de generar el artefacto.

---

## 17. Inventario de datos sensibles

| Dato | En tránsito | En reposo | En logs | Retención |
|---|---|---|---|---|
| Cédula del cliente | HTTPS (query y cuerpo JSON) | Cifrada con SQLCipher | Enmascarada (`******0942`) | 180 días |
| Nombre completo | HTTPS | Cifrada | Enmascarado o ausente | 180 días |
| Celular | HTTPS | No se persiste | No se registra | — |
| Cupo y saldo de crédito | HTTPS | Cifrados | Solo montos agregados en auditoría | 180 días |
| `creditId` / `paymentId` | HTTPS + extras del Intent | Cifrados | Sí (no son datos personales) | 180 días |
| **OTP** | HTTPS (cuerpo del POST) | **No se persiste** | **Redactado** | — |
| **Credencial de APIM** | Header HTTP | `SecureStorage` (Android Keystore) | **Redactada** | Hasta el próximo `INITIALIZE` |
| Token de sesión de HioPos | Extra del Intent | `Preferences` (privado) | No se registra | Hasta el `FINALIZE` |
| Llave de cifrado de la base | — | `SecureStorage` (Android Keystore) | Nunca | Permanente |
| Comprobante en PDF | — | Caché privada de la app | — | 24 horas |

---

## 18. Cobertura de pruebas de seguridad

**228 pruebas, todas pasan.** Las orientadas a seguridad e integridad:

| Área | Qué se verifica |
|---|---|
| **Grafo de inyección de dependencias** | El contenedor se construye con validación estricta (`ValidateOnBuild`, `ValidateScopes`) y cada servicio se resuelve; ningún servicio tiene constructores de la misma aridad que el contenedor no pueda desempatar |
| **Certificate pinning** | Cadena inválida se rechaza aun con pin correcto; pin incorrecto se rechaza; lista vacía aplica TLS estándar; múltiples pines; pin en la cadena y no solo en el hoja; estabilidad y formato del pin |
| **Redacción en logs** | La credencial de APIM nunca queda registrada; `Authorization` redactado; cédula enmascarada en URL, cuerpo de petición y cuerpo de respuesta; OTP redactado; el gate de registro funciona; headers no sensibles se conservan |
| **Enmascarado de PII** | Cédulas de cualquier longitud, incluidas las de ≤ 4 caracteres; URLs; idempotencia del enmascarado |
| **Control de ambiente** | Precedencia CloudLicense → appsettings; producción con clave de sandbox se rechaza; producción contra URL de sandbox se rechaza; producción sin tienda se rechaza; HTTPS obligatorio; sandbox sigue operando |
| **Parseo de configuración** | XML sin namespace, con namespace por defecto y con prefijo; capitalización del atributo; entrada inválida no lanza |
| **Idempotencia de abonos** | Reintento no vuelve a cobrar; barrera sobrevive al reinicio; error de red bloquea; rechazo de negocio permite reintentar; cobros legítimos pasan; fallo de la base no bloquea el recaudo |
| **Idempotencia de créditos** | El replay devuelve los datos financieros completos; un registro incompleto falla en lugar de emitir un comprobante con importes en cero |
| **Anti-abuso del OTP** | Cooldown, topes, no consumir intentos por fallos de red, persistencia del tope, concurrencia |
| **Integridad del dinero** | Redondeo comercial, equivalencia entre todos los caminos de conversión, ida y vuelta, formato fiscal sin separadores |
| **Contrato con el POS** | Cantidad exacta de flags de capacidad; el voucher no parece factura; un solo corte de papel; `<Formats>` en toda línea de texto |
| **Identificadores fiscales** | Normalización, truncado a 40, relleno del consecutivo |
| **Impresión del comprobante** | 19 pruebas: layout, ancho de columna, PDF estructuralmente válido, offsets correctos con caracteres acentuados, escapado de caracteres especiales, cadena de impresión con fallback, sin duplicación, sin falsos positivos |

---

## 19. Configuración por ambiente

| Clave | Pruebas | Producción |
|---|---|---|
| `Environment` | `sandbox` | `production` (por CloudLicense) |
| `SubscriptionKey` | `__SANDBOX__` (clave pública del manual) | Provisionada por ICG en CloudLicense |
| `BaseUrl` | `https://api.credinet.co/pos/` | `https://api.credinet.co/posprod/` |
| `StoreId` | vacío | Obligatorio (se valida) |
| `OtpDestination` | `1` (WhatsApp) | `1` (WhatsApp) |
| `CertificatePins` | vacío (TLS estándar) | Pines de Sistecrédito (§20) |
| `TimeoutSeconds` | `30` | `30` |
| `Printing:EnableSunmiNative` | `false` | `false` (§20) |

El APK embebido trae la configuración de **pruebas**. La de producción llega por CloudLicense, lo
que significa que **la credencial de producción no está dentro del artefacto distribuido** y que el
control de §8 impide operar con una combinación incoherente.

---

## 20. Elementos que dependen de terceros

Ninguno de estos impide el despliegue; son entregables externos que permiten elevar aún más la
postura de seguridad o completar funcionalidad opcional.

| # | Elemento | De quién depende | Estado del módulo |
|---|---|---|---|
| 1 | **Pines SPKI de `api.credinet.co`** | Sistecrédito | Mecanismo implementado y probado. Se activa cargando `CERTIFICATE_PINS` en CloudLicense, sin recompilar. Mientras la lista esté vacía, opera con validación TLS estándar |
| 2 | **Campo de idempotencia en `payCredit`** | Sistecrédito | La barrera local cubre el reinicio del terminal. Un campo equivalente a `invoice` permitiría además deduplicar del lado servidor, cubriendo el caso de dos terminales cobrando el mismo crédito simultáneamente |
| 3 | **AIDL oficial de impresión Sunmi** | Sunmi / Permoda | El comprobante de abono se imprime por el Android Print Framework, camino estándar que funciona con impresora Bluetooth, USB, red o print service. La vía nativa Sunmi es una optimización opcional; se habilita con `Printing:EnableSunmiNative=true` tras validar en hardware |
| 4 | **Alta en CloudLicense por terminal** | ICG | Requiere el package `com.pos2pay`, el APK Name `permoda` y el SHA-1 `7d142427…` |
| 5 | **Alineación de páginas de 16 KB** | Mantenedores de SQLitePCLRaw | Android 16 lo exigirá para librerías nativas. Sin efecto en las versiones de Android actualmente en operación |

---

## 21. Checklist de despliegue

### 21.1 Generar el artefacto

```powershell
# Variables de entorno (PKCS12: el MISMO valor en ambas)
setx PERMODA_KEYSTORE_PASS "<clave>"
setx PERMODA_KEY_PASS      "<clave>"
# setx no afecta la terminal actual: abrir una nueva.
# En un pipeline: usar variables secretas del agente.

dotnet test tests/SistecreditoTEF.Tests
dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable
dotnet publish src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj -c Release -f net10.0-android
```

### 21.2 Verificar el artefacto (obligatorio)

```powershell
apksigner verify --print-certs <apk>
```

El SHA-1 debe ser **`7d1424275849081e1f569cdec675d527d7cccbce`**. Si aparece `CN=Android Debug`, **no
distribuir**.

### 21.3 Instalar

| Situación del terminal | Procedimiento |
|---|---|
| **Sin el módulo instalado** | `adb install com.pos2pay-Signed.apk` |
| **Con una versión firmada con otro certificado** | `adb uninstall com.pos2pay` y luego instalar. Android no permite cambiar la firma de un paquete existente (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`) |

> **Importante:** desinstalar elimina los datos locales de la app — la base de idempotencia y la
> auditoría cifrada. No hacerlo con una venta o un abono en curso.

### 21.4 Configurar en CloudLicense

Provisionar `API_BASE_URL` (`/posprod/`), `SUBSCRIPTION_KEY`, `STORE_ID`, `STORE_NAME`,
`ENVIRONMENT=production` y, cuando estén disponibles, `CERTIFICATE_PINS`. Si algo falta o es
incoherente, el módulo rechaza la transacción con un mensaje explícito al cajero en lugar de operar
contra el ambiente equivocado.

### 21.5 Confirmar en el terminal

1. Que HioPos lance el módulo (valida package + APK Name + SHA-1 en ICG).
2. Un ciclo de facturación completo, verificando que la factura cuadre.
3. Un abono desde el ícono del launcher, verificando que el comprobante se imprima.
4. Que el ícono de abonos siga accesible con una venta de HioPos cerrada, y bloqueado con una abierta.

---

## 22. Higiene recomendada

| Acción | Motivo |
|---|---|
| Rotar las contraseñas del keystore | Buena práctica periódica; guardarlas en un gestor o como variables secretas del pipeline |
| Mover el keystore obsoleto fuera del directorio de build | Conserva la trazabilidad histórica sin riesgo de confundirlo con el vigente |
| Incorporar `dotnet test` y el análisis de vulnerabilidades como gates de CI | Detecta regresiones antes de generar el artefacto |
| Subir `versionCode` en cada release | Algunos MDM tratan el mismo `versionCode` como "ya instalado" y no aplican el reemplazo |
| Extraer un proyecto `Core` (`net10.0`) con la capa pura | Mejora estructural: hoy la suite compila la capa pura mediante globs, lo que funciona pero acopla el proyecto de pruebas a la estructura de carpetas |

---

*Informe de QA y Seguridad. El detalle de la evolución del código y las decisiones de
implementación está en [`2026-07-26-Remediacion.md`](2026-07-26-Remediacion.md). La arquitectura vigente del módulo está en
[`../02-Arquitectura.md`](../02-Arquitectura.md). Para excepciones al stack oficial o cambios de arquitectura,
contactar al Equipo de Arquitectura — DOPE.*
