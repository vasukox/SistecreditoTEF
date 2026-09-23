# 02 · Arquitectura

> Este documento explica **cómo está construido** el módulo. El *qué hace* está en
> [`06-Flujos-Funcionales.md`](06-Flujos-Funcionales.md); los contratos externos en `04` y `05`.

---

## 1. Estructura de la solución

```
SistecreditoTEF.sln
├─ src/SistecreditoTEF.Maui      Aplicación Android (único artefacto distribuible)
└─ tests/SistecreditoTEF.Tests   Suite xUnit sobre net10.0 (sin emulador)
```

**Un solo APK.** El módulo cubre los tres modos de operación (venta, recaudo desde el POS y
abono autónomo) en un mismo artefacto; el modo se resuelve en tiempo de ejecución según cómo se
abrió la aplicación. No hay un segundo APK de abonos.

La suite de pruebas compila la capa pura del proyecto de la aplicación mediante globs, lo que
permite ejecutarla en `net10.0` sin el workload de Android. Es funcional y es también la razón
por la que la mejora estructural pendiente es extraer un proyecto `Core` (ver §9).

---

## 2. Arquitectura macro

```
┌──────────────────────┐  Intent Android   ┌───────────────────────┐  HTTPS REST   ┌──────────────────┐
│    HioPosCloud       │ ────────────────▶ │   Módulo TEF          │ ────────────▶ │   Credinet API   │
│  (POS · Android)     │  11 acciones      │   (esta aplicación)   │ Ocp-Apim-Key  │  (Azure APIM)    │
│                      │ ◀──────────────── │                       │ ◀──────────── │                  │
└──────────────────────┘  setResult(…)     └───────────────────────┘     JSON      └──────────────────┘
          ▲                                           │
          │  Broadcast de auditoría                   │  Persistencia local cifrada
          │  icg.actions.externalApi.AUDIT            ▼
          └────────────────────────────────  SQLite + SQLCipher (idempotencia · auditoría · cajeros)
```

- **Frontera izquierda:** IPC de Android por Intents. Contrato de ICG, 11 acciones. Detalle en
  [`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md).
- **Frontera derecha:** HTTPS REST + JSON contra Azure API Management. Detalle en
  [`05-Integracion-Credinet.md`](05-Integracion-Credinet.md).
- **Lateral:** auditoría hacia el POS y persistencia local cifrada.

---

## 3. Capas internas

Arquitectura por capas con dependencias hacia adentro: la interfaz depende del dominio, el
dominio depende de contratos, nunca al revés.

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  PRESENTACIÓN (MVVM)                                                          │
│  Views/*.xaml  ──bindings──▶  ViewModels/*   ·   Navegación: Shell + AppRoutes │
│  Controles compartidos: ScaffoldView · HeroCardView · BackBarView             │
└──────────────────────────────────┬────────────────────────────────────────────┘
                                   │
┌──────────────────────────────────▼────────────────────────────────────────────┐
│  DOMINIO / CASOS DE USO                                                       │
│  UseCases/SistecreditoService   fachada: orquesta las operaciones de negocio, │
│                                 idempotencia y auditoría                      │
│  Services/Auth/AuthService      reglas de identificación del cajero (abonos)   │
│  Models/*                       registros inmutables del dominio               │
└──────────────────────────────────┬────────────────────────────────────────────┘
                                   │  depende de interfaces
┌──────────────────────────────────▼────────────────────────────────────────────┐
│  INTEGRACIONES Y PLATAFORMA (contratos + implementaciones)                     │
│  Services/Credinet   ICredinetRepository · ICredinetApi · ApiConfig · políticas │
│  Services/Hiopos     constantes de contrato · parser · constructores de salida  │
│  Services/Platform   base cifrada · idempotencia · auditoría · impresión ·      │
│                      navegación · registro HTTP · contexto de arranque          │
└──────────────────────────────────┬────────────────────────────────────────────┘
                                   │
┌──────────────────────────────────▼────────────────────────────────────────────┐
│  ANDROID                                                                       │
│  Platforms/Android   MainActivity (recibe los Intents) ·                        │
│                      AndroidTransactionResultHandler · BroadcastAuditLogger     │
└───────────────────────────────────────────────────────────────────────────────┘
```

**Regla de oro:** cada capa habla con la de abajo a través de interfaces
(`ICredinetRepository`, `ITokenStore`, `IIdempotencyStore`, `INavigationService`,
`ITransactionResultHandler`, `IAuditLogger`, `IReceiptPrinter`, `IDocumentReader`,
`ILaunchContext`, `IAuthStore`…). Eso permite probar el dominio con dobles y sustituir la
implementación de Android por una inerte fuera de la plataforma.

### 3.1 El mismo concepto en tres formas

| Forma | Ejemplo | Ubicación | Por qué existe |
|---|---|---|---|
| **DTO** | `ClientDto`, `CreditDto` | `Dtos/` | Espejo exacto del JSON de Credinet |
| **Dominio** | `Client`, `Credit` | `Models/` | Registros inmutables limpios, independientes del contrato externo |
| **Salida al POS** | `HioposResponse` | `Services/Hiopos/` | Estructura de extras que espera HioPos |

La conversión DTO → dominio ocurre solo en `Mappers/CredinetMapper`, invocada por el
repositorio. Un cambio de contrato de Credinet se absorbe en un único lugar.

---

## 4. Patrones aplicados

| Patrón | Dónde | Para qué |
|---|---|---|
| **MVVM** | `Views/` + `ViewModels/` | Estado y lógica de presentación fuera del XAML |
| **Result tipado** | `Common/Result.cs` → `ApiResult<T>` | Errores explícitos entre capas, sin excepciones; la interfaz decide con un `switch` exhaustivo |
| **Repository** | `ICredinetRepository` | Aísla al dominio del HTTP crudo; traduce excepciones y códigos de error a `ApiError` |
| **Facade** | `SistecreditoService` | Punto único de entrada al dominio, con idempotencia y auditoría transversales |
| **Capa anticorrupción** | `CredinetMapper`, `HioposIntentParser`, `HioposResultBuilder` | Traducen los contratos externos al modelo interno y de vuelta |
| **Strategy** | `CredinetHttpPolicies.Select` · cadena de `IReceiptPrinter` | Elige política de reintentos según la petición; elige impresora según el hardware |
| **Chain of Responsibility** | Pipeline de `DelegatingHandler` y cadena de impresoras | Cada eslabón agrega una preocupación sin acoplarse al resto |
| **Decorator** | `AuthInterceptor` | Autenticación transversal sin tocar cada llamada |
| **Jerarquías selladas** | `ApiError` (`Network`/`Http`/`Business`/`Local`) | Clasificación exhaustiva de fallos |
| **Inyección de dependencias** | `AppServicesRegistration` | Inversión de control y testeabilidad |
| **Clave de idempotencia** | `SistecreditoService` + `IIdempotencyStore` | Un reintento no duplica el crédito ni el cobro |
| **Guard clauses / degradación segura** | `MainActivity.HandleIntent`, `SecureDb` | Nunca dejar al POS colgado ni hacerlo crashear |

---

## 5. Pipeline HTTP

```
CredinetApiClient
   │  HttpRequestMessage (URL + query, o cuerpo JSON)
   ▼
[AuthInterceptor]      agrega Ocp-Apim-Subscription-Key
   ▼
[HttpLoggingHandler]   registra petición y respuesta con la credencial redactada y el documento enmascarado
   ▼
[Política Polly]       reintentos solo en lecturas idempotentes
   ▼
[HttpClientHandler]    GZip/Deflate · techo de 4 conexiones simultáneas · certificate pinning si hay pines
   ▼
   Azure APIM → Credinet
```

El techo de conexiones existe porque la consulta de plazos válidos dispara varias peticiones en
paralelo: sin límite, sobre red móvil, los handshakes TLS compiten entre sí y el conjunto resulta
más lento que reutilizando conexiones.

Los manejadores se registran **transitorios**, no como únicos: `HttpClientFactory` rota la cadena
periódicamente y reasignar el manejador interno de una instancia ya usada lanza excepción.

---

## 6. Inyección de dependencias y arranque

Todo el registro común vive en `AppServicesRegistration.AddSistecreditoSharedServices`.
`MauiProgram` solo agrega lo específico de la plataforma.

**Secuencia de `MauiProgram.CreateMauiApp`:**

1. `SQLitePCL.Batteries_V2.Init()` — inicializa el proveedor SQLCipher **antes** de cualquier
   uso de SQLite; si no, la base no quedaría cifrada.
2. `LoadAppSettingsFromAsset()` — carga la configuración embebida (ver
   [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md) §4.1).
3. En `Debug`: *User Secrets* y registro de diagnóstico.
4. `AddSistecreditoSharedServices()` — pipeline HTTP, almacenes, dominio, ViewModels y páginas.
5. Registro de los manejadores de Android (resultado al POS y auditoría por broadcast).
6. `AppLogger.Init(...)`.

**Ciclos de vida:**

| Ciclo | Componentes | Por qué |
|---|---|---|
| Único (*singleton*) | Pipeline HTTP, repositorio, almacenes (token, idempotencia, estado, auditoría, cajeros), navegación, constructores de salida, control de OTP, impresoras, `SistecreditoService`, contexto de arranque | Estado compartido o conexión cacheada |
| Transitorio | ViewModels, páginas, manejadores HTTP, `ApiConfig` | Instancia fresca por navegación; la configuración se relee para respetar la precedencia |

`ApiConfig` **no** es de instancia única: `ApiConfigProvider` lo reconstruye cuando llega el
`INITIALIZE`. Congelarlo en el arranque hacía que la precedencia "CloudLicense gana" solo se
cumpliera por casualidad de orden.

---

## 7. Persistencia local cifrada

`Services/Platform/SecureDb.cs` abre conexiones SQLite cifradas con SQLCipher:

- La **llave** se genera aleatoriamente (32 bytes) en el primer arranque y se guarda en
  `SecureStorage`, respaldado por el Android Keystore. Nunca está en el código ni en disco en
  claro.
- **Degradación segura:** si la base no se puede abrir con la llave, se elimina y se recrea
  vacía en lugar de fallar. Los datos locales son operativos, no la fuente de verdad.
- **Espera acotada:** `SecureStorage` puede demorar en terminales sin bloqueo de pantalla; hay
  un tiempo máximo de espera con generación de llave alternativa.
- **Purga:** el mantenimiento de las tablas corre en segundo plano al recibir el `INITIALIZE`,
  para no demorar el arranque del POS.

Qué se guarda cifrado: idempotencia de créditos y abonos, auditoría de operaciones y las
credenciales de los cajeros habilitados para abonos autónomos (contraseñas derivadas con PBKDF2,
nunca en claro).

---

## 8. Estado compartido entre pantallas

| Componente | Qué guarda | Por qué existe |
|---|---|---|
| `ITransactionStateStore` | Transacción activa, documento de venta, cliente validado, crédito creado, último pago | Es el contexto que sobrevive a la navegación del Shell |
| `IStandaloneModeTracker` | Modo autónomo y origen del recaudo | Determina cómo se cierra el flujo y quién imprime |
| `ILaunchContext` | Acción del Intent con que se abrió la aplicación | Es el único dato disponible **antes** de que el Shell construya su pantalla raíz |
| `ISesionCajero` | Quién está operando los abonos | Su nombre viaja a Credinet en la traza del recaudo |
| `OtpRequestThrottle` | Reenvíos e intentos de verificación del OTP | Vive fuera del ViewModel para que navegar atrás no reinicie los contadores |

`ILaunchContext` merece la explicación: el Shell navega a su pantalla raíz dentro de
`base.OnCreate`, mientras que el Intent de HioPos se procesa en `OnResume`. Preguntar por la
transacción activa desde la pantalla raíz respondía "no hay" incluso en plena venta, y el módulo
terminaba pidiendo la clave del cajero —que solo corresponde a los abonos— en medio de una
factura. El contexto de arranque se registra antes de `base.OnCreate` justamente para que la
raíz pueda decidir bien.

---

## 9. Decisiones de arquitectura

| # | Decisión | Motivo |
|---|---|---|
| 1 | **MAUI solo Android** | HioPosCloud opera sobre terminales Android; el resto de las plataformas solo agregaba ruido y advertencias |
| 2 | **Un único APK para los tres modos** | El modo se resuelve en ejecución; dos artefactos se desincronizaban y dejaban diferencias silenciosas entre ellos |
| 3 | **`HttpClient` explícito, sin generador de clientes** | Control total de tiempos de espera, cabeceras y construcción de la cadena de consulta |
| 4 | **`ApiResult<T>` en lugar de excepciones entre capas** | Errores exhaustivos y explícitos; la interfaz decide con un `switch`, no con `try/catch` disperso |
| 5 | **Idempotencia doble** (local por venta + `invoice` remoto) | Evitar el segundo crédito o el segundo cobro ante cualquier reintento |
| 6 | **Reintentos solo en lecturas seguras** | Nunca duplicar una escritura ni disparar un OTP extra |
| 7 | **SQLCipher para la base local** | Documentos e identificadores de crédito cifrados en disco, con la llave en el Keystore |
| 8 | **Configuración por precedencia, CloudLicense primero** | La credencial de producción no viaja dentro del artefacto y se rota sin recompilar |
| 9 | **Configuración embebida como recurso** | El sistema de archivos de Android no está disponible durante el arranque de MAUI |
| 10 | **AOT desactivado** | El arranque con JIT es suficiente para una aplicación de caja y la compilación guiada por perfil cuesta minutos en cada invalidación |
| 11 | **Firma de *Release* obligatoria y refirma forzada** | Un artefacto firmado en depuración es rechazable y su llave es pública. El build falla antes que degradarse en silencio |
| 12 | **Nunca dejar al POS esperando** | Cualquier excepción no controlada se traduce en una respuesta de cancelación al POS |
| 13 | **Dobles escritos a mano en las pruebas** | Pruebas explícitas y legibles, sin la indirección de una biblioteca de simulación |

**Mejora estructural pendiente:** extraer un proyecto `Core` en `net10.0` con la capa pura. Hoy
la suite la compila mediante globs, lo que funciona pero acopla el proyecto de pruebas a la
estructura de carpetas de la aplicación.

---

## 10. Mapa de carpetas

```
src/SistecreditoTEF.Maui/
├─ MauiProgram.cs                Arranque y registro específico de Android
├─ AppServicesRegistration.cs    Registro de dependencias común
├─ appsettings.json              Configuración embebida (ambiente de pruebas)
├─ AppShell.xaml(.cs)            Shell de navegación
├─ Common/                       ApiResult, ApiError, mensajes al cajero, dinero, PII, conversores
├─ Dtos/                         Espejo del JSON de Credinet
├─ Models/                       Dominio (registros inmutables)
├─ Enums/                        Tipo de documento, frecuencia de cuotas
├─ Mappers/                      DTO → dominio
├─ UseCases/                     SistecreditoService (fachada de dominio)
├─ Services/
│  ├─ Auth/                      Identificación de cajeros para abonos autónomos
│  ├─ Credinet/                  Cliente HTTP, repositorio, configuración, políticas, pinning
│  ├─ Hiopos/                    Constantes de contrato, parser, constructores de salida, comprobantes
│  └─ Platform/                  Base cifrada, idempotencia, auditoría, impresión, navegación, registro
├─ ViewModels/                   Un ViewModel por pantalla
├─ Views/                        Páginas XAML y controles compartidos
├─ Resources/                    Sistema de diseño, íconos, logotipo
└─ Platforms/Android/            MainActivity, resultado al POS, auditoría por broadcast

tests/SistecreditoTEF.Tests/     Suite xUnit
docs/                            Esta documentación
```
