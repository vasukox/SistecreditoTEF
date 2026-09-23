# Informe de QA — SistecreditoTEF.Maui

> ⚠️ **DOCUMENTO HISTÓRICO — no vigente.** Refleja el estado del módulo en su fecha de emisión;
> varios hallazgos ya fueron corregidos. Ver [README.md](README.md) del histórico y la
> documentación vigente en [../README.md](../README.md).
> **Fecha de auditoría:** 2026-07-28
> **Rama / commit base:** `main` @ `51c539a` (+ working tree con cambios sin commitear)
> **Alcance:** módulo TEF Sistecrédito para HioPosCloud — 126 archivos `.cs`, 13 archivos de test,
> integración HioPos (Intents) + Credinet (HTTPS REST).
> **Tipo de auditoría:** funcional, seguridad, consistencia/concurrencia, robustez bajo estrés,
> calidad de código, dependencias y cadena de build/firma.

---

## Índice

1. [Resumen ejecutivo](#1-resumen-ejecutivo)
2. [Qué se ejecutó realmente (evidencia)](#2-qué-se-ejecutó-realmente-evidencia)
3. [Hallazgos críticos / bloqueantes](#3-hallazgos-críticos--bloqueantes)
4. [Hallazgos altos](#4-hallazgos-altos)
5. [Hallazgos medios](#5-hallazgos-medios)
6. [Hallazgos bajos y deuda técnica](#6-hallazgos-bajos-y-deuda-técnica)
7. [Lo que está bien hecho](#7-lo-que-está-bien-hecho)
8. [Cobertura de pruebas: análisis de brechas](#8-cobertura-de-pruebas-análisis-de-brechas)
9. [Casos de prueba recomendados (batería propuesta)](#9-casos-de-prueba-recomendados-batería-propuesta)
10. [Plan de remediación priorizado](#10-plan-de-remediación-priorizado)
11. [Veredicto de release](#11-veredicto-de-release)

---

## 1. Resumen ejecutivo

El módulo está **bien diseñado en su arquitectura** (capas limpias, `ApiResult<T>` en vez de
excepciones, anti-corruption layers, fail-safe en `MainActivity`, Polly conservador) y los
comentarios del código demuestran conocimiento profundo del contrato de ICG y de las trampas del
módulo fiscal DIAN. Ese trabajo es sólido.

El problema no es el diseño: es que **varios controles documentados no están realmente activos**, y
que **la red de seguridad automatizada está caída**, así que las regresiones no se detectan.

| Severidad | Cantidad | Bloquea el go-live |
|---|---|---|
| 🔴 **Crítico** | 5 | Sí |
| 🟠 **Alto** | 12 | Sí (la mayoría) |
| 🟡 **Medio** | 19 | No, pero antes del rollout masivo |
| ⚪ **Bajo** | 14 | No |

### Los cinco titulares

1. **El APK de Release se firma con la llave de DEBUG.** Verificado con `keytool` sobre el binario
   real: `CN=Android Debug, O=Android, C=US`. La firma de producción solo existe porque alguien la
   aplica a mano fuera de MSBuild (`*.firmado.apk`).
2. **La suite de tests no compilaba** (error `CS0535`), así que `dotnet test` fallaba antes de
   ejecutar un solo test. Además el proyecto de tests apunta a `net10.0-android`, por lo que **no
   puede ejecutarse en Windows ni en CI** ni siquiera compilando. Al repararlo: **72 pasan, 1 falla**.
3. **Un POS en producción puede quedar silenciosamente en SANDBOX.** `CloudConfigStore` lee el XML de
   CloudLicense de forma sensible al namespace y no valida que los parámetros obligatorios llegaran.
   Si no llegan, `appsettings.json` hace fallback a la key pública de pruebas y a la URL `/pos/`.
4. **El camino de idempotencia imprime un voucher legalmente incorrecto:** al reintentar una venta,
   el crédito se reconstruye con todos los campos financieros en cero → el cliente recibe un
   comprobante con cuota mensual $0 y TEA 0%.
5. **Los abonos no tienen ninguna barrera de idempotencia** (ni local persistente ni remota), solo un
   guard en memoria de 60s. Un reinicio del POS en el momento equivocado permite un **doble cobro**.

---

## 2. Qué se ejecutó realmente (evidencia)

Este informe no es solo lectura de código. Esto es lo que se ejecutó:

### 2.1 Suite de tests

```
# Estado encontrado
$ dotnet test tests/SistecreditoTEF.Tests
error CS0535: 'SistecreditoServiceTests.FakeStateStore' no implementa el miembro
de interfaz 'ITransactionStateStore.HioposTransactionActive'
→ 0 tests ejecutados
```

Tras agregar la propiedad faltante al fake (única modificación aplicada al repo en esta auditoría,
en `tests/.../SistecreditoServiceTests.cs:186`), el proyecto compila pero **el host de test no
arranca**, porque el TFM es Android:

```
El proceso del host de prueba ... finalizó con el siguiente error:
You must install or update .NET to run this application.
App: ...\bin\Debug\net10.0-android\testhost.exe
```

Para obtener resultados reales se recompiló el código puro + los tests existentes contra `net10.0`
en un runner temporal fuera del repo. Resultado:

```
Con error!  -  Con error: 1, Superado: 72, Omitido: 0, Total: 73, Duración: 826 ms
```

**Test que falla:** `HioposResultBuilderTests.BuildBehavior_retorna_los_18_flags_en_extras`
([HioposResultBuilderTests.cs:36](../tests/SistecreditoTEF.Tests/Services/Hiopos/HioposResultBuilderTests.cs#L36))

```
Assert.Equal() Failure: Strings differ
Expected: "true"      ← el test espera OnlyUseDocumentPath = true
Actual:   "false"     ← producción lo cambió a false (scoped storage Android 13+)
```

El cambio de producción es **correcto** (está justificado en
[HioposConstants.cs:165-170](../src/SistecreditoTEF.Maui/Services/Hiopos/HioposConstants.cs#L165-L170));
lo que quedó desactualizado es el test.

### 2.2 Verificación de firma del APK

```
$ keytool -printcert -jarfile bin/Release/net10.0-android/com.pos2pay-Signed.apk
Propietario: CN=Android Debug, O=Android, C=US          ← ⚠️ LLAVE DE DEPURACIÓN
SHA256: C1:90:09:8F:33:9E:CF:12:B6:33:C7:5E:...

$ keytool -printcert -jarfile bin/Release/net10.0-android/com.pos2pay.apk.firmado.apk
Propietario: CN=Sistecredito TEF, OU=Permoda, O=Permoda, L=Bogota, ST=Cundinamarca, C=CO
SHA256: D7:97:8F:9E:67:E2:28:CB:FF:6F:88:23:BE:CA:47:C1:E4:0D:8F:8C:18:F1:C0:2A:CE:36:1C:CB:E9:EF:86:7E
```

### 2.3 Build en Release

`0 Errores`, pero **63 warnings**, incluyendo una vulnerabilidad de dependencias:

```
warning NU1903: El paquete "SQLitePCLRaw.lib.e_sqlite3.android" 2.1.2 tiene una
vulnerabilidad de gravedad ALTA conocida
https://github.com/advisories/GHSA-2m69-gcr7-jv3q
```

Confirmado con `dotnet list package --vulnerable --include-transitive`.

### 2.4 Prueba aritmética de dinero

Ejecutada sobre el runtime .NET real, comparando las dos conversiones pesos→centavos que coexisten
en el código:

| Pesos | `MoneyConverter.FromPesosToCents` (trunca) | `ToCents` de los ViewModels (redondea) | |
|---|---|---|---|
| 19999,99 | 1999999 | 1999999 | ok |
| 1234,56 | 123456 | 123456 | ok |
| **8,29** | **828** | **829** | ⚠️ divergen |
| **0,29** | **28** | **29** | ⚠️ divergen |
| **70,07** | **7006** | **7007** | ⚠️ divergen |

---

## 3. Hallazgos críticos / bloqueantes

### 🔴 C-1 · El APK de Release se firma con la llave de depuración

**Archivo:** [SistecreditoTEF.Maui.csproj:107-118](../src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj#L107-L118)

La firma está condicionada a `Exists('$(PermodaKeystore)')`, con default
`$(MSBuildProjectDirectory)\permoda-release.keystore`, es decir
`src\SistecreditoTEF.Maui\permoda-release.keystore`. Los keystores reales están en la **raíz del
repositorio**:

```
C:\Proyectos\SistecreditoTEF.Maui\permoda-release.keystore        (10/07/2026)
C:\Proyectos\SistecreditoTEF.Maui\permoda-release-v2.keystore     (14/07/2026)
C:\Proyectos\SistecreditoTEF.Maui\src\SistecreditoTEF.Maui\...    ← vacío (verificado)
```

`Exists()` da `false` → `AndroidKeyStore` nunca se activa → .NET Android cae al
`~\.android\debug.keystore`. **El diseño "seguro por defecto" convierte un error de configuración en
un APK debug-firmado sin un solo warning.**

**Impacto:**
- Un APK debug-firmado es rechazable por políticas MDM y por ICG, y su llave es pública/compartida:
  cualquiera puede firmar un APK con el mismo package `com.pos2pay` y suplantar el módulo TEF.
- `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS` **no están definidas** en la máquina (verificado), así
  que ni arreglando la ruta firmaría hoy.
- Existen **dos keystores** (`v1` y `v2`) y ninguno está referenciado por el `-v2`. Como el piloto ya
  corrió en terminales reales, **cuál de los dos firmó lo instalado determina con cuál se puede
  actualizar**; con el otro, las actualizaciones fallan con `INSTALL_FAILED_UPDATE_INCOMPATIBLE`.

**Remediación:**
1. Mover el default a la raíz de la solución o, mejor, usar un `signing.props` en la raíz (ya está
   contemplado en `.gitignore`) que ambos `.csproj` importen.
2. **Hacer que la firma falle el build, no que se degrade en silencio.** Si `Configuration=Release`
   y no hay keystore o faltan las passwords → `<Error>` de MSBuild.
3. Comparar los SHA-256 de los dos keystores y documentar el canónico.
4. Agregar un paso de verificación post-build (`apksigner verify --print-certs`) en el pipeline.

---

### 🔴 C-2 · La red de seguridad automatizada está caída

**Tres defectos acumulados que se refuerzan:**

| # | Defecto | Archivo |
|---|---|---|
| a | `FakeStateStore` no implementa `HioposTransactionActive` → **no compila** | [SistecreditoServiceTests.cs:178](../tests/SistecreditoTEF.Tests/UseCases/SistecreditoServiceTests.cs#L178) |
| b | TFM `net10.0-android` → `testhost.exe` de Android, **no ejecutable en Windows/CI** | [SistecreditoTEF.Tests.csproj:4](../tests/SistecreditoTEF.Tests/SistecreditoTEF.Tests.csproj#L4) |
| c | 1 test desactualizado falla (`OnlyUseDocumentPath`) | [HioposResultBuilderTests.cs:36](../tests/SistecreditoTEF.Tests/Services/Hiopos/HioposResultBuilderTests.cs#L36) |

`HioposTransactionActive` se agregó junto al guard de colisión de modos. **Nadie corrió
los tests desde entonces** — y no podían correrlos aunque quisieran, por (b).

**Impacto:** todos los hallazgos de este informe que un test habría detectado (C-4, A-8, A-11, M-5,
M-14) llegaron a producción precisamente porque no hay ejecución automatizada.

**Remediación:** cambiar el TFM de tests a `net10.0` (los tests son de lógica pura, no necesitan
Android — se validó recompilándolos así), reparar el fake y el test desactualizado, y **meter
`dotnet test` en el pipeline como gate**.

---

### 🔴 C-3 · Un POS en producción puede quedar silenciosamente en SANDBOX

**Archivos:** [CloudConfigStore.cs:44](../src/SistecreditoTEF.Maui/Services/Platform/CloudConfigStore.cs#L44),
[ApiConfig.cs:102-120](../src/SistecreditoTEF.Maui/Services/Credinet/ApiConfig.cs#L102-L120),
[appsettings.json](../src/SistecreditoTEF.Maui/appsettings.json)

Tres problemas encadenados:

**1. El parseo es sensible al namespace XML.**

```csharp
foreach (var p in doc.Descendants("Param"))   // ← requiere que NO haya namespace
```

`XName` implícito = namespace vacío. Si el XML de ICG viene con un namespace por defecto
(`<Configuration xmlns="...">`), `Descendants("Param")` devuelve **cero elementos**. Nótese que
`XmlDocumentReader` sí hace lo correcto (compara por `LocalName`) — la lección ya está aprendida en
el repo, pero no se aplicó aquí.

**2. El fallo es silencioso y se loguea como éxito.**

```csharp
AppLogger.I("CloudConfigStore", $"Parámetros Cloud guardados: {count}");  // count = 0 → nivel Info
```

**3. El fallback lleva a sandbox.** `appsettings.json` embebido trae
`SubscriptionKey: "__SANDBOX__"`, `BaseUrl: ".../pos/"` (sandbox) y `Environment: "sandbox"`.

**Impacto:** una terminal de producción arranca, no recibe los parámetros, no avisa, y opera contra
el ambiente sandbox con la key pública del manual. Créditos que el cajero cree reales no existen en
producción. Es el peor modo de fallo posible en este sistema: **incorrecto y silencioso**.

**Remediación:**
- Comparar por `LocalName` (igual que `XmlDocumentReader`).
- Validar los parámetros obligatorios y **fallar fuerte**: si `Environment=production` y falta
  `SUBSCRIPTION_KEY` o `API_BASE_URL`, responder `TransactionResult=Failed` con mensaje claro al
  cajero, no operar degradado.
- Loguear `count == 0` como `Warn`/`Error`, no `Info`.
- Mostrar el ambiente activo en la UI (badge "SANDBOX") para que sea imposible confundirlo.

---

### 🔴 C-4 · El replay de idempotencia genera un comprobante financieramente falso

**Archivo:** [SistecreditoService.cs:147-169](../src/SistecreditoTEF.Maui/UseCases/SistecreditoService.cs#L147-L169)

En un *hit* de idempotencia se reconstruye el `Credit` así:

```csharp
return new ApiResult<Credit>.Ok<Credit>(new Credit(
    CreditId:            cached.CreditId,
    CreditNumber:        cached.CreditNumber,
    EffectiveAnnualRate: 0.0,      // ← TEA
    DownPayment:         0.0,
    TotalFeeValue:       0.0,      // ← cuota mensual
    TotalDownPayment:    0.0,      // ← cuota inicial
    ... todos los demás en 0.0
```

Esos campos alimentan directamente el voucher impreso
([ConfirmacionViewModel.cs:115-139](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L115-L139)):
`cuotaMensual: Credito.TotalFeeValue`, `tasaEfectivaAnual: Credito.EffectiveAnnualRate`,
`cuotaInicial: Credito.TotalDownPayment`.

**Resultado:** el cliente firma y se lleva un comprobante que dice **cuota mensual $0, cuota inicial
$0, Tasa E.A. 0,00%** para un crédito real y vigente. En Colombia eso es un documento de crédito con
información sustancialmente falsa entregado al consumidor.

**Agravante:** el esquema `CachedTransaction` **tiene** los campos `MerchantReceiptXml` y
`CustomerReceiptXml` justamente para poder reimprimir el voucher original… y se guardan vacíos:

```csharp
await _idempotency.SaveAsync(new CachedTransaction(
    ...
    CardHolder:         "",
    CardNum:            "",
    MerchantReceiptXml: "",      // ← nunca se llena
    CustomerReceiptXml: "",      // ← nunca se llena
```

**Remediación:** persistir el `Credit` completo (o los XML ya construidos) y devolver *eso* en el
replay. Si por alguna razón no se puede reconstruir, es preferible devolver `Failed` con instrucción
de consultar en Sistecrédito que imprimir ceros.

---

### 🔴 C-5 · Los abonos no tienen idempotencia: doble cobro posible

**Archivos:** [PagoViewModel.cs:102-106](../src/SistecreditoTEF.Maui/ViewModels/PagoViewModel.cs#L102-L106),
[PayCreditRequest.cs](../src/SistecreditoTEF.Maui/Dtos/PayCreditRequest.cs)

El flujo de crédito tiene **doble barrera** (local por `SaleId` + `invoice` remoto). El flujo de pago
tiene **una sola, y es volátil**:

```csharp
private bool HasRecentPaymentAttempt() =>
    state.LastPaymentAttemptCreditId == CreditoSeleccionado.CreditId
    && (DateTime.UtcNow - lastAt).TotalSeconds < 60;
```

`ITransactionStateStore` es un singleton **en memoria**. Y `PayCreditRequest` viaja sin ningún
identificador de idempotencia (`CreditId`, `TotalValuePaid`, `UserName` — nada más), así que Credinet
no puede deduplicar del lado servidor.

**Escenario reproducible:**
1. El cajero toca "Pagar" $200.000.
2. Credinet procesa el pago; la respuesta se pierde (timeout de red / POS se reinicia / la app se
   mata por presión de memoria — habitual en terminales POS).
3. El proceso arranca de nuevo → `state` vacío → el guard de 60s no existe.
4. El cajero, que vio un error, vuelve a cobrar → **el cliente paga dos veces**.

**Remediación:**
- Persistir los intentos de pago en el store SQLite cifrado que ya existe, con la misma clave de
  idempotencia que los créditos.
- Solicitar a Sistecrédito un campo de idempotencia en `payCredit` (equivalente a `invoice`).
- Antes de reintentar, consultar `getCreditDetails`/`getactivecredits` y comparar el saldo para
  detectar si el pago anterior sí entró.

---

## 4. Hallazgos altos

### 🟠 A-1 · Cédula completa en logcat (control existente, no aplicado)

El proyecto documenta enmascarado de cédula como control de seguridad, y `HttpLoggingHandler` sí lo
hace en la URL. Pero se filtra por otros cuatro caminos:

| Archivo:línea | Código |
|---|---|
| [CapturaCedulaViewModel.cs:123](../src/SistecreditoTEF.Maui/ViewModels/CapturaCedulaViewModel.cs#L123) | `Log.Info("CCVM", $"ENTER doc={NumeroDocumento} ...")` |
| [CapturaCedulaViewModel.cs:201](../src/SistecreditoTEF.Maui/ViewModels/CapturaCedulaViewModel.cs#L201) | `$"...validando {TipoDocumento.Code()} {NumeroDocumento}"` |
| [CredinetApiClient.cs:159,166,173,180](../src/SistecreditoTEF.Maui/Services/Credinet/CredinetApiClient.cs#L159) | `$"...en {request.Method} {request.RequestUri}"` ← la URL cruda con `idDocument=` |

Lo llamativo: `CapturaCedulaViewModel` **tiene** un helper `Mask()` y lo usa en la línea 97, pero no
en las líneas 123 y 201. Además esos `Log.Info` de traza (`ENTER`, `calling`, `await done`,
`navigating`, `EXIT`) están bajo `#if ANDROID`, **no bajo `#if DEBUG`**, así que se envían en Release.

**Impacto:** habeas data (Ley 1581 de 2012). En terminales POS con `adb` habilitado —común para
soporte— cualquiera con acceso USB extrae el histórico de cédulas atendidas.

**Remediación:** aplicar `Mask()` en todos los puntos, envolver la traza de diagnóstico en
`#if DEBUG`, y loguear `request.RequestUri` pasándolo por el mismo `MaskUrl` del handler.

---

### 🟠 A-2 · La auditoría persiste la cédula completa

**Archivo:** [SistecreditoService.cs:64-67](../src/SistecreditoTEF.Maui/UseCases/SistecreditoService.cs#L64-L67)

```csharp
_audit.Log(AuditActions.ValidateOk,
    $"cedula={cliente.DocumentId}, disponibles=${cliente.AvailableCreditLimit:N0}");
```

Ese comment va a **dos destinos**: el broadcast a HioPos (`icg.actions.externalApi.AUDIT`, sale del
sandbox de la app hacia el POS) y la tabla `audit_log` local. La BD local está cifrada con SQLCipher
(bien), pero el broadcast no lo está y no hay retención definida.

**Remediación:** enmascarar en el comment de auditoría; si se necesita la cédula completa para
conciliación, dejarla solo en la BD cifrada y no en el broadcast.

---

### 🟠 A-3 · La `SUBSCRIPTION_KEY` de producción se guarda sin cifrar

**Archivo:** [CloudConfigStore.cs:48](../src/SistecreditoTEF.Maui/Services/Platform/CloudConfigStore.cs#L48)

```csharp
Preferences.Set(Prefix + key.Trim(), p.Value?.Trim() ?? string.Empty);
```

`Preferences` = `SharedPreferences` de Android: privado de la app pero **texto plano en disco**. La
limitación está reconocida en el comentario de la clase, lo cual es honesto — pero el proyecto **ya
tiene** `SecureStorage` (Android Keystore) y SQLCipher funcionando, así que la mitigación está
disponible y no se usó para la credencial más sensible del sistema.

**Mitigantes:** `android:allowBackup="false"` está correctamente puesto; el POS es un dispositivo
controlado. **Aun así:** un POS rooteado o un volcado de `/data/data/com.pos2pay/shared_prefs/`
entrega la key de APIM de producción.

**Remediación:** migrar `SUBSCRIPTION_KEY` a `SecureStorage`. El resto de parámetros (URL, storeId,
environment) pueden quedarse en `Preferences`.

---

### 🟠 A-4 · Dependencia transitiva con vulnerabilidad de severidad alta

```
SQLitePCLRaw.lib.e_sqlite3.android  2.1.2  High  GHSA-2m69-gcr7-jv3q
```

Entra por `sqlite-net-pcl` 1.9.172. El proyecto referencia
`SQLitePCLRaw.bundle_e_sqlcipher` 2.1.10, pero el `lib.e_sqlite3.android` viejo se resuelve igual.

**Remediación:** fijar `SQLitePCLRaw.lib.e_sqlite3.android` a una versión parcheada con un
`PackageReference` explícito, y añadir `dotnet list package --vulnerable` al pipeline como gate.

---

### 🟠 A-5 · El certificate pinning está inactivo en producción

**Archivos:** [ApiConfig.cs:59](../src/SistecreditoTEF.Maui/Services/Credinet/ApiConfig.cs#L59),
[appsettings.json](../src/SistecreditoTEF.Maui/appsettings.json)

La implementación de `CertificatePinning` es **correcta** (no acepta cadena inválida, compara SPKI
del leaf y de la cadena). Pero `CertificatePins` está vacío en `appsettings.json` y **no se lee de
CloudLicense**, así que el pinning nunca se activa: hoy es TLS estándar.

**Impacto:** un POS con un CA corporativo instalado (proxy de inspección) permite MITM del tráfico
Credinet, incluyendo la `Ocp-Apim-Subscription-Key` y los OTP.

**Remediación:** pedir los pines SPKI a Sistecrédito, cargarlos, y añadir `CERTIFICATE_PINS` a los
parámetros que se leen de CloudLicense. Documentar el plan de rotación (pinear al menos dos: actual
y backup).

---

### 🟠 A-6 · Un timeout de `SecureStorage` destruye la BD local

**Archivo:** [SecureDb.cs:80-103](../src/SistecreditoTEF.Maui/Services/Platform/SecureDb.cs#L80-L103)

```csharp
try { existing = await SecureStorage...GetAsync(...).WaitAsync(TimeSpan.FromSeconds(3)); }
catch (Exception ex) { AppLogger.W(...); }        // ← timeout: existing queda null

if (string.IsNullOrEmpty(existing))
    existing = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));   // ← llave NUEVA
```

Si `SecureStorage` **existía pero tardó más de 3s**, se genera una llave nueva, la BD cifrada con la
llave vieja no se puede abrir, y `OpenAsync` la **borra y la recrea vacía**
([SecureDb.cs:37](../src/SistecreditoTEF.Maui/Services/Platform/SecureDb.cs#L37)).

**Impacto:** se pierden a la vez el **store de idempotencia** (queda solo la barrera remota
`invoice`) y **toda la auditoría financiera local**, sin recuperación y sin alerta. Además el
`catch` de `OpenAsync` es demasiado amplio: cualquier fallo de `CreateTableAsync` (disco lleno, BD
bloqueada) dispara el borrado.

**Remediación:** distinguir "no había llave" de "no pude leer la llave". En el segundo caso
reintentar con backoff y **no borrar nada**. Restringir el borrado a la excepción específica de
`SQLITE_NOTADB`/llave incorrecta, y auditar el evento de recreación.

---

### 🟠 A-7 · `ConfirmacionViewModel` puede dejar a HioPos colgado hasta el timeout

**Archivo:** [ConfirmacionViewModel.cs:55-74](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L55-L74)

```csharp
private void Finalizar()
{
    if (Credito is null) return;        // ← nunca se llama a FinishWithResult
```

El POS espera un `setResult`. Si `Credito` es null (state limpiado, proceso recreado, cold start en
esa pantalla), el cajero toca "Finalizar" y **no pasa nada**: HioPos espera, hace timeout y —según
el propio comentario de `HioposConstants`— "saca la factura sin hacer nada".

`BuildResponse()` **sí tiene** la rama que arma un `Failed` para ese caso (líneas 93-102), pero es
**código muerto**: el early-return la hace inalcanzable.

**Contraste:** `ReciboPagoViewModel` hace lo correcto — cuando `Pago is null` devuelve `Failed`
explícito ([ReciboPagoViewModel.cs:73-84](../src/SistecreditoTEF.Maui/ViewModels/ReciboPagoViewModel.cs#L73-L84))
y envuelve todo en try/catch. La asimetría entre los dos ViewModels es el defecto.

**Remediación:** eliminar el early-return y dejar que `BuildResponse()` devuelva el `Failed`; envolver
en try/catch como el de pagos.

---

### 🟠 A-8 · Dos conversiones pesos→centavos incompatibles; una pierde centavos

**Archivos:** [MoneyConverter.cs:31-32](../src/SistecreditoTEF.Maui/Common/MoneyConverter.cs#L31-L32),
[ConfirmacionViewModel.cs:243](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L243),
[ReciboPagoViewModel.cs:187](../src/SistecreditoTEF.Maui/ViewModels/ReciboPagoViewModel.cs#L187)

```csharp
// MoneyConverter — TRUNCA
public static long FromPesosToCents(double pesos) => (long)(pesos * 100.0);

// Los dos ViewModels — REDONDEAN (y está duplicado entre ellos)
private static string ToCents(double pesos) => ((long)Math.Round(pesos * 100)).ToString(...);
```

Verificado en runtime: `8,29 → 828` (trunca) vs `829` (redondea). El comentario de `MoneyConverter`
afirma *"Idempotente, sin perdida"* — es **falso**.

**Agravante de tipos:** `TransactionStateStore.CreditValue` es `decimal` (correcto para dinero) pero
se castea a `double` en cada uso (`(double)state.CreditValue`, `(double)Monto`), y todos los modelos
de dominio usan `double`. Se declara la precisión y luego se descarta.

**Por qué no lo detectó el test:** `MoneyConverterTests` solo prueba `500_000.0`, un valor exactamente
representable en binario, donde truncar y redondear coinciden.

**Remediación:** una sola función compartida, con `decimal` y `MidpointRounding.AwayFromZero`
(convención contable en COP). Migrar los modelos de dominio de `double` a `decimal`.

---

### 🟠 A-9 · El `Amount` devuelto al POS sobrescribe el solicitado sin validación

**Archivos:** [ConfirmacionViewModel.cs:199](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L199),
[ReciboPagoViewModel.cs:149](../src/SistecreditoTEF.Maui/ViewModels/ReciboPagoViewModel.cs#L149)

Ambos flujos devuelven un `Amount` calculado por la app, no el del Intent:

```csharp
[HioposExtras.Amount] = ToCents(Credito.CreditValue);       // crédito
[HioposExtras.Amount] = ToCents(paidTotal);                 // pago (capital+interés+mora+seguro+cargos)
```

La decisión está **razonada** en los comentarios (que el POS y la DIAN no difieran) y es defendible.
El problema es que **no hay ninguna comprobación de que coincida** con el `Amount` que HioPos pidió.
Si difieren, HioPos recibe un pago por un monto distinto al de la venta y la factura queda
descuadrada — el POS pedirá el resto o rechazará el cierre.

**Remediación:** comparar contra `tx.AmountCents` y, si la diferencia excede una tolerancia (p. ej.
1 peso), auditar y decidir explícitamente: devolver `Failed` con mensaje claro, o usar los campos
`FixedPaymentMeanId`/`FixedPaymentMeanAmount` (ya declarados en `HioposExtras` y sin usar) que
existen justamente para esto.

---

### 🟠 A-10 · No hay resolución de transacciones "en duda"

**Archivos:** [OtpViewModel.cs:289-297](../src/SistecreditoTEF.Maui/ViewModels/OtpViewModel.cs#L289-L297),
[HioposConstants.cs:150](../src/SistecreditoTEF.Maui/Services/Hiopos/HioposConstants.cs#L150)

`POST /create` no es reintentable (correcto). Pero si responde con timeout **después** de haber
creado el crédito en Sistecrédito:

- El store local nunca guardó el `SaleId` (el `SaveAsync` está en la rama `Ok`).
- La UI cuenta el fallo como intento de OTP inválido y muestra "El código no es válido".
- `SupportsTransactionQuery = false`, así que el POS no puede consultar el estado.
- No hay pantalla ni flujo de conciliación.

**Resultado:** el cliente tiene un crédito real, el cajero cree que falló, la venta se pierde y nadie
lo detecta hasta la conciliación contable.

**Remediación:** ante `NetworkException` en `create`, no reportar "código inválido". Consultar
`getactivecredits` con la cédula y el monto para detectar el crédito recién creado, y ofrecer al
cajero "Verificar si el crédito se creó". Registrar el intento en el store local **antes** del POST
(estado `pendiente`) y resolverlo después.

---

### 🟠 A-11 · El "debounce" no debounce: N llamadas concurrentes y carrera de datos

**Archivo:** [SeleccionCuotasViewModel.cs:146-152](../src/SistecreditoTEF.Maui/ViewModels/SeleccionCuotasViewModel.cs#L146-L152)

```csharp
public async Task OnMontoChangedAsync(decimal monto)
{
    CancellationTokenSource? localCts = null;    // ← declarado y JAMÁS usado (warning CS0219)
    try
    {
        await Task.Delay(500);                   // ← sin token: no cancela nada
```

El comentario dice *"Debounce 500ms para no spamear la API"*. Lo que hace es **retrasar cada
pulsación 500ms y luego lanzarlas todas**. El `catch (TaskCanceledException) { /* debounced */ }` es
código muerto porque nada cancela.

**Prueba de estrés (escribir `1500000`, 7 dígitos):** 7 llamadas a `getSimulatedMonthLimit` en
paralelo. Como las respuestas pueden llegar desordenadas, `MaxMonths` puede quedar fijado por la
respuesta de un monto **anterior** → el cajero ve plazos que no corresponden al monto en pantalla, y
`MesesSeleccionados` se ajusta a esa lista incorrecta.

**Remediación:** un `CancellationTokenSource` a nivel de instancia, cancelado al inicio de cada
llamada y pasado a `Task.Delay(500, token)`; descartar respuestas obsoletas comparando el monto.

---

### 🟠 A-12 · El límite de intentos de OTP es solo de cliente y se puede reiniciar

**Archivo:** [OtpViewModel.cs:124,222,281,293](../src/SistecreditoTEF.Maui/ViewModels/OtpViewModel.cs#L124)

El contador de intentos vive en `private int _attempts` **dentro del ViewModel**, y los ViewModels
están registrados como **`Transient`**
([AppServicesRegistration.cs:120](../src/SistecreditoTEF.Maui/AppServicesRegistration.cs#L120)).

**Consecuencia:** navegar hacia atrás y volver a la pantalla de OTP crea un ViewModel nuevo con
`_attempts = 0`, mientras el mismo OTP sigue vigente. El tope de 3 intentos se **reinicia a voluntad**.

El throttle de reenvíos sí es singleton (bien diseñado), así que no se pueden pedir códigos nuevos
sin límite — pero *verificar* el código sí es prácticamente ilimitado durante la vida del OTP.

Para un OTP de 6 dígitos el riesgo estadístico es bajo, pero es un **control de seguridad
declarado que no se cumple**, y la protección real depende enteramente de que Credinet limite del
lado servidor (no verificado en esta auditoría).

**Remediación:** mover el contador de intentos al singleton `OtpRequestThrottle` (junto al de
reenvíos) y **confirmar con Sistecrédito** que `create` aplica rate-limit por token del lado
servidor. Un control de fuerza bruta no debe vivir en el cliente.

---

## 5. Hallazgos medios

### 🟡 M-1 · `DelegatingHandler` como singleton + dependencia cautiva

**Archivo:** [AppServicesRegistration.cs:40-45,77](../src/SistecreditoTEF.Maui/AppServicesRegistration.cs#L40-L45)

```csharp
services.AddSingleton<AuthInterceptor>();
services.AddSingleton<HttpLoggingHandler>();
...
services.AddSingleton<ICredinetRepository, CredinetRepository>();
```

Registrar `DelegatingHandler` como singleton es un antipatrón conocido: cuando `HttpClientFactory`
rota la cadena de handlers (`HandlerLifetime`, 2 min por defecto) intenta reasignar `InnerHandler`
sobre la **misma instancia ya usada**, y el setter lanza `InvalidOperationException`.

**Hoy no explota** porque `CredinetRepository` es singleton y captura el `HttpClient` (transient) para
siempre — una **dependencia cautiva** que impide la rotación. Es decir: **dos defectos que se
cancelan mutuamente.** Si alguien cambia `CredinetRepository` a transient/scoped —un refactor
aparentemente inocuo— todo el HTTP se rompe después de 2 minutos.

**Remediación:** registrar los handlers como `Transient` (lo que espera `HttpClientFactory`) y dejar
que la factory maneje la vida del `HttpClient`. Documentar por qué el repositorio es singleton.

---

### 🟡 M-2 · `ApiConfig` singleton: los parámetros de CloudLicense quedan obsoletos

`ApiConfig` se construye una vez y vive todo el proceso. Si ICG cambia un parámetro en CloudLicense
y HioPos reenvía `INITIALIZE` **sin que el proceso muera**, `CloudConfigStore` guarda el valor nuevo
pero `ApiConfig` sigue con el viejo. La precedencia documentada ("CloudLicense gana") solo se cumple
si el `ApiConfig` se construye después del `INITIALIZE`, lo cual es accidental, no garantizado.

**Remediación:** reconstruir `ApiConfig` en el handler de `INITIALIZE`, o exponerlo vía
`IOptionsMonitor`/factory que relea `CloudConfigStore`.

---

### 🟡 M-3 · Varios parámetros documentados como configurables por CloudLicense no lo son

[ApiConfig.cs:128-140](../src/SistecreditoTEF.Maui/Services/Credinet/ApiConfig.cs#L128-L140) lee de
`section[...]` únicamente —nunca de `Cloud(...)`— para: `Source`, `AuthMethod`, `Frequency`,
`TimeoutSeconds`, `OtpResendCooldownSeconds`, `OtpMaxResends`, `OtpVerifyCooldownSeconds` y
`CertificatePins`. El comentario de la línea 44 afirma *"Configurable via appsettings.json o
CloudConfigStore sin recompilar"*: para estos campos es falso — requieren recompilar el APK.

---

### 🟡 M-4 · `OtpMaxResends`: 5 en configuración vs 3 documentado

`appsettings.json` dice `5`; el default del código, los comentarios de `ApiConfig`,
`OtpRequestThrottle` y la documentación de arquitectura de la época dicen `3`. El valor efectivo es **5**.

---

### 🟡 M-5 · Campo `Frequency` del `ModifyDocumentResult` recibe los meses

**Archivo:** [ConfirmacionViewModel.cs:184](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L184)

```csharp
("Frequency", state.Months.ToString(...))     // ← Months, no Frequency
```

En Credinet `Frequency` es la periodicidad en días (30). Se está enviando el número de cuotas al
módulo fiscal bajo una etiqueta que significa otra cosa. Además `Fees` se toma de `Credito.Fees`
(API) mientras el voucher usa `state.Months` (UI): **dos fuentes para el mismo dato**, que pueden
discrepar si la API ajusta el plazo.

---

### 🟡 M-6 · `PaymentMeanId = "2"` hardcodeado en dos lugares

[ConfirmacionViewModel.cs:167](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L167) y
[ReciboPagoViewModel.cs:128](../src/SistecreditoTEF.Maui/ViewModels/ReciboPagoViewModel.cs#L128), con
un `TODO` reconociéndolo. Si el CloudLicense de alguna tienda mapea el medio 2 a otra cosa, el
enriquecimiento DIAN se adosa al medio de pago equivocado. Debería venir de `CloudConfigStore`.

---

### 🟡 M-7 · Tienda y cajero hardcodeados; `ShopData` se ignora

`Tienda => "Permoda"` y `Cajero: "Cajero Permoda"` están fijos en el código
([ConfirmacionViewModel.cs:33](../src/SistecreditoTEF.Maui/ViewModels/ConfirmacionViewModel.cs#L33),
[ReciboPagoViewModel.cs:91,93](../src/SistecreditoTEF.Maui/ViewModels/ReciboPagoViewModel.cs#L91)).
El extra `ShopData` que HioPos envía **nunca se lee**, y `payCredit` manda
`userName: "Cajero Permoda"` para todos los abonos, aunque `SellerData` está disponible (el flujo de
crédito sí lo extrae). Con múltiples tiendas KOAJ, todos los vouchers y toda la traza de auditoría en
Credinet quedan indistinguibles.

---

### 🟡 M-8 · Los `CancellationTokenSource` de OTP están entrelazados

**Archivo:** [OtpViewModel.cs:418-438](../src/SistecreditoTEF.Maui/ViewModels/OtpViewModel.cs#L418-L438)

`StartVerifyCooldown()` reutiliza `_cooldownCts` —el CTS del cooldown de **reenvío**— como su token:

```csharp
var token = _cooldownCts?.Token ?? CancellationToken.None;
```

Tres consecuencias: (a) cancelar el cooldown de reenvío mata el de verificación y viceversa; (b) si
`_cooldownCts` es null, el token es `None` y el bucle **no se puede cancelar**; (c) tras
`CancelResendCooldown()` (que hace `Dispose()`), el bucle en vuelo llama `Task.Delay(1000, token)`
sobre un CTS liberado → `ObjectDisposedException`, que **no** está capturada (solo se captura
`TaskCanceledException`) y se pierde como excepción no observada en un `Task.Run` fire-and-forget.

Relacionado: warning **CA1001** — `OtpViewModel` tiene campos `IDisposable` y no implementa
`IDisposable`, así que los CTS se filtran en cada navegación.

---

### 🟡 M-9 · Un error de red se cuenta como OTP incorrecto

**Archivo:** [OtpViewModel.cs:289-297](../src/SistecreditoTEF.Maui/ViewModels/OtpViewModel.cs#L289-L297)

El `catch (Exception)` hace `_attempts++` y muestra "El código no es válido. Te quedan N intentos".
Tres cortes de red seguidos → "Se agotaron los intentos" con un OTP perfectamente válido. Los fallos
de infraestructura no deben consumir intentos de autenticación.

---

### 🟡 M-10 · Thread-safety inconsistente en `TransactionStateStore`

**Archivo:** [TransactionStateStore.cs:22-25](../src/SistecreditoTEF.Maui/Services/Platform/TransactionStateStore.cs#L22-L25)

Todos los campos de referencia están protegidos por `lock (_gate)`, pero cuatro propiedades no:

```csharp
public decimal   CreditValue                { get; set; }   // sin lock
public int       Months                     { get; set; }   // sin lock
public string?   LastPaymentAttemptCreditId  { get; set; }   // sin lock
public DateTime? LastPaymentAttemptAt        { get; set; }   // sin lock
```

`Clear()` las escribe **dentro** del lock, pero los accesos externos no. `decimal` y `DateTime?` no
son de escritura atómica, así que hay riesgo real de lectura desgarrada — y `LastPaymentAttemptAt` es
precisamente la barrera anti-doble-cobro (ver C-5).

---

### 🟡 M-11 · Un fallo transitorio de BD inutiliza idempotencia y auditoría para siempre

`SqliteIdempotencyStore` y `SqliteAuditLog` usan `Lazy<Task<SQLiteAsyncConnection>>`. Si la task de
inicialización falla, **el `Lazy` cachea la task fallida** y todas las llamadas siguientes fallan
durante toda la vida del proceso. Un bloqueo momentáneo de la BD al arrancar deja al POS operando el
día entero sin barrera de idempotencia local.

**Remediación:** reintento con reinicialización del `Lazy` (o `AsyncLazy` con retry).

---

### 🟡 M-12 · La auditoría es fire-and-forget y el `Finish()` es inmediato

[SqliteAuditLog.cs:44](../src/SistecreditoTEF.Maui/Services/Platform/SqliteAuditLog.cs#L44) hace
`_ = PersistAsync(entry)` sin esperar, y `FinishWithResult` llama `activity.Finish()` de inmediato.
Para una traza financiera, la escritura de `CreditCreated`/`Payment` debería confirmarse (o al menos
hacer flush) antes de devolver el control al POS.

---

### 🟡 M-13 · Sin retención ni purga en las tablas locales

`cached_transactions` y `audit_log` crecen indefinidamente. `CachedTransaction.CreatedAt` existe pero
nunca se usa para purgar; `SqliteAuditLog.Clear()` no lo invoca nada periódicamente. En un POS que
opera años, la BD cifrada crece sin techo y `FindBySaleIdAsync` se degrada. Definir una política de
retención (p. ej. 90 días de idempotencia, 1 año de auditoría).

---

### 🟡 M-14 · "18 flags" documentado vs 17 emitidos

`HioposCapabilities` define **17** flags y `BuildBehavior` emite 17 entradas, pero el nombre del test,
el docstring de `BuildBehavior` ("las 18 capacidades") y `MainActivity` ("B3: 18 flags obligatorios")
dicen 18. O sobra en la documentación o **falta un flag del contrato**. Dado que el propio código
advierte que HioPos hace NPE al leer valores ausentes, conviene verificarlo contra el manual de ICG.
El test no valida la cantidad, así que no lo detectaría.

---

### 🟡 M-15 · El voucher dice SMS pero el canal es WhatsApp

**Archivo:** [ReceiptBuilder.cs:67](../src/SistecreditoTEF.Maui/Services/Hiopos/ReceiptBuilder.cs#L67)

```csharp
{CenterLine("Recibiras un SMS con la clave")}
```

El canal confirmado por Sistecrédito para test y producción es **WhatsApp** (`OtpDestination = 1`, y
`appsettings.json` lo tiene en 1). El cliente espera un SMS que nunca llega → llamadas a soporte y
ventas abandonadas. Es una corrección de una línea con impacto operativo directo.

---

### 🟡 M-16 · Mensajes de excepción técnicos mostrados al cajero

`ErrorMessage = $"Error inesperado: {ex.Message}"` en
[PagoViewModel.cs:145](../src/SistecreditoTEF.Maui/ViewModels/PagoViewModel.cs#L145) y
[SeleccionCuotasViewModel.cs:130,179](../src/SistecreditoTEF.Maui/ViewModels/SeleccionCuotasViewModel.cs#L130).
Existe `FriendlyMessage` justamente para esto y no se usa en las ramas de excepción. Puede exponer
detalles internos (host, rutas, stack de red) en la pantalla del POS, frente al cliente.

---

### 🟡 M-17 · XML de venta malformado aborta la transacción en vez de degradar

[MainActivity.cs:425](../src/SistecreditoTEF.Maui/Platforms/Android/MainActivity.cs#L425) llama
`XmlDocumentReader.Parse(...)` sin try/catch. `Parse` lanza ante XML inválido (a diferencia de
`Read`, que captura y devuelve null). La excepción sube al `catch` de `HandleIntent` → `Canceled`,
así que la venta entera se cancela cuando lo correcto sería continuar sin autocompletar la cédula
(el documento solo aporta conveniencia y el `SaleId`).

---

### 🟡 M-18 · Comentario de `apk_name` desactualizado

[MainActivity.cs:19](../src/SistecreditoTEF.Maui/Platforms/Android/MainActivity.cs#L19) documenta
`icg.actions.electronicpayment.sistecredito.XXX`. El `apk_name` real es **`permoda`**. El código está
bien (usa la constante); el comentario induce a error en el dato más crítico de la integración.

---

### 🟡 M-19 · Key de sandbox hardcodeada en el repositorio

[ApiConfig.cs:73](../src/SistecreditoTEF.Maui/Services/Credinet/ApiConfig.cs#L73) contiene
`SandboxSubscriptionKey = "88dec4b8617c4644a239a8af283dc742"`. Es pública (viene del manual), así
que el riesgo es bajo, pero la convierte en el fallback silencioso de C-3 y aparecerá en cualquier
escaneo de secretos. Preferible moverla a configuración.

---

## 6. Hallazgos bajos y deuda técnica

| # | Hallazgo | Ubicación |
|---|---|---|
| B-1 | 5 warnings de posible desreferencia nula (`CS8602`/`CS8604`) en código de producción | `MauiProgram:73`, `MainActivity:121,506`, `SunmiPrinter:40`, `PdfReceiptPrinter:33,49`, `CredinetApiClient:123` |
| B-2 | `ToCents` y `SanitizeForDian` duplicados literalmente entre los dos ViewModels de recibo | `ConfirmacionViewModel`, `ReciboPagoViewModel` |
| B-3 | `ExtractSellerName` duplicado (VM + servicio) y basado en regex sobre XML: frágil ante namespaces, atributos y entidades | `SistecreditoService:261`, `ConfirmacionViewModel:253` |
| B-4 | `MaskDocumentId` no enmascara documentos de ≤4 caracteres (devuelve el valor completo) | `ConfirmacionViewModel:246` |
| B-5 | `await Task.CompletedTask;` sin efecto | `ReciboPagoViewModel:86` |
| B-6 | `StoreId: ""` en appsettings → se omite el parámetro en todas las consultas; si producción lo exige, falla | `appsettings.json` |
| B-7 | ~20 warnings `CA1305`/`CA1304` de cultura: `ToString()` sin `IFormatProvider` en montos y fechas del voucher | varios |
| B-8 | 5 usos de APIs MAUI obsoletas (`ScaleTo`/`FadeTo`/`TranslateTo`) | `UiBehaviors` |
| B-9 | `XA0141`: `libe_sqlite3.so` sin páginas de 16 KB — **Android 16 lo exigirá** | dependencia SQLitePCLRaw |
| B-10 | `FechaPago => DateTime.Now...` recalcula en cada acceso; puede mostrar horas distintas en la misma pantalla | `ReciboPagoViewModel:31` |
| B-11 | Mezcla de `DateTime.Now` (auditoría, recibos) y `DateTime.UtcNow` (throttle, idempotencia) sin criterio documentado | varios |
| B-12 | 4 métodos de `SistecreditoService` sin auditoría, pese a que la clase declara auditar "cada operación clave" | `CalcularCuotaAsync`, `ObtenerCreditosActivosAsync`, `ObtenerLimiteMesesAsync` |
| B-13 | `BuildQs` lanza `IndexOutOfRange` si recibe un número impar de argumentos (hoy no ocurre) | `CredinetApiClient:114` |
| B-14 | `PuedeReenviar = AttemptsLeft <= 0 \|\| ResendCooldownSeconds <= 0` — lógica redundante y confusa; `CanResend` ya lo filtra | `OtpViewModel:285` |

---

## 7. Lo que está bien hecho

Es importante para calibrar el informe: estos controles se auditaron **y pasaron**.

| Área | Veredicto |
|---|---|
| **XXE / billion laughs** | ✅ No explotable. `XDocument.Parse` en .NET usa `DtdProcessing.Prohibit` por defecto, así que DTDs y entidades externas se rechazan. Recomendación menor: pasar `XmlReaderSettings` explícitos para no depender de un default implícito. |
| **Inyección en query string** | ✅ Correcto. `BuildQs` aplica `Uri.EscapeDataString` a claves y valores; se verificó que no hay concatenación cruda. |
| **Lógica de certificate pinning** | ✅ Implementación correcta: rechaza cadena inválida antes de comparar, compara SPKI del leaf y de toda la cadena, permite pinear intermedia/raíz. El problema es que no está activada (A-5), no cómo está escrita. |
| **Redacción de la API key en logs** | ✅ `HttpLoggingHandler` redacta `Ocp-Apim-Subscription-Key` y `Authorization` correctamente. |
| **Política de reintentos** | ✅ Ejemplar. Solo GET seguros, excluye explícitamente `getCreditToken` y todo POST, backoff corto, techo global por `HttpClient.Timeout`. Es exactamente la política correcta para un TEF. |
| **Fail-safe del contrato HioPos** | ✅ `HandleIntent` envuelve todos los handlers y devuelve `Canceled` ante excepción, con un segundo try/catch para el finish. Evita el "error en módulo externo". |
| **Escapado XML de vouchers** | ✅ `Escape()` en `ReceiptBuilder` y `ModifyDocumentResultBuilder` ordena bien (`&` primero) y neutraliza saltos de línea. |
| **Regla anti-doble-envío DIAN** | ✅ `ModifyDocumentResultBuilder` emite exclusivamente `PaymentMeans`, sin líneas ni totales. La restricción está bien entendida y bien implementada. |
| **Cifrado en reposo** | ✅ SQLCipher con llave de 32 bytes de `RandomNumberGenerator` en Android Keystore; `allowBackup=false`. Correcto en diseño (el defecto está en el manejo del timeout, A-6). |
| **Ciclo de vida del Intent** | ✅ Procesar en `OnResume` (no `OnCreate`) es la decisión correcta y está bien justificada. |
| **Guard de colisión de modos** | ✅ La bandera de liveness explícita `HioposTransactionActive` es mejor que inferir del estado residual, tal como dice el comentario. |
| **`ApiResult<T>`** | ✅ Buen diseño; los `switch` exhaustivos en los ViewModels evitan errores silenciosos. |

---

## 8. Cobertura de pruebas: análisis de brechas

**73 tests en 13 archivos.** El patrón es claro y problemático: **se prueba la capa pura y fácil; no
se prueba nada donde vive el dinero, la seguridad o el estado.**

### Cubierto

`MoneyConverter` (parcial), `DocumentType`, `ApiResult`, `CredinetMapper`, `HioposIntentParser`,
`TransactionResult`, `HioposResultBuilder`, `CredinetApiClient` (construcción de request),
`AuthInterceptor`, `SeleccionCuotasViewModel` (parcial), `ReceiptBuilder`, `XmlDocumentReader`,
`ModifyDocumentResultBuilder`, `SistecreditoService` (parcial).

### Sin cobertura — y con hallazgo asociado en este informe

| Componente sin tests | Hallazgo que un test habría atrapado |
|---|---|
| `CloudConfigStore` | 🔴 C-3 (namespace, params faltantes) |
| `SistecreditoService` — camino de replay de idempotencia | 🔴 C-4 (voucher en ceros) |
| `ConfirmacionViewModel` | 🟠 A-7 (POS colgado), 🟠 A-9 (Amount), 🟡 M-5 (`Frequency`) |
| `ReciboPagoViewModel` | 🟠 A-9 |
| `PagoViewModel` | 🔴 C-5, rango de `CanPagar`, anti-doble-cobro |
| `OtpRequestThrottle` | 🟠 A-12, lógica de cooldown anidada |
| `OtpViewModel` | 🟠 A-12, 🟡 M-8, 🟡 M-9 |
| `CertificatePinning` | control de seguridad con **cero** tests |
| `ApiConfig.FromConfiguration` | 🔴 C-3, 🟡 M-2, 🟡 M-3 (la precedencia documentada nunca se verifica) |
| `CredinetHttpPolicies.IsRetryable` | política de seguridad crítica sin tests |
| `TransactionStateStore` | 🟡 M-10 |
| `SecureDb` | 🟠 A-6 |
| `FriendlyMessage` | fuga de detalles técnicos |
| `MoneyConverter.FromPesosToCents` | 🟠 A-8 — **hay un test, pero solo con valores exactos** |

El caso de `MoneyConverterTests` es el más instructivo: existe un test, pasa, y **oculta** el defecto
porque solo prueba `500_000.0`. Cobertura sin casos límite es peor que nada: da confianza falsa.

---

## 9. Casos de prueba recomendados (batería propuesta)

Priorizados por hallazgo. Todos son ejecutables como tests unitarios salvo los marcados 🔧 (requieren
terminal o emulador).

### Dinero y redondeo

1. `FromPesosToCents` con `8.29`, `0.29`, `70.07`, `19999.99` → esperar 829, 29, 7007, 1999999.
2. Propiedad: para todo monto con 2 decimales, `FromCentsToPesos(FromPesosToCents(x)) == x`.
3. `ToCents` y `FromPesosToCents` deben coincidir para 10.000 montos aleatorios.
4. `Amount` devuelto al POS == `Amount` del Intent cuando el cajero no edita el monto.

### Idempotencia y consistencia

5. Replay de idempotencia: el voucher reimpreso debe traer cuota, TEA y cuota inicial **iguales** al
   original (falla hoy → C-4).
6. Doble `PagarCreditoAsync` con el mismo `creditId` y monto: el segundo debe bloquearse **tras
   reiniciar el store** (falla hoy → C-5).
7. `create` que lanza `NetworkException`: no debe consumir intento de OTP y debe dejar el intento
   registrado como pendiente.

### Configuración y ambiente

8. `CloudConfigStore.SaveFromXml` con XML **con namespace por defecto** → debe leer los params
   (falla hoy → C-3).
9. `ApiConfig` con `ENVIRONMENT=production` y sin `SUBSCRIPTION_KEY` → debe fallar, no caer a
   sandbox.
10. Precedencia: CloudLicense > appsettings > secrets > default, un test por nivel.

### Contrato HioPos

11. `Finalizar()` con `CreatedCredit == null` → debe emitir `TransactionResult=Failed` (falla hoy → A-7).
12. `BuildBehavior` debe emitir exactamente N flags, con N asertado explícitamente (→ M-14).
13. `HandleTransaction` con `DocumentData` malformado → la transacción continúa sin documento (→ M-17).
14. Voucher: verificar que no aparece la palabra "SMS" cuando `OtpDestination=1` (→ M-15).

### Seguridad

15. `CertificatePinning.Validate`: cadena inválida→false; pin correcto→true; pin incorrecto→false;
    lista vacía→true; pin en intermedia→true.
16. `IsRetryable`: `getCreditToken` → false; cualquier POST → false; otros GET → true.
17. Ningún log debe contener la cédula completa: test que capture `ILogger` y afirme el enmascarado
    (falla hoy → A-1).
18. `MaskDocumentId` con longitudes 1..4 (falla hoy → B-4).

### Concurrencia y estrés

19. `OnMontoChangedAsync` invocado 10 veces en 200ms → **una** llamada a la API (falla hoy → A-11).
20. Respuestas desordenadas del límite de meses → gana la del último monto (falla hoy → A-11).
21. `OtpRequestThrottle` desde 8 hilos concurrentes → `ResendCount` nunca supera `MaxResends`.
22. `TransactionStateStore`: escritura/lectura concurrente de `LastPaymentAttemptAt` sin valores
    desgarrados (→ M-10).
23. 🔧 `SecureDb` con `SecureStorage` que tarda >3s teniendo llave previa → la BD **no** debe borrarse
    (falla hoy → A-6).

### Extremo a extremo 🔧

24. Ciclo completo `INITIALIZE → GET_BEHAVIOR → TRANSACTION → setResult` en terminal real, verificando
    que la factura cuadra.
25. Matar el proceso de la app entre el `create` y el voucher; reabrir; confirmar que no se duplica el
    crédito.
26. Reinstalar el APK firmado de producción sobre el instalado en el piloto → confirmar que **no** da
    `INSTALL_FAILED_UPDATE_INCOMPATIBLE` (valida C-1).
27. Sesión de 8 horas con el proceso vivo, una venta cada 30 min → detecta la rotación de handlers
    (→ M-1) y fugas de CTS (→ M-8).

---

## 10. Plan de remediación priorizado

### Bloque 1 — Antes de cualquier despliegue (1–2 días)

| # | Acción |
|---|---|
| C-1 | Corregir la ruta del keystore; hacer que Release **falle** si no puede firmar; verificar la firma en el pipeline; resolver cuál keystore es el canónico. |
| C-2 | TFM de tests a `net10.0`; reparar el fake y el test de `OnlyUseDocumentPath`; `dotnet test` como gate de CI. |
| C-3 | `LocalName` en `CloudConfigStore`; validación fuerte de params obligatorios; log de `count==0` como error; badge de ambiente en la UI. |
| C-4 | Persistir el crédito completo (o el XML del voucher) y devolverlo en el replay. |
| M-15 | Cambiar "SMS" por "WhatsApp" en el voucher (una línea, impacto operativo inmediato). |

### Bloque 2 — Antes del rollout masivo (3–5 días)

| # | Acción |
|---|---|
| C-5 | Idempotencia persistente de pagos + solicitar campo de idempotencia a Sistecrédito. |
| A-1, A-2 | Enmascarar cédula en todos los logs y en la auditoría; traza de diagnóstico bajo `#if DEBUG`. |
| A-4 | Subir `SQLitePCLRaw.lib.e_sqlite3.android`; `--vulnerable` como gate. |
| A-5 | Obtener y cargar los pines SPKI; activar pinning; documentar rotación. |
| A-6 | Distinguir "sin llave" de "no pude leer la llave"; nunca borrar la BD por timeout. |
| A-7 | Eliminar el early-return de `Finalizar`; try/catch simétrico con el flujo de pago. |
| A-8 | Una sola conversión de dinero, con `decimal` y `AwayFromZero`. |
| A-3 | `SUBSCRIPTION_KEY` a `SecureStorage`. |

### Bloque 3 — Endurecimiento (1–2 semanas)

A-9 (validación de `Amount`), A-10 (resolución de transacciones en duda), A-11 (debounce real), A-12
(intentos de OTP al singleton + confirmar rate-limit servidor), M-1 (handlers transient), M-2/M-3
(recarga y cobertura de config), M-5..M-14, y la batería de tests de la sección 9.

### Bloque 4 — Deuda técnica

Warnings de nulabilidad y cultura, deduplicación (`ToCents`, `SanitizeForDian`, `ExtractSellerName`),
APIs MAUI obsoletas, retención de datos, y `XA0141` antes de que Android 16 llegue a las terminales.

---

## 11. Veredicto de release

> ### 🔴 NO APTO para rollout masivo en el estado actual.

**Fundamento:** el piloto pasó en hardware real (2026-07-07), lo cual valida el *happy path* del
handshake con HioPos — y eso es un logro. Pero un piloto ejercita el camino feliz; esta auditoría
encontró que **los caminos de fallo son donde está el riesgo**, y varios de ellos fallan de la peor
manera posible: silenciosamente y con dinero de por medio.

Los tres que impiden firmar el release:

1. **C-1** — el artefacto que produce el build no está firmado con la llave de producción, y nadie se
   enteraría porque no hay verificación. La firma actual depende de un paso manual no versionado.
2. **C-3** — existe un camino por el que una terminal de producción opera contra sandbox sin avisar.
   Mientras eso sea posible, no hay garantía de que lo que el cajero ve sea real.
3. **C-2** — sin tests ejecutables no hay forma de saber si una corrección introduce una regresión.
   Arreglar los demás hallazgos a ciegas es tan riesgoso como no arreglarlos.

**Camino al sí:** completar el Bloque 1 y el Bloque 2, con evidencia ejecutada (no solo revisada) de
los casos 1–14, 26 y 27 de la sección 9. Con eso el módulo queda en condiciones razonables para
rollout gradual por tienda, con monitoreo de la auditoría de Credinet durante las primeras semanas.

---

### Nota sobre modificaciones al repositorio

Esta auditoría aplicó **un solo cambio**, indispensable para poder ejecutar las pruebas:

- `tests/SistecreditoTEF.Tests/UseCases/SistecreditoServiceTests.cs:186` — se agregó
  `public bool HioposTransactionActive { get; set; }` a `FakeStateStore`.

El runner temporal usado para ejecutar los tests en `net10.0` vive fuera del repositorio (en el
directorio scratch de la sesión) y **no** se incorporó al proyecto: es un andamio de diagnóstico, no
la solución. La solución correcta a C-2 es cambiar el TFM del proyecto de tests real.

---

*Auditoría de seguridad, consistencia, robustez y calidad de código.
Para excepciones al stack oficial o cambios de arquitectura, contactar al Equipo de Arquitectura — DOPE.*
