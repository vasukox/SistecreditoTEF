# Documentación — Módulo TEF Sistecrédito para HioPosCloud

Documentación técnica y operativa del módulo de pago electrónico que integra **HioPosCloud**
(POS de ICG Software) con **Credinet** (Sistecrédito) en las terminales de Permoda / KOAJ.

Esta carpeta es la **fuente única de verdad documental** del módulo. Cada tema tiene un
documento propietario: si un dato aparece en dos lugares, el propietario manda.

---

## Mapa de la documentación

| # | Documento | Responde a | Audiencia |
|---|---|---|---|
| — | [`../README.md`](../README.md) | ¿Qué es esto y cómo lo compilo? | Todos |
| 01 | [`01-Vision-y-Alcance.md`](01-Vision-y-Alcance.md) | ¿Qué problema resuelve, qué incluye y qué no? | Negocio · Arquitectura |
| 02 | [`02-Arquitectura.md`](02-Arquitectura.md) | ¿Cómo está construido? Capas, patrones, decisiones | Desarrollo · Arquitectura |
| 03 | [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md) | **¿Qué ambientes existen y cómo se configura cada uno?** | Todos |
| 04 | [`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md) | Contrato con el POS de ICG y alta del módulo | Desarrollo · ICG |
| 05 | [`05-Integracion-Credinet.md`](05-Integracion-Credinet.md) | Contrato con la API de Sistecrédito | Desarrollo · Soporte |
| 06 | [`06-Flujos-Funcionales.md`](06-Flujos-Funcionales.md) | Qué ve y hace el cajero, paso a paso | Negocio · Soporte · QA |
| 07 | [`07-Seguridad.md`](07-Seguridad.md) | Controles de seguridad y datos personales | Seguridad · Auditoría |
| 08 | [`08-Calidad-y-Pruebas.md`](08-Calidad-y-Pruebas.md) | Qué se prueba, qué está verificado y qué no | QA · Arquitectura |
| 09 | [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md) | Cómo se firma, instala, configura y se soporta | Operaciones · Soporte |
| 10 | [`10-Glosario.md`](10-Glosario.md) | Vocabulario del dominio | Todos |
| — | [`historico/`](historico/README.md) | Informes de auditoría archivados (no vigentes) | Auditoría |

### Rutas de lectura sugeridas

- **Me acabo de sumar al proyecto:** `../README.md` → 01 → 02 → 06.
- **Voy a instalar o configurar una terminal:** 03 → 09.
- **Voy a hablar con ICG:** 04 § *Alta del módulo*.
- **Voy a atender un incidente en tienda:** 09 § *Diagnóstico* → 05 § *Códigos de error*.
- **Necesito evaluar riesgo o cumplimiento:** 07 → 08.

---

## Control documental

| Campo | Valor |
|---|---|
| Producto | Módulo TEF Sistecrédito para HioPosCloud |
| Propietario técnico | Aplicaciones Digitales — Permoda |
| Gobierno de arquitectura | Equipo de Arquitectura — DOPE |
| Trazabilidad de la iniciativa | Azure DevOps · **HU8-973** (referencia exigida por la política de desarrollo DOPE; no se replica en el resto de la documentación) |
| Última revisión integral | 2026-09-02 |
| Estado | Vigente |

---

## Convenciones de esta documentación

1. **Un dato, un dueño.** Los valores de configuración viven en `03`; los identificadores del
   contrato con ICG en `04`; los endpoints en `05`. Los demás documentos enlazan, no copian.
2. **Todo dato verificable indica cómo se verificó.** Si algo no se ejecutó sobre hardware o
   sobre el binario, se marca como *no verificado* en lugar de afirmarlo.
3. **Nada de referencias a tickets en el cuerpo.** La trazabilidad va en el control documental
   de esta página; los documentos describen el sistema, no su historia de cambios.
4. **Lo histórico se archiva, no se borra.** Los informes superados viven en
   [`historico/`](historico/README.md) con su fecha y una nota de qué documento vigente los
   reemplaza.
5. **Idioma:** español. Los identificadores técnicos (`SubscriptionKey`, `TRANSACTION`,
   `apk_name`) se dejan en su forma original porque son literales del contrato.

## Cómo mantenerla

| Si cambias… | Actualiza |
|---|---|
| `appsettings.json`, `ApiConfig` o los parámetros de CloudLicense | `03` |
| `HioposConstants` (apk_name, acciones, capacidades) o el resultado al POS | `04` |
| `ICredinetApi`, DTOs o el mapeo de errores | `05` |
| Una pantalla o el orden del flujo | `06` |
| Un control de seguridad, PII o la firma del APK | `07` y `09` |
| Capas, patrones o una decisión de diseño | `02` |
| El proceso de build, firma o instalación | `09` |

Cambios que afecten el stack tecnológico oficial o la arquitectura deben pasar por el
Equipo de Arquitectura — DOPE.
