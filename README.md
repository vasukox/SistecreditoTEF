# Módulo TEF Sistecrédito para HioPosCloud

> Módulo de cobro electrónico (**TEF**) que permite **pagar y financiar con Sistecrédito
> (Credinet)** desde las terminales **HioPosCloud** de las tiendas Permoda / KOAJ, y cobrar
> cuotas de créditos existentes.

| | |
|---|---|
| **Tipo** | Aplicación móvil **.NET MAUI** (Android) |
| **Plataforma** | `net10.0-android` · mínimo API 24 · objetivo API 36 · validado en Android 13 |
| **Package name** | `com.permoda.sistecreditotef` |
| **APK Name (ICG)** | `permoda` |
| **Versión** | `1.0.0` |
| **Backend** | API de Credinet (Sistecrédito) sobre Azure API Management, HTTPS REST |

📖 **La documentación completa está en [`docs/`](docs/README.md).** Este README es solo el punto
de entrada.

---

## Qué hace

Cuando el cajero elige *"Sistecredito"* como medio de pago, HioPos entrega la operación a este
módulo, que la conduce con el cliente, la resuelve contra Credinet y devuelve el resultado al POS
para que imprima y cierre la factura.

```
┌──────────────────────┐   Intent Android    ┌───────────────────────┐   HTTPS REST    ┌──────────────────┐
│     HioPosCloud      │ ──────────────────▶ │   Módulo TEF          │ ──────────────▶ │   Credinet API   │
│  POS de ICG          │  11 acciones        │   Sistecrédito        │  Azure APIM     │   Sistecrédito   │
│                      │ ◀────────────────── │                       │ ◀────────────── │                  │
│  · Arma la factura   │   setResult(…)      │  · Conduce el flujo   │      JSON       │  · Valida cupo   │
│  · Dispara el cobro  │                     │  · Orquesta negocio   │                 │  · Envía el OTP  │
│  · Imprime y cierra  │                     │  · Devuelve resultado │                 │  · Crea crédito  │
└──────────────────────┘                     └───────────────────────┘                 └──────────────────┘
```

Tres modos de operación en un solo APK: **venta a crédito**, **recaudo desde el POS** (entrada de
caja) y **abono autónomo** (desde el ícono, con identificación del cajero). Detalle en
[`docs/06-Flujos-Funcionales.md`](docs/06-Flujos-Funcionales.md).

---

## Ambientes

Hay **dos ambientes de Credinet** —pruebas y producción— y el módulo resuelve cuál usar por
configuración, sin recompilar:

| | Pruebas | Producción |
|---|---|---|
| Origen de la configuración | `appsettings.json` embebido en el APK | **CloudLicense** (ICG), por terminal |
| URL base | `https://api.credinet.co/pos/` | `https://api.credinet.co/posprod/` |
| Credencial | Clave pública del manual del proveedor | Clave real de Sistecrédito |

La credencial de producción **no viaja dentro del artefacto**. El módulo se niega a operar si su
configuración de producción es incoherente.

⚠️ **Todos los ambientes comparten el mismo `packageName`:** instalar el APK apuntado a un
ambiente reemplaza al otro en la terminal. Para saber contra qué ambiente opera una terminal, ver
[`docs/03-Ambientes-y-Configuracion.md`](docs/03-Ambientes-y-Configuracion.md) §6 — es el
documento completo de ambientes y configuración.

---

## Compilar y ejecutar

**Requisitos:** SDK de .NET 10 con el workload de Android
(`dotnet workload install maui-android`) y una terminal o emulador Android conectado por `adb`.

```powershell
dotnet build src/SistecreditoTEF.Maui -f net10.0-android            # Debug
dotnet build src/SistecreditoTEF.Maui -f net10.0-android -t:Run     # desplegar en el dispositivo
dotnet test  tests/SistecreditoTEF.Tests                            # pruebas
```

En desarrollo, la credencial real nunca va al repositorio:

```powershell
dotnet user-secrets set "Credinet:SubscriptionKey" "<clave>"
```

Generar el artefacto de producción **exige el keystore y sus contraseñas**: un `Release` que no
pueda firmar de verdad falla el build. Procedimiento completo en
[`docs/09-Despliegue-y-Operacion.md`](docs/09-Despliegue-y-Operacion.md).

---

## Estructura

```
SistecreditoTEF.Maui/
├─ src/SistecreditoTEF.Maui/     Aplicación Android (único artefacto distribuible)
│  ├─ appsettings.json           Configuración embebida (ambiente de pruebas)
│  ├─ MauiProgram.cs             Arranque y registro específico de plataforma
│  ├─ AppServicesRegistration.cs Registro de dependencias
│  ├─ Platforms/Android/         Recepción de los Intents del POS y auditoría
│  ├─ Services/Hiopos/           Contrato con HioPosCloud
│  ├─ Services/Credinet/         Cliente HTTP de Credinet
│  ├─ Services/Platform/         Base cifrada, idempotencia, auditoría, impresión
│  ├─ Services/Auth/             Identificación de cajeros (abonos autónomos)
│  ├─ UseCases/                  Lógica de negocio
│  ├─ ViewModels/ · Views/       MVVM
│  └─ Resources/                 Sistema de diseño, íconos, logotipo
├─ tests/SistecreditoTEF.Tests/  Suite xUnit
├─ docs/                         Documentación técnica y operativa
└─ SistecreditoTEF.sln
```

---

## Estado

| | |
|---|---|
| Suite de pruebas | **564 / 564 correctas** (2026-09-02) |
| Piloto en terminal HioPos real | Validado en hardware (2026-07-07) |
| Firma de producción | Verificada sobre el binario; obligatoria en `Release` |
| Dependencias vulnerables | Ninguna |

Pendientes externos antes de un despliegue amplio: alta y provisión por terminal en CloudLicense
(ICG), credencial y `StoreId` de producción y whitelist de IP (Sistecrédito). Detalle en
[`docs/09-Despliegue-y-Operacion.md`](docs/09-Despliegue-y-Operacion.md) §5.3.

---

## Documentación

| Documento | Contenido |
|---|---|
| [`docs/README.md`](docs/README.md) | Índice, rutas de lectura y control documental |
| [`docs/01-Vision-y-Alcance.md`](docs/01-Vision-y-Alcance.md) | Qué resuelve, qué incluye y qué no |
| [`docs/02-Arquitectura.md`](docs/02-Arquitectura.md) | Capas, patrones, dependencias, decisiones |
| [`docs/03-Ambientes-y-Configuracion.md`](docs/03-Ambientes-y-Configuracion.md) | **Ambientes y configuración** |
| [`docs/04-Integracion-HioPosCloud.md`](docs/04-Integracion-HioPosCloud.md) | Contrato con el POS y alta en ICG |
| [`docs/05-Integracion-Credinet.md`](docs/05-Integracion-Credinet.md) | Contrato con la API de Sistecrédito |
| [`docs/06-Flujos-Funcionales.md`](docs/06-Flujos-Funcionales.md) | Flujos de caja, paso a paso |
| [`docs/07-Seguridad.md`](docs/07-Seguridad.md) | Controles de seguridad y datos personales |
| [`docs/08-Calidad-y-Pruebas.md`](docs/08-Calidad-y-Pruebas.md) | Verificación, cobertura y brechas |
| [`docs/09-Despliegue-y-Operacion.md`](docs/09-Despliegue-y-Operacion.md) | Firma, instalación, configuración, diagnóstico |
| [`docs/10-Glosario.md`](docs/10-Glosario.md) | Vocabulario del dominio |

Manuales del proveedor (en la raíz del repositorio): *"Manual de interfaces Credinet para
sistemas POS"* y *"API de desarrollo de un Módulo de Cobro Electrónico para HioPosCloud"*.

---

*Cambios que afecten el stack tecnológico oficial o la arquitectura deben pasar por el Equipo de
Arquitectura — DOPE.*
