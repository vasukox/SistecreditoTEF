# SistecreditoTEF.Maui — Documentación de la integración

> **Qué es esto:** módulo de pago (TEF) para **HioPosCloud** que permite pagar/financiar
> con **Sistecrédito (Credinet)** desde las terminales POS de las tiendas Permoda.
> App **.NET MAUI · Android 13+**. Referencia HU: **HU8-973**.
>
> Este documento está pensado para que **cualquier persona que lo lea entienda el
> programa completo**: qué hace, cómo se conecta, qué enviarle a ICG (HioPos), dónde
> poner las credenciales de Credinet, cómo pasar a producción, y cómo está armado el código.

---

## 1. La foto grande — 3 sistemas que se hablan

```
┌─────────────────────┐  Intent Android   ┌──────────────────────┐  HTTPS REST   ┌──────────────────┐
│    HioPosCloud       │ ───────────────▶ │   ESTA APP (.NET)     │ ───────────▶ │   Credinet API   │
│ (POS, Android 13)    │  icg.actions...  │  Módulo TEF Siste-    │  Ocp-Apim-Key │  (Sistecrédito)  │
│  - Arma la factura   │ ◀─────────────── │  crédito              │ ◀─────────── │   Azure APIM     │
│  - Dispara el pago   │  setResult(...)  │  - UI del flujo       │   JSON        │  - Valida cliente│
│  - Imprime recibo    │                  │  - Llama a Credinet   │               │  - Crea crédito  │
│  - Cierra la venta   │                  │  - Devuelve resultado │               │  - Envía OTP     │
└─────────────────────┘                  └──────────────────────┘               └──────────────────┘
```

- **HioPosCloud** (software POS de ICG): cuando el cajero elige "Sistecrédito" como medio de
  pago, dispara un **Intent de Android** a esta app con los datos de la venta.
- **Esta app**: muestra el flujo (cédula → cupo → cuotas → OTP → crédito), habla con Credinet
  por HTTPS, y **le devuelve el resultado a HioPos** (aceptado/fallido + comprobantes).
- **Credinet** (Sistecrédito): valida al cliente, calcula cuotas, manda el OTP y crea el crédito.

---

## 2. Datos técnicos clave (memorízalos / ténlos a mano)

| Dato | Valor actual |
|---|---|
| **Nombre del paquete (packageName)** | `com.pos2pay` |
| **apk_name** (para HioPos) | `permoda` |
| **Versión** | `1.0.0` |
| **Prefijo de acciones HioPos** | `icg.actions.electronicpayment.permoda.` |
| **Acción de auditoría (broadcast)** | `icg.actions.externalApi.AUDIT` |
| **Base URL Credinet (sandbox)** | `https://api.credinet.co/pos/` |
| **Base URL Credinet (producción)** | `https://api.credinet.co/posprod/` |
| **Header de auth Credinet** | `Ocp-Apim-Subscription-Key` |
| **Min Android** | API 24 · **probar en API 33 (Android 13)** |

> El `apk_name` está en un solo lugar: `Services/Hiopos/HioposConstants.cs` →
> `HioposActions.ApkName`. Si ICG te asigna otro nombre, se cambia **ahí** y se recompila.

---

## 3. Flujo funcional (qué pasa, paso a paso)

### 3.1 Flujo CRÉDITO (crear un crédito nuevo)
```
1. Cajero elige "Sistecrédito" en HioPos  → HioPos dispara Intent TRANSACTION a la app
2. App lee la venta (monto, SaleId, etc.)
3. Pantalla "Consultar cliente": cédula   → GET  getCreditLimitClient   (valida cupo)
4. Pantalla "Simular crédito": monto+plazo → GET  getSimulatedMonthLimit (plazos posibles)
                                            → GET  getCreditDetails       (cuota, TEA, aval)
5. Pantalla "Confirmar"                     → GET  getCreditToken         (manda OTP al cliente)
6. Pantalla "Verificación": cajero digita el OTP → POST create           (crea el crédito)
7. Pantalla "Confirmación OK"               → setResult(ACCEPTED) a HioPos + comprobantes
8. HioPos imprime y cierra la factura. Credinet manda SMS de confirmación al cliente.
```

### 3.2 Flujo PAGO (recaudar cuota de un crédito existente)
```
1. Cédula → GET getactivecredits (lista créditos activos)
2. Selecciona crédito + monto → POST payCredit (registra el pago)
3. Recibo → setResult(ACCEPTED) a HioPos
```

---

## 4. 📞 Comunicación con HioPosCloud / ICG — QUÉ ENVIAR Y PEDIR

Para que la app aparezca como medio de pago "Sistecrédito" en el POS, ICG debe **dar de alta
el módulo en CloudLicense**. Esto es lo que **tú les envías/pides**:

### 4.1 Lo que ENVÍAS a ICG
| # | Qué | Valor |
|---|---|---|
| 1 | **packageName** de la app | `com.pos2pay` |
| 2 | **apk_name** | `permoda` |
| 3 | **APK firmado** (release) | El `.apk` de producción (ver §8) |
| 4 | **Versión** | `1.0.0` |
| 5 | **Logo + nombre** del medio de pago | La app ya los expone en `GET_CUSTOM_PARAMS` (nombre = "Sistecredito"). ⚠️ Reemplazar el **logo placeholder** por el real de Permoda antes de release (ver §8). |
| 6 | **Capacidades declaradas** | La app responde `GET_BEHAVIOR` con las flags de §4.3 (solo crédito). |

### 4.2 Lo que PIDES a ICG
- Que **registren el módulo** en CloudLicense con el `packageName` + `apk_name` de arriba.
- Que **asignen el módulo** a cada POS (o subconjunto) de las tiendas.
- Que confirmen el **proceso de validación/aceptación** del APK (si tienen pruebas de aceptación).
- Contacto de soporte y SLA para cambios en CloudLicense.

### 4.3 Capacidades que la app le declara a HioPos (`GET_BEHAVIOR`)
Definidas en `Services/Hiopos/HioposConstants.cs → HioposCapabilities`:

| Flag | Valor | Flag | Valor |
|---|---|---|---|
| SupportsCredit | **true** ✅ | CanPrint | false (imprime HioPos) |
| HasCustomParams | **true** (logo/nombre) | ReadCardFromApi | false |
| canAudit | **true** | SupportsDebit | false |
| OnlyUseDocumentPath | **true** (facturas grandes) | SupportsPartialRefund | false |
| (todo lo demás) | false | SupportsTransactionVoid | false |

### 4.4 Las 11 acciones (intent-filters) que la app escucha
Prefijo `icg.actions.electronicpayment.permoda.` + :
`INITIALIZE`, `FINALIZE`, `GET_BEHAVIOR`, `GET_VERSION`, `SHOW_SETUP_SCREEN`,
`TRANSACTION` ⭐, `GET_CUSTOM_PARAMS`, `GET_PRINT_INFO`, `READ_CARD`, `CHARGE_CARD`, `GET_CARD_DATA`.

> Están declaradas automáticamente vía atributos `[IntentFilter]` en
> `Platforms/Android/MainActivity.cs`. `TRANSACTION` es la que inicia el flujo de pago.

### 4.5 Qué le devuelve la app a HioPos al terminar (`TRANSACTION` → setResult)
`TransactionResult` (ACCEPTED/FAILED/UNKNOWN_RESULT), `TransactionType`, `Amount`, `TipAmount`,
`TaxAmount`, `SurchargeAmount`, `TransactionData` (JSON `{creditId, creditNumber, saleId}` ≤250),
`MerchantReceipt` + `CustomerReceipt` (XML), `AuthorizationId` (= `creditId` de Credinet),
`CardHolder`, `CardType` = "Sistecredito", `CardNum` (cédula enmascarada). Si falla: `ErrorMessage`.

---

## 5. 🔑 Credenciales de Credinet — DÓNDE PONERLAS

Hay **dos fuentes** de credenciales, y la de CloudLicense **tiene prioridad**:

1. **CloudLicense (producción — recomendado).** ICG carga los parámetros en su portal (§4)
   y HioPos los entrega en el `INITIALIZE`. La app los **persiste y los usa con prioridad**
   sobre `appsettings.json`. Así la key **no queda dentro del APK** y se cambia sin recompilar.
   → Implementado en `Services/Platform/CloudConfigStore.cs` + `ApiConfig.FromConfiguration`.
   Parámetros que reconoce: `API_BASE_URL`, `SUBSCRIPTION_KEY`, `STORE_ID`, `ENVIRONMENT`,
   `OTP_DESTINATION` (toman efecto desde el `INITIALIZE`, es decir el arranque del POS).

2. **`appsettings.json` (fallback / sandbox / dev).** Si NO llegan parámetros por CloudLicense,
   se usan estos. Ideal para desarrollo y pruebas en sandbox.

### 📄 `src/SistecreditoTEF.Maui/appsettings.json` (fallback)

```jsonc
{
  "Credinet": {
    "Environment": "sandbox",                         // "sandbox" | "production" (solo para logs)
    "SubscriptionKey": "__SANDBOX__",                 // ⬅️ AQUÍ va la key de producción
    "StoreId": "",                                    // ⬅️ AQUÍ va el StoreId real de Permoda
    "BaseUrl": "https://api.credinet.co/pos/",         // ⬅️ cambiar a .../posprod/ en producción
    "OtpDestination": 0,                              // 0 = SMS · 1 = WhatsApp
    "Frequency": 30,                                  // 30 = mensual · 14 = quincenal
    "Source": "2",                                    // canal POS (fijo por manual)
    "AuthMethod": 1,                                  // método OTP (fijo por manual)
    "TimeoutSeconds": 30,                             // timeout de red
    "CertificatePins": []                             // pines SPKI (opcional, ver §7)
  }
}
```

### Para pasar a PRODUCCIÓN se cambian **3 valores**:
```jsonc
"Environment": "production",
"BaseUrl": "https://api.credinet.co/posprod/",
"SubscriptionKey": "<LA KEY QUE ENTREGÓ SISTECRÉDITO>",
"StoreId": "<EL STORE ID REAL>"
```
**Todo lo demás (paths, métodos, body, headers) es idéntico.** No hay que tocar código.

### ⚠️ Sobre la seguridad de la key
- **NO commitees la key de producción al repositorio.**
- En **desarrollo**, usá User Secrets (no queda en git):
  ```bash
  dotnet user-secrets set "Credinet:SubscriptionKey" "<tu-key>"
  ```
- Para el **APK de producción**, poné la key en `appsettings.json` **solo en la máquina/pipeline
  de build** (que produce el APK firmado), y mantené ese valor fuera del control de versiones.
- El valor especial `"__SANDBOX__"` usa la key pública de pruebas del manual (solo sandbox).

### Nota sobre CloudLicense (implementado ✅)
La app **sí consume** los parámetros que ICG provisiona en CloudLicense: al recibir `INITIALIZE`,
`MainActivity` parsea el XML de `Parameters` y lo persiste (`CloudConfigStore`), y `ApiConfig` los
usa **con prioridad** sobre `appsettings.json`. Si no llegan, cae al `appsettings.json`.
El XML esperado está en §4.

---

## 6. ⚙️ Configuración completa (todas las claves explicadas)

| Clave (`Credinet:`) | Default | Qué hace |
|---|---|---|
| `Environment` | `sandbox` | Solo etiqueta para logs/diagnóstico |
| `SubscriptionKey` | `__SANDBOX__` | API key (header `Ocp-Apim-Subscription-Key`). `__SANDBOX__` = key de pruebas |
| `StoreId` | `""` | Identificador de tienda en Sistecrédito (vacío = no se envía) |
| `BaseUrl` | `.../pos/` | Endpoint base. `/pos/` sandbox, `/posprod/` producción |
| `OtpDestination` | `0` | Canal del OTP: **0 = SMS** (llega a los números Permoda), 1 = WhatsApp |
| `Frequency` | `30` | Frecuencia de cuotas: 30 mensual, 14 quincenal |
| `Source` | `"2"` | Canal = POS (fijo por manual) |
| `AuthMethod` | `1` | Método de autorización = OTP (fijo por manual) |
| `TimeoutSeconds` | `30` | Timeout total de cada request (incluye reintentos) |
| `CertificatePins` | `[]` | Pines de certificado (vacío = TLS estándar). Ver §7 |

Se leen en `Services/Credinet/ApiConfig.cs`. `appsettings.json` va **embebido** en el APK
(EmbeddedResource) porque en el arranque de MAUI el sistema de archivos aún no está disponible.

---

## 7. 🔒 Seguridad

| Capa | Estado | Detalle |
|---|---|---|
| **Base de datos local cifrada** | ✅ | SQLite con **SQLCipher**. La llave se genera al azar en el 1er arranque y se guarda en **SecureStorage** (Android Keystore). Cédulas y `creditId` quedan cifrados en disco. Ver `Services/Platform/SecureDb.cs` |
| **Key no se loguea** | ✅ | `HttpLoggingHandler` **redacta** `Ocp-Apim-Subscription-Key` y **enmascara la cédula** en los logs |
| **OTP nunca se persiste** | ✅ | El token es de un solo uso, no se guarda |
| **Reintentos de red (Polly)** | ✅ | Solo reintenta GET de lectura ante fallos transitorios; **nunca** `create`/`payCredit`/`getCreditToken` (evita doble crédito/pago/OTP). No afecta ventas exitosas |
| **Certificate pinning** | ⚙️ listo, **apagado** | Infra en `CertificatePinning.cs`. Se activa poniendo pines SPKI en `Credinet:CertificatePins`. **Sin pines = TLS estándar** (no rompe nada). Pedir el pin a Sistecrédito |

### Cómo activar el certificate pinning (cuando tengas el pin)
1. Pedí a Sistecrédito el **SPKI SHA-256 (base64)** del certificado de `api.credinet.co`.
2. Ponelo en `appsettings.json`:
   ```jsonc
   "CertificatePins": [ "AAAAAAAA...base64...=", "BBBB...backup...=" ]
   ```
3. Con eso, la app exige que el certificado del servidor coincida con algún pin. Vacío = desactivado.

---

## 8. 📦 De sandbox a producción — CHECKLIST

### A. Sistecrédito (Credinet)
- [ ] `SubscriptionKey` de producción
- [ ] `StoreId` real
- [ ] **IP pública** de los POS en la **whitelist** de Sistecrédito
- [ ] Confirmar canal OTP por defecto (SMS vs WhatsApp) para los clientes reales
- [ ] (Opcional) SPKI pin para certificate pinning

### B. HioPos / ICG
- [ ] Alta del módulo en CloudLicense (`packageName` + `apk_name`)
- [ ] Módulo asignado a cada POS
- [ ] Versión del APK registrada

### C. Este código
- [ ] `appsettings.json` → `Environment=production`, `BaseUrl=.../posprod/`, key + StoreId reales
- [ ] **APK firmado con keystore de PRODUCCIÓN** (hoy es debug)
- [ ] **Logo real de Permoda** (hoy placeholder) → `Resources/Images/SistecreditoLogo.cs`
- [ ] Probado en **terminal Android 13 real** (piloto)

### D. Compilar / firmar / instalar (firma ya configurada en el .csproj)

La firma de Release está lista en `SistecreditoTEF.Maui.csproj` — se activa **sola** si existe
el keystore. Las contraseñas van por **variables de entorno** (nunca en el repo).

```bat
:: 1) Crear el keystore (UNA sola vez; guardarlo con respaldo seguro)
keytool -genkeypair -v -keystore permoda-release.keystore -alias sistecredito ^
  -keyalg RSA -keysize 2048 -validity 10000
::    → dejarlo en src/SistecreditoTEF.Maui/  (o pasar la ruta con -p:PermodaKeystore=...)

:: 2) Definir las contraseñas como variables de entorno (las que elegiste en el paso 1)
setx PERMODA_KEYSTORE_PASS "＜clave-del-store＞"
setx PERMODA_KEY_PASS      "＜clave-del-alias＞"
::    (cerrar y reabrir la terminal para que tomen efecto)

:: 3) Compilar el APK firmado de producción
dotnet publish src/SistecreditoTEF.Maui -f net10.0-android -c Release

:: 4) Sacar SHA-1 / SHA-256 (para el email a ICG)
keytool -list -v -keystore permoda-release.keystore -alias sistecredito

:: 5) Instalar en una terminal conectada por adb
adb install -r <ruta-al-apk>-Signed.apk
```

⚠️ **El keystore es único e irremplazable:** si se pierde, no se puede volver a actualizar la app
en las terminales (Android exige que todas las versiones se firmen con el MISMO keystore).
Guardarlo con respaldo y las contraseñas en un gestor seguro. Está en `.gitignore` para no commitearlo.

---

## 9. ✅ Estado actual y pendientes

### Funciona y verificado
- Flujo Credinet completo (validar → simular → OTP → crear → pagar), probado con datos reales.
- Idempotencia local por `SaleId` (evita doble cobro si HioPos reintenta).
- Echo-back completo a HioPos (crédito y pago).
- BD local **cifrada** + **auditoría persistente** (verificado: archivo cifrado en disco).
- Reintentos Polly + pinning (infra).
- Cambio de ambiente por config (3 líneas).

### Pendiente / recomendado
| Prioridad | Ítem |
|---|---|
| 🔴 Bloqueante | Piloto en **terminal HioPos real** (el handshake con HioPos nunca se ejecutó de verdad) |
| 🔴 | APK firmado de producción + **logo real** |
| 🟡 | Tablas SQLite de `error_log`/`client_cache` (auditoría e idempotencia ya persisten) |
| 🟢 | **Alineación a 16 KB**: en emuladores/dispositivos Android 15+ en modo 16 KB aparece un aviso del sistema ("libraries not 16 KB aligned") por librerías nativas de .NET/AndroidX/SQLCipher. **NO afecta Android 13** (POS actuales) ni la funcionalidad. Se resuelve actualizando el workload de .NET Android + paquetes NuGet cuando publiquen los `.so` alineados. Solo relevante si se sube a Play o se migra a Android 15+ |

---

## 10. 🗂️ Estructura del código (dónde está cada cosa)

```
src/SistecreditoTEF.Maui/
├─ appsettings.json                 ← CONFIG (credenciales, ambiente) — §5
├─ MauiProgram.cs                   ← arranque + DI + pipeline HTTP (Polly, pinning, timeout)
├─ AppShell.xaml                    ← rutas de navegación (flyout desactivado)
├─ Platforms/Android/
│  ├─ MainActivity.cs               ← recibe los Intents de HioPos (11 acciones)
│  └─ BroadcastAuditLogger.cs       ← manda el broadcast de auditoría a HioPos
├─ Services/Hiopos/
│  ├─ HioposConstants.cs            ← apk_name, acciones, extras, capacidades ⭐
│  ├─ HioposIntentParser.cs         ← lee los extras del Intent TRANSACTION
│  ├─ HioposResultBuilder.cs        ← arma el setResult a HioPos
│  └─ ReceiptBuilder.cs             ← comprobantes XML
├─ Services/Credinet/
│  ├─ ApiConfig.cs                  ← lee appsettings (§5/§6)
│  ├─ CredinetApiClient.cs          ← cliente HTTP (los 7 endpoints)
│  ├─ CredinetRepository.cs         ← DTO→dominio + manejo de errores
│  ├─ AuthInterceptor.cs            ← agrega el header de la key
│  ├─ CredinetHttpPolicies.cs       ← Polly (reintentos seguros)
│  └─ CertificatePinning.cs         ← pinning (off por defecto)
├─ Services/Platform/
│  ├─ SecureDb.cs                   ← abre SQLite CIFRADO (SQLCipher + SecureStorage)
│  ├─ SqliteIdempotencyStore.cs     ← cache SaleId→creditId (idempotencia)
│  ├─ SqliteAuditLog.cs             ← auditoría persistente cifrada
│  └─ HttpLoggingHandler.cs         ← logs con key redactada
├─ UseCases/SistecreditoService.cs  ← lógica de negocio (orquesta todo)
├─ ViewModels/                      ← una VM por pantalla (MVVM)
├─ Views/                           ← las pantallas (XAML) + Controls/HeroCardView
└─ Resources/Styles/                ← sistema de diseño (Colors.xaml, Styles.xaml)
```

---

## 11. 🩺 Problemas resueltos (histórico útil para debugging)

| Síntoma | Causa raíz | Dónde |
|---|---|---|
| "Se queda cargando" al validar cédula | Bug de tipos genéricos en `ApiResult<T>.Map` (el `switch` nunca matcheaba) | `Common/Result.cs` |
| Navegación colgada | `ConfigureAwait(false)` antes de navegar/UI | `CapturaCedulaViewModel.cs` |
| `224 CustomerNotFound` con cédula válida | Cédula con separador (`.`) del teclado numérico | `CapturaCedulaViewModel.cs` (sanitiza) |
| `223 RequestValuesInvalid` al simular | `getCreditLimitClient` no devuelve la cédula → se enviaba `idDocument` vacío | `SistecreditoService.cs` (inyecta doc) |
| Crash al elegir plazo | `Picker` abría un diálogo Material y el tema no tenía `colorSurface` | `styles.xml` + chips en `SeleccionCuotasPage.xaml` |
| "Pagar" no hacía nada | Botón sin `NotifyCanExecuteChangedFor` → quedaba deshabilitado | `PagoViewModel.cs` |
| **OTP no llegaba** | Se forzaba `destination=1` (WhatsApp) y el número no tiene WhatsApp | **Ahora SMS por defecto** (`OtpDestination`) |
| BD cifrada "no se creaba" | `SecureStorage` tarda ~2s en el emulador (no colgado) | `SecureDb.cs` (timeout 3s) |

---

## 12. 📖 Glosario

| Término | Significado |
|---|---|
| **TEF** | Transferencia Electrónica de Fondos (categoría del módulo en HioPos) |
| **apk_name** | Nombre con el que ICG identifica el módulo en CloudLicense (`permoda`) |
| **SaleId** | UUID único de la venta en HioPos (se usa como `invoice` para idempotencia) |
| **creditId** | GUID del crédito en Credinet (se devuelve a HioPos como `AuthorizationId`) |
| **OTP** | Clave dinámica de 6 dígitos que Credinet manda al celular del cliente |
| **TEA** | Tasa Efectiva Anual (obligatorio mostrarla al cliente) |
| **AVAL** | Seguro obligatorio incluido en cada cuota |
| **SPKI** | Clave pública del certificado (lo que se "pinea" en certificate pinning) |
| **Idempotencia** | Que reintentar una operación no la duplique (por `SaleId`) |

---

*Documento de HU8-973. Ante dudas del contrato exacto de Credinet o HioPos, ver los manuales
originales: "MANUAL INTERFACES CREDINET PARA SISTEMAS POS" (M-SCL-03 v05) y "API de desarrollo de
un Módulo de Cobro Electrónico para HioPosCloud" (v3.5/3.6).*
