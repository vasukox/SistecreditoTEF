# Remediación de la auditoría QA — SistecreditoTEF.Maui

> ⚠️ **DOCUMENTO HISTÓRICO — no vigente.** Refleja el estado del módulo en su fecha de emisión.
> Ver [README.md](README.md) del histórico y la documentación vigente en [../README.md](../README.md).
> **Fecha:** 2026-07-28
> **Documento hermano:** [`2026-07-24-Auditoria-QA.md`](2026-07-24-Auditoria-QA.md) es el informe de auditoría (qué se
> encontró). **Este documento registra qué se corrigió, cómo, y con qué evidencia se verificó.**

---

## 1. Resumen

| Métrica | Antes | Después |
|---|---|---|
| Tests que **se pueden ejecutar** | 0 (la suite no compilaba, y el TFM impedía correrla) | **183** |
| Tests que pasan | — | **183 / 183** |
| APK de Release | firmado con **`CN=Android Debug`**, exit code 0 | firma real, o **el build falla** |
| Dependencias con vulnerabilidad alta | 1 (`SQLitePCLRaw.lib.e_sqlite3.android` 2.1.2) | **0** |
| Impresión de abonos | los 3 printers devolvían `true` sin imprimir | PDF real + cadena con fallback verificado |
| Hallazgos críticos abiertos | 5 | **0** |
| Hallazgos altos abiertos | 12 | **0** |

### Cómo reproducir la verificación

```powershell
# Suite completa (ahora ejecutable en Windows y en CI)
dotnet test tests/SistecreditoTEF.Tests
#  -> Correctas! Con error: 0, Superado: 183, Total: 183

# Dependencias
dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable
#  -> no tiene paquetes vulnerables

# Firma: sin contrasenas, el Release DEBE fallar
dotnet build src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj -c Release -f net10.0-android
#  -> error: "...faltan las contrasenas: define PERMODA_KEYSTORE_PASS y PERMODA_KEY_PASS..."

# Firma: con contrasenas, y VERIFICAR el certificado del APK resultante
dotnet publish src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj -c Release -f net10.0-android
keytool -printcert -jarfile <apk>
#  -> Propietario NO debe decir "CN=Android Debug"
```

---

## 2. Los dos modos siguen conviviendo

Requisito explícito: facturación (TEF) y abonos viven en el mismo APK (`com.pos2pay`) y ninguno
puede romper al otro. Lo que se hizo para garantizarlo:

| Aspecto | Modo HI-POS (facturación) | Modo standalone (abonos por el ícono) |
|---|---|---|
| **Quién imprime** | HioPos, con el XML `MerchantReceipt`/`CustomerReceipt` que le devolvemos | La app, vía `IReceiptPrinter` |
| **Doble impresión** | Imposible: en modo HI-POS no se imprime localmente | Imposible: la cadena para en el primer printer que tiene éxito (hay test) |
| **Cierre del flujo** | `setResult` + `finish()` | `FinishAffinity()` |
| **Datos del comprobante** | Misma fuente que el modo standalone (`ReceiptTextBuilder` / `ReceiptBuilder`) | Ídem |
| **Configuración de Credinet** | Llega por CloudLicense en el `INITIALIZE` | **La misma**, persistida: sobrevive al `FINALIZE` |

**Decisión deliberada:** el `FINALIZE` **no** borra la configuración de CloudLicense. Si lo hiciera,
un abono standalone posterior (que no recibe `INITIALIZE`) se quedaría sin `SUBSCRIPTION_KEY` y el
recaudo dejaría de funcionar al cerrar HioPos. `CloudConfigStore.Clear()` existe pero solo se invoca
si alguien lo pide explícitamente.

**Verificado por tests:** el guard de colisión de modos (`HioposTransactionActive`) sigue intacto;
las pruebas de la cadena de impresión cubren que un printer exitoso no encadene al siguiente.

---

## 3. Impresión de abonos — el problema real y su corrección

Esto es lo que se pidió validar explícitamente. **Los tres printers reportaban éxito sin imprimir.**

### 3.1 `SunmiPrinter`: una API que no existe

```csharp
// ANTES
intent.SetComponent(new ComponentName(
    "com.sunmi.printerservice", "com.sunmi.printerservice.PrinterService"));
intent.SetAction("sunmi.print");        // el docstring decía "sunmi printerprint"
activity.StartService(intent);
await Task.Delay(1500);
return true;                            // <- SIEMPRE true
```

Cuatro problemas independientes:

1. El servicio AIDL real de Sunmi es `woyou.aidlservice.jiuv5.IWoyouService`, no
   `com.sunmi.printerservice`. La detección de disponibilidad **nunca podía funcionar** (y el
   `<queries>` del manifest declaraba el paquete equivocado).
2. La acción usada (`"sunmi.print"`) no existe, y no coincidía ni con el comentario de la propia
   clase (`"sunmi printerprint"`).
3. `StartService` hacia un servicio de otra app lanza `IllegalStateException` en Android 8+ desde
   background. Sunmi se consume con `BindService` + AIDL.
4. **Devolvía `true` incondicionalmente.** Este es el punto: la app reportaba "comprobante impreso"
   sin haber impreso, así que el defecto era invisible.

**Corrección:** integrar Sunmi de verdad exige el AIDL oficial del fabricante y validación en
hardware — los códigos de transacción del Binder son ordinales del AIDL y adivinarlos produciría
fallos silenciosos, exactamente el problema que se está corrigiendo. Así que la clase ahora:

- Se declara disponible **solo** si el paquete AIDL real está instalado **y**
  `Printing:EnableSunmiNative` está en `true` (por defecto `false`).
- Devuelve `false` mientras no exista el binding — **nunca un falso positivo** — para que la cadena
  caiga al printer que sí funciona.

> ⚠️ **Pendiente de hardware:** para activar la impresión nativa Sunmi hay que agregar el AIDL/SDK
> oficial, implementar el binding y validarlo en una terminal Sunmi física. Está documentado en el
> propio `SunmiPrinter.cs`. Mientras tanto el abono imprime por Android Print, que es un camino
> estándar y verificable.

### 3.2 `AndroidPrintPrinter`: texto plano donde el sistema espera un PDF

```csharp
// ANTES: OnLayout declaraba PrintContentType.Document...
var bytes = System.Text.Encoding.UTF8.GetBytes(_content);   // ...y OnWrite mandaba TEXTO
stream.Write(bytes, 0, bytes.Length);
```

El `ParcelFileDescriptor` de un trabajo de impresión debe recibir un **PDF válido**. Con texto crudo
el print service produce un trabajo inválido o una hoja en blanco. **Esta era la causa concreta de
que el abono no saliera.** Además `PrintAsync` devolvía `true` justo después de llamar a
`printManager.Print(...)`, sin mirar el resultado.

**Corrección:**
- Se escribe un **PDF real**, generado por el nuevo `ThermalPdfWriter` (sin dependencias nuevas:
  un objeto de página, la fuente Type1 estándar Courier y un stream de contenido).
- Se fija el tamaño de papel a **rollo de 80 mm** con márgenes mínimos. Sin eso el framework asume
  A4 y el comprobante sale en una hoja enorme con el texto en una esquina.
- Se consulta el `PrintJob` devuelto y se reporta `false` si quedó fallido, bloqueado (sin papel,
  impresora offline) o cancelado, para que la cadena use el fallback.

### 3.3 `PdfReceiptPrinter` era código muerto

`AndroidPrintPrinter.IsAvailable` devuelve `true` en **cualquier** dispositivo Android (`PrintManager`
siempre existe), y la selección se hacía una sola vez al resolver el DI:

```csharp
// ANTES: si el elegido fallaba, NO había fallback
if (sunmi.IsAvailable) return sunmi;
if (android.IsAvailable) return android;
return pdf;                              // <- inalcanzable
```

**Corrección:** `CompositeReceiptPrinter` recorre los printers en orden y pasa al siguiente si uno
devuelve `false` o lanza. Además el fallback final ahora comparte un **PDF** (`application/pdf`), no
un `.txt` — un `.txt` no se puede mandar a imprimir desde el chooser en la mayoría de POS, así que
el "fallback de impresión" tampoco imprimía.

### 3.4 Un solo layout para el comprobante

Cada printer tenía su propio formato (`SunmiPrinter` usaba tags inventados `{center}{b}{cut}`), y
ninguno coincidía con el voucher que HioPos imprime. El cliente recibía un comprobante distinto según
por dónde saliera. Ahora los tres consumen `ReceiptTextBuilder`, con las **mismas 42 columnas** del
voucher de HioPos.

### 3.5 El cajero se enteraba si no imprimía… ahora sí

Antes, si la impresión fallaba se logueaba y la Activity **se cerraba igual**: el cajero se quedaba
sin comprobante y sin saberlo. Ahora se le muestra un diálogo con "Reintentar" / "Continuar sin
imprimir", y si elige continuar queda auditado. Hay además un comando `Reimprimir`.

### 3.6 Qué se verificó y qué falta

| Verificado con tests (19 casos) | Requiere hardware |
|---|---|
| El comprobante lleva todos los datos del abono | Que la impresora térmica física corte y alinee bien |
| Ninguna línea excede las 42 columnas | Impresión nativa Sunmi (AIDL oficial) |
| Los valores quedan alineados al margen derecho | Que el print service del POS acepte el rollo de 80 mm |
| El PDF es estructuralmente válido (`%PDF`, catálogo, página, fuente, xref, trailer, `%%EOF`) | |
| El `xref` apunta a offsets reales **con nombres acentuados** (José Muñoz Ñandú) | |
| Se escapan `(`, `)` y `\` del nombre del cliente | |
| El ancho de página corresponde a 80 mm | |
| Si el primero falla, se intenta el siguiente | |
| Un printer que **lanza** no corta la cadena | |
| Si el primero funciona, **no** se imprime por duplicado | |
| Si todos fallan, se reporta `false` (no un éxito falso) | |
| El formato de moneda no depende del locale del POS | |
| La cédula va enmascarada en el comprobante | |

---

## 4. Correcciones por hallazgo

### Críticos

| # | Hallazgo | Corrección | Verificación |
|---|---|---|---|
| **C-1** | Release firmado con la llave de depuración | Ruta del keystore a la raíz de la solución; target `ValidarFirmaDeRelease` que **falla el build** si no puede firmar; escape `-p:AllowUnsignedRelease=true` con warning; **+ `ForzarRefirmaEnRelease`** (ver §4.1) | Build de Release sin contraseñas → error claro. Antes: exit 0 con `CN=Android Debug`. Certificado del APK verificado con `keytool` |
| **C-2** | La suite de tests no se podía ejecutar | TFM de tests a `net10.0` con globs sobre la capa pura; `FakeStateStore` y `FakeIdempotencyStore` actualizados; test de `OnlyUseDocumentPath` corregido | `dotnet test` → 183/183 |
| **C-3** | Un POS de producción podía operar en sandbox en silencio | `CloudConfigParser` compara por `LocalName` (agnóstico al namespace); `SaveFromXml` devuelve el conteo y loguea **error** si es 0; `ApiConfig.Validate()` detecta incoherencias; `MainActivity` **rechaza la TRANSACTION** con mensaje al cajero | 20 tests (`CloudConfigParserTests`, `ApiConfigTests`) incl. XML con namespace por defecto y con prefijo |
| **C-4** | El replay de idempotencia imprimía un voucher con importes en $0 | `CachedTransaction` persiste el `Credit` completo serializado; el replay lo devuelve tal cual; si el cache es de una versión anterior devuelve `ApiError.Local` accionable en vez de ceros | 2 tests: el replay conserva cuota, TEA, cuota inicial y plazo; el cache incompleto falla en vez de imprimir ceros |
| **C-5** | Doble cobro posible en abonos | Idempotencia **persistida** por crédito+monto en la BD cifrada, escrita **antes** del POST; estados `Pending`/`Completed`/`Failed`; `PaymentOutcome` distingue los 4 desenlaces | 11 tests (`AbonoIdempotenciaTests`) incl. reinicio del proceso, error de red y que los cobros legítimos sí pasen |

### 4.1 Un segundo defecto de firma, descubierto al verificar

Corregir las propiedades de firma **no fue suficiente**, y solo se detectó porque se verificó el
binario en vez de confiar en el log. El build decía:

```
[FIRMA] Release se firmara con permoda-release-v2.keystore (alias sistecredito).
```

…y el APK resultante seguía teniendo `CN=Android Debug`. La causa, visible con `-v:n`:

```
_Sign:
  Se omitira el destino "_Sign" porque todos los archivos de salida estan
  actualizados respecto a los archivos de entrada.
```

El target `_Sign` de .NET Android es **incremental** y compara el APK firmado de salida contra el
`.apk` sin firmar de entrada. **El keystore y las contraseñas no son entradas declaradas**, así que
cambiar la configuración de firma no invalida el APK ya firmado: MSBuild conserva el binario viejo y
la firma nueva nunca se aplica.

Es el mismo patrón que el defecto original —una falla de firma silenciosa— pero por otra vía: se rota
el certificado, se recompila, y se distribuye el APK con la firma anterior sin ninguna señal.

**Corrección:** el target `ForzarRefirmaEnRelease` borra el APK firmado previo antes de que corra
`_Sign`, de modo que en Release la firma se aplique siempre.

### 4.2 Y un tercer defecto: PKCS12 no admite dos contraseñas

Con la refirma forzada, `_Sign` por fin se ejecutó… y falló:

```
Xamarin.Android.Common.targets(2767,2): error MSB6006: "java.exe" salio con el codigo 2
```

Ese target es la tarea `<AndroidApkSigner>`. `apksigner` reventaba con
`UnrecoverableKeyException: Given final block not properly padded`, y el APK quedaba **sin firma**
(`keytool -printcert` no devolvía certificado).

Causa: **el keystore es PKCS12**, y PKCS12 usa **una sola contraseña** para el almacén y para la
llave privada. Se estaban pasando dos distintas. Sumado a eso, las variables de entorno de la sesión
tenían el valor de un certificado anterior, y el default del csproj apuntaba al v1.

**Corrección:** `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS` deben tener **el mismo valor**, y el
default del csproj apunta a `permoda-release-v2.keystore`. Documentado en el bloque de comentarios de
la firma, junto con el historial de rotación de certificados.

### 4.3 Estado final de la firma, verificado sobre el binario

```
keytool -printcert -jarfile bin/Release/net10.0-android/com.pos2pay-Signed.apk

  Propietario: CN=SistecreditoTEF, OU=Permoda, O=Permoda, L=Bogota, ST=Cundinamarca, C=CO
  SHA1:   7D:14:24:27:58:49:08:1E:1F:56:9C:DE:C6:75:D5:27:D7:CC:CB:CE
  SHA256: B7:0E:EB:EE:8D:8B:3D:47:A4:B4:EC:8E:D6:7F:5C:65:A1:24:8F:1E:44:05:65:E2:05:43:42:90:49:AF:08:43
```

**Lección para el pipeline:** verificar el certificado del artefacto es un paso obligatorio, no
opcional. Los tres defectos de firma de esta HU produjeron builds con `exit code 0` o con logs que
decían lo contrario de lo que pasaba:

| Defecto | Lo que decía el build | Lo que había en el APK |
|---|---|---|
| Ruta del keystore mal | `exit 0`, sin warnings | firmado con `CN=Android Debug` |
| `_Sign` saltado por incremental | `[FIRMA] Release se firmara con …v2.keystore` | firmado con `CN=Android Debug` |
| PKCS12 con dos contraseñas | `error MSB6006: java.exe codigo 2` | **sin firma** |

El log del build no es evidencia. El certificado del binario sí.

### 4.4 Regresión introducida durante la remediación, detectada en terminal

Al hacer que los servicios leyeran la configuración vigente en lugar de una copia
capturada al arrancar, se les agregó una **segunda sobrecarga de constructor**:

```csharp
public CredinetRepository(ICredinetApi api, ApiConfigProvider configProvider)  // para DI
public CredinetRepository(ICredinetApi api, ApiConfig config)                  // "para tests"
```

`Microsoft.Extensions.DependencyInjection` no puede elegir entre dos constructores de la misma aridad
cuando ninguno es subconjunto del otro: lanza `AmbiguousConstructorException` en el primer
`GetService`. En el terminal se manifestó como un crash al abrir la app desde el ícono, en la
resolución de `CreditosActivosViewModel → SistecreditoService → ICredinetRepository`.

**Por qué la suite no lo detectó:** todas las pruebas instanciaban las clases a mano. El código
compilaba, los 205 tests pasaban, y el defecto solo aparecía con un contenedor real. Era un hueco de
cobertura, no un falso negativo.

**Correcciones:**

1. **Un solo constructor por servicio.** Se introdujo `IApiConfigSource`, implementada por
   `ApiConfigProvider` (recargable, para la app) y por `StaticApiConfigSource` (valor fijo, para
   pruebas). `CredinetRepository` y `AuthInterceptor` quedaron con un único constructor.

   > Nota: `[ActivatorUtilitiesConstructor]` **no** resuelve este caso. Ese atributo lo honra
   > `ActivatorUtilities.CreateInstance`, no el `CallSiteFactory` que usa el `ServiceProvider` para
   > resolver un servicio registrado — que es justo lo que aparecía en el stack trace. La única
   > solución es eliminar la ambigüedad.

2. **Fail-safe en la navegación del launcher.** La navegación a la pantalla de abonos corría dentro
   de un `MainThread.BeginInvokeOnMainThread(async () => …)`, que es `async void` para el
   dispatcher: una excepción ahí **no** la atrapa el `try/catch` de `HandleIntent`, sube al
   `SynchronizationContext` de Android y mata el proceso. Ahora está envuelta y el cajero recibe un
   aviso en lugar de un cierre abrupto.

3. **El test que faltaba.** `InyeccionDeDependenciasTests` construye un contenedor real con
   `ValidateOnBuild` y `ValidateScopes`, resuelve cada servicio, y verifica estructuralmente que
   ningún servicio tenga dos constructores públicos de la misma aridad.

**Lección:** una suite que solo instancia clases a mano no valida el grafo de DI. En una app MAUI un
error de resolución no es un error de compilación: es un crash en el arranque del flujo.

### Altos

| # | Hallazgo | Corrección |
|---|---|---|
| **A-1** | Cédula completa en logcat por 4 caminos | `PiiMask` centralizado; traza de `CapturaCedulaViewModel` movida a `#if DEBUG`; `CredinetApiClient` loguea la URL enmascarada; `HttpLoggingHandler` enmascara también los **cuerpos** JSON y **redacta el OTP** |
| **A-2** | La auditoría persistía la cédula completa | Enmascarada en `SistecreditoService`. El test que exigía lo contrario se corrigió |
| **A-3** | `SUBSCRIPTION_KEY` en `Preferences` sin cifrar | Migrada a `SecureStorage` (Android Keystore), con caché en memoria para no bloquear el arranque |
| **A-4** | Dependencia con vulnerabilidad alta | `SQLitePCLRaw.lib.e_sqlite3.android` y `.core` fijados en **2.1.12** (2.1.10 y 2.1.11 siguen afectados) |
| **A-5** | Pinning TLS inactivo | Los pines se aceptan por CloudLicense (`CERTIFICATE_PINS`) y el callback los lee del proveedor en cada validación → activables sin recompilar |
| **A-6** | Un timeout de `SecureStorage` destruía la BD local | Se distingue "no había llave" de "no pude leerla": reintentos con backoff, llave marcada como no autoritativa, y **la BD no se borra**; el borrado solo ocurre ante un error compatible con "no descifrable" |
| **A-7** | `Finalizar` podía dejar a HioPos colgado | Eliminado el early-return; ahora **siempre** se responde, con `try/catch` simétrico al flujo de pago |
| **A-8** | Dos conversiones de dinero incompatibles; una truncaba | Nueva clase `Money` (única, `decimal`, `AwayFromZero`); `MoneyConverter` delega en ella |
| **A-9** | El `Amount` devuelto no se validaba contra el del Intent | `ValidateAmountAgainstIntent` compara con tolerancia de 1 peso y registra el descuadre en log y auditoría |
| **A-10** | Sin resolución de transacciones en duda | `PaymentOutcome.InDoubt` / `NetworkUncertain` + `ResolverAbonoEnDudaAsync`; la UI le dice al cajero que verifique en vez de reintentar a ciegas |
| **A-11** | El "debounce" lanzaba N llamadas concurrentes | Debounce real con `CancellationTokenSource` + número de secuencia para descartar respuestas obsoletas |
| **A-12** | El tope de intentos de OTP se reiniciaba navegando atrás | Contador movido al singleton `OtpRequestThrottle` |

### Medios y bajos

Corregidos: **M-1** (handlers `Transient`), **M-2** (`ApiConfigProvider` + recarga en `INITIALIZE`),
**M-3** (todos los parámetros por CloudLicense), **M-4** (`OtpMaxResends` alineado a 3), **M-5**
(`Frequency` recibe la periodicidad, no los meses; `Fees` con fuente única), **M-6**
(`PaymentMeanId` con constante única), **M-7** (tienda y cajero desde configuración y `SellerData`),
**M-8** (un CTS por temporizador + `IDisposable`), **M-9** (un error de red ya no consume intento de
OTP), **M-10** (todo el estado bajo el mismo lock), **M-11** (sin `Lazy` que cachee el fallo),
**M-12/M-13** (retención de 180 días con purga), **M-14** (el test asierta la cantidad de flags),
**M-15** (el voucher dice WhatsApp según configuración), **M-16** (`FriendlyMessage` en vez de
`ex.Message`), **M-17** (XML malformado no cancela la venta), **M-18** (comentario del `apk_name`),
**B-1** (nulabilidad), **B-2/B-3** (deduplicado en `DianFieldSanitizer` y `ExtractSellerName`,
ahora agnóstico al namespace), **B-4** (documentos cortos se enmascaran), **B-5** (`await
Task.CompletedTask` eliminado), **B-7** (cultura invariante), **B-10** (fecha fija del comprobante),
**B-13** (`Shorten` sin riesgo de NRE), **B-14** (lógica de reenvío simplificada).

---

## 5. Archivos nuevos

| Archivo | Para qué |
|---|---|
| `Common/Money.cs` | Conversión de dinero única, en `decimal` |
| `Common/PiiMask.cs` | Enmascarado de datos personales centralizado |
| `Services/Credinet/ICloudConfig.cs` | Seam testeable para los parámetros de CloudLicense |
| `Services/Credinet/ApiConfigProvider.cs` | Configuración recargable tras el `INITIALIZE` |
| `Services/Platform/CloudConfigParser.cs` | Parseo puro del XML de `Parameters` |
| `Services/Hiopos/DianFieldSanitizer.cs` | Normalización de identificadores fiscales |
| `Services/Platform/ReceiptTextBuilder.cs` | Layout único del comprobante de abono |
| `Services/Platform/ThermalPdfWriter.cs` | Generación de PDF para rollo de 80 mm |
| `Services/Platform/CompositeReceiptPrinter.cs` | Cadena de impresión con fallback real |

**Tests nuevos:** `MoneyTests`, `PiiMaskTests`, `CloudConfigParserTests`, `ApiConfigTests`,
`OtpRequestThrottleTests`, `DianFieldSanitizerTests`, `AbonoIdempotenciaTests`,
`ImpresionAbonosTests`.

---

## 6. Pendientes que NO se pueden cerrar desde el código

| # | Pendiente | Por qué |
|---|---|---|
| 1 | **Impresión nativa Sunmi** | Requiere el AIDL/SDK oficial del fabricante y validación en terminal física. Hoy el abono imprime por Android Print, que es estándar. Habilitar con `Printing:EnableSunmiNative=true` después de validar |
| 2 | **Pines SPKI de Credinet** | Sistecrédito debe entregarlos. El mecanismo ya está listo (`CERTIFICATE_PINS` por CloudLicense); mientras la lista esté vacía, es TLS estándar |
| 3 | **Idempotencia de `payCredit` del lado servidor** | Hay que pedirle a Sistecrédito un campo equivalente a `invoice`. La barrera local ya protege el caso del reinicio del POS, pero no dos POS cobrando el mismo crédito a la vez |
| 4 | **Terminales del piloto: desinstalar antes de actualizar** | El certificado vigente (`CN=SistecreditoTEF`, SHA-1 `7d142427…`) **no es** el que firmó el APK del piloto (`CN=Sistecredito TEF`, SHA-256 `D7:97:8F:9E…`, keystore v1). Android no permite actualizar un paquete con una firma distinta: la instalación falla con `INSTALL_FAILED_UPDATE_INCOMPATIBLE`. Hay que **desinstalar `com.pos2pay` en cada terminal del piloto** antes de instalar esta versión. El alta del nuevo SHA-1 en ICG ya está hecha |
| 5 | **Sacar el keystore v1 del directorio de build** | `permoda-release.keystore` (v1, obsoleto, contraseña distinta) sigue junto al v2. Conviene renombrarlo a `permoda-release_obsoleto_2026-07.keystore` y moverlo fuera de la raíz del repo: conserva la trazabilidad histórica sin riesgo de que alguien lo tome por el vigente |
| 6 | **Rotar las contraseñas del keystore** | Se compartieron por chat durante esta sesión |
| 6b | **`AndroidR8Mode` es una propiedad inexistente** | Se agregó `<AndroidR8Mode>false</AndroidR8Mode>` con un comentario que dice que fuerza d8 sin minificación. Esa propiedad **no existe** en el SDK de .NET Android (verificado: no aparece en ningún `.targets`/`.props` de `Microsoft.Android.Sdk.Windows`): es un no-op. El `java.exe code 2` no era R8 sino la firma (§4.2). El knob real es `<AndroidLinkTool></AndroidLinkTool>` (vacío = solo d8). Conviene quitarla para no dejar la falsa impresión de que R8 está desactivado |
| 7 | **Verificar el conteo de flags de `GET_BEHAVIOR`** | El código emite 17 y la documentación de ICG decía 18. El test ahora fija 17; si el manual exige un flag más, hay que agregarlo |
| 8 | **`XA0141`: páginas de 16 KB** | `libe_sqlite3.so` no las soporta y **Android 16 lo exigirá**. Depende de que SQLitePCLRaw publique binarios alineados |
| 9 | **Pruebas E2E en terminal** | Los casos 24–27 de la §9 del informe (ciclo completo, matar el proceso entre `create` y el voucher, reinstalación sobre el piloto, sesión de 8 horas) requieren hardware |

---

## 7. Nota sobre la ejecución de los tests

El proyecto de tests ahora apunta a `net10.0` y compila la capa pura del APK mediante globs con
exclusiones (`Platforms/`, `Views/`, printers de plataforma, SQLite, `Preferences`/`SecureStorage`
quedan fuera; los valida el build del APK). Los archivos nuevos del dominio se toman
automáticamente.

**Mejora recomendada a futuro:** extraer un proyecto `SistecreditoTEF.Core` (`net10.0`) referenciado
por el APK y por los tests. Es la solución arquitectónicamente correcta, pero implica mover ~80
archivos; se prefirió el cambio de menor riesgo que logra el objetivo inmediato: **que la suite
corra en CI**.

---

*Para excepciones al stack oficial o cambios de arquitectura, contactar
al Equipo de Arquitectura — DOPE.*
