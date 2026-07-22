# SistecreditoTEF.Maui

> Módulo de pago **TEF** (Transferencia Electrónica de Fondos) para **HioPosCloud** que
> permite **pagar y financiar con Sistecrédito (Credinet)** desde las terminales POS de
> las tiendas Permoda / KOAJ.

| | |
|---|---|
| **Tipo** | App móvil **.NET MAUI** (Android) |
| **Plataforma** | Android 13+ (min. API 24, `net10.0-android`) |
| **Package name** | `com.permoda.sistecreditotef` |
| **Módulo HioPos (`apk_name`)** | `permoda` |
| **Versión** | 1.0.0 |
| **Historia de Usuario** | **HU8‑973** (Azure DevOps · Equipo de Arquitectura — DOPE) |
| **Backend** | Credinet API (Sistecrédito, Azure APIM) vía HTTPS REST |

📖 **La documentación funcional y técnica completa está en [DOCUMENTACION.md](DOCUMENTACION.md).**
Este README es el punto de entrada rápido; ese documento explica el programa entero
paso a paso.

---

## ¿Qué hace?

Cuando un cajero elige **"Sistecredito"** como medio de pago en HioPos, esta app:

1. Recibe los datos de la venta desde HioPos (vía **Intent de Android**).
2. Guía el flujo en pantalla: **cédula → cupo → cuotas → OTP → crédito**.
3. Habla con **Credinet** (Sistecrédito) por HTTPS para validar al cliente, simular
   cuotas, enviar el **OTP** y crear el crédito (o registrar el pago de una cuota).
4. **Devuelve el resultado a HioPos** (aceptado/fallido + comprobantes) para que
   imprima el recibo y cierre la factura.

### Los 3 sistemas que se hablan

```
┌─────────────────────┐  Intent Android   ┌──────────────────────┐  HTTPS REST   ┌──────────────────┐
│    HioPosCloud       │ ───────────────▶ │   ESTA APP (.NET)     │ ───────────▶ │   Credinet API   │
│ (POS, Android 13)    │  icg.actions...  │  Módulo TEF Siste-    │ Ocp-Apim-Key  │  (Sistecrédito)  │
│  - Arma la factura   │ ◀─────────────── │  crédito              │ ◀─────────── │   Azure APIM     │
│  - Dispara el pago   │  setResult(...)  │  - UI del flujo       │   JSON        │  - Valida cliente│
│  - Imprime recibo    │                  │  - Llama a Credinet   │               │  - Crea crédito  │
│  - Cierra la venta   │                  │  - Devuelve resultado │               │  - Envía OTP     │
└─────────────────────┘                  └──────────────────────┘               └──────────────────┘
```

---

## Stack tecnológico

- **.NET 10 / C#** con **.NET MAUI** (`Microsoft.Maui.Controls`) — UI y ciclo de vida.
- **MVVM** con `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- **HttpClientFactory + Polly** (`Microsoft.Extensions.Http.Polly`) — cliente Credinet
  con reintentos seguros.
- **SQLite cifrado con SQLCipher** (`sqlite-net-pcl` + `SQLitePCLRaw.bundle_e_sqlcipher`)
  — cache local e idempotencia por `SaleId`, cifrado en disco (llave en Android Keystore).
- **Configuración** vía `appsettings.json` (embebido) + **User Secrets** en dev + parámetros
  de **CloudLicense** en producción (prioridad).

> **Nota de arquitectura (DOPE):** el almacenamiento local usa SQLite/SQLCipher por ser una
> app **on‑device** en la terminal POS (no hay servidor local donde correr SQL Server). No es
> una base de datos de backend. Cualquier persistencia de servidor debe usar SQL Server 2022
> según la política del stack oficial.

---

## Cómo compilar y ejecutar

**Requisitos:** SDK de **.NET 10** con el workload de Android (`dotnet workload install maui-android`),
un emulador o terminal Android conectada por `adb`.

```bash
# Restaurar y compilar (Debug)
dotnet build src/SistecreditoTEF.Maui -f net10.0-android

# Desplegar en el dispositivo/emulador conectado
dotnet build src/SistecreditoTEF.Maui -f net10.0-android -t:Run

# Pruebas
dotnet test tests/SistecreditoTEF.Tests
```

> En **sandbox** la app funciona sin credenciales reales: usa `SubscriptionKey="__SANDBOX__"`,
> la key pública de pruebas del manual de Credinet.

---

## Configuración de credenciales

Las credenciales de Credinet salen de **dos fuentes**, y **CloudLicense tiene prioridad**:

1. **CloudLicense (producción).** ICG entrega los parámetros en el `INITIALIZE`; la app los
   persiste y los usa por encima del `appsettings.json`. Así **la key no queda dentro del APK**.
2. **`appsettings.json` (fallback / sandbox / dev).** Ver
   [`src/SistecreditoTEF.Maui/appsettings.json`](src/SistecreditoTEF.Maui/appsettings.json).

Claves principales (sección `Credinet:`):

| Clave | Sandbox | Producción |
|---|---|---|
| `Environment` | `sandbox` | `production` |
| `BaseUrl` | `https://api.credinet.co/pos/` | `https://api.credinet.co/posprod/` |
| `SubscriptionKey` | `__SANDBOX__` | *(la key real de Sistecrédito)* |
| `StoreId` | `""` | *(el StoreId real de Permoda)* |
| `OtpDestination` | `1` (WhatsApp) | `1` (WhatsApp) |

> 🔐 **El OTP se entrega siempre por WhatsApp** a la línea del cliente (`OtpDestination=1`),
> confirmado por Sistecrédito para test y producción — no cambiar el canal en el go‑live.

> ⚠️ **Nunca commitees la key de producción.** En dev usá User Secrets:
> `dotnet user-secrets set "Credinet:SubscriptionKey" "<tu-key>"`.

Detalle completo de todas las claves en [DOCUMENTACION.md §5 y §6](DOCUMENTACION.md).

---

## Estructura del proyecto

```
SistecreditoTEF.Maui/
├─ src/SistecreditoTEF.Maui/          ← la app MAUI
│  ├─ appsettings.json                ← configuración (credenciales, ambiente)
│  ├─ MauiProgram.cs                  ← arranque + DI + pipeline HTTP (Polly, pinning)
│  ├─ Platforms/Android/              ← MainActivity (recibe Intents de HioPos) + auditoría
│  ├─ Services/Hiopos/                ← contrato con HioPos (acciones, extras, capacidades)
│  ├─ Services/Credinet/             ← cliente HTTP de Credinet (endpoints, auth, reintentos)
│  ├─ Services/Platform/              ← SQLite cifrado, idempotencia, auditoría, logging
│  ├─ UseCases/                       ← lógica de negocio (orquesta el flujo)
│  ├─ ViewModels/ · Views/            ← MVVM (una VM/pantalla) + XAML
│  └─ Resources/                      ← sistema de diseño, íconos, logo
├─ tests/SistecreditoTEF.Tests/       ← pruebas unitarias
├─ docs/                              ← Calidad-y-Seguridad (informe)
├─ DOCUMENTACION.md                   ← documentación completa del programa ⭐
└─ SistecreditoTEF.sln
```

---

## Seguridad

- **BD local cifrada** (SQLCipher; llave en Android Keystore vía SecureStorage) — cédulas y
  `creditId` cifrados en disco.
- **La API key nunca se loguea** — el `HttpLoggingHandler` la redacta y enmascara la cédula.
- **El OTP nunca se persiste** (uso único).
- **Reintentos seguros (Polly)** — solo en GET de lectura; **nunca** en `create`/`payCredit`/
  `getCreditToken`, para evitar doble crédito/pago/OTP.
- **Idempotencia por `SaleId`** — reintentar una venta no la duplica.
- **Certificate pinning** — infraestructura lista, **apagada por defecto** (se activa
  agregando pines SPKI en `Credinet:CertificatePins`).

Informe detallado en [docs/Calidad-y-Seguridad.pdf](docs/Calidad-y-Seguridad.pdf).

---

## Estado del proyecto

**Funciona y verificado:** flujo Credinet completo (validar → simular → OTP → crear → pagar),
idempotencia local, echo‑back completo a HioPos, BD cifrada + auditoría persistente, reintentos
Polly, cambio de ambiente por config. **Piloto en terminal HioPos real validado en hardware
(2026‑07‑07).**

**Pendiente / recomendado antes del rollout masivo:**
- APK firmado de producción con keystore de **producción** + **logo real de Permoda**.
- Whitelist de las IP públicas de los POS en Sistecrédito.
- Alta del módulo en CloudLicense para cada POS.

El **checklist completo de paso a producción** está en [DOCUMENTACION.md §8](DOCUMENTACION.md).

---

## Documentación relacionada

- **[DOCUMENTACION.md](DOCUMENTACION.md)** — guía completa: flujo funcional, contrato con
  HioPos/ICG, credenciales, seguridad, checklist de producción, estructura del código,
  histórico de problemas resueltos y glosario.
- Manuales originales del proveedor: *"MANUAL INTERFACES CREDINET PARA SISTEMAS POS"*
  (M‑SCL‑03 v05) y *"API de desarrollo de un Módulo de Cobro Electrónico para HioPosCloud"*
  (v3.5/3.6).

---

*Entregable de la HU8‑973 · Equipo de Arquitectura — DOPE. Para excepciones al stack oficial
o cambios de arquitectura, contactar al Equipo DOPE.*
