# Histórico documental

Documentos **superados**, conservados por trazabilidad de la auditoría. **No son la referencia
vigente**: describen el estado del módulo en la fecha de su emisión, y varios de sus hallazgos ya
fueron corregidos.

> Para el estado actual, usar siempre la documentación vigente en [`../README.md`](../README.md).

| Fecha | Documento | Qué contiene | Reemplazado por |
|---|---|---|---|
| 2026-07-24 | [`2026-07-24-Auditoria-QA.md`](2026-07-24-Auditoria-QA.md) | Auditoría de calidad y seguridad: hallazgos críticos, altos, medios y plan de remediación | [`../08-Calidad-y-Pruebas.md`](../08-Calidad-y-Pruebas.md) · [`../07-Seguridad.md`](../07-Seguridad.md) |
| 2026-07-26 | [`2026-07-26-Remediacion.md`](2026-07-26-Remediacion.md) | Remediación de los hallazgos, con el detalle de cada corrección y los defectos descubiertos al verificar | [`../02-Arquitectura.md`](../02-Arquitectura.md) · [`../07-Seguridad.md`](../07-Seguridad.md) |
| 2026-07-28 | [`2026-07-28-Certificacion-QA-y-Seguridad.md`](2026-07-28-Certificacion-QA-y-Seguridad.md) | Certificación posterior a la remediación, con la evidencia ejecutada | [`../08-Calidad-y-Pruebas.md`](../08-Calidad-y-Pruebas.md) |
| 2026-07-28 | `2026-07-28-Calidad-y-Seguridad.pdf` · `.html` | Exportación del informe anterior para distribución | — |

## Cómo leer estos documentos

1. **Se leen en orden cronológico.** El de auditoría plantea hallazgos que el de remediación
   corrige y el de certificación verifica. Leer uno solo, fuera de secuencia, da una imagen
   equivocada del estado del módulo.
2. **Los datos de identidad pueden estar desactualizados.** El identificador de paquete y los
   números de versión cambiaron después de su emisión; los valores vigentes están en
   [`../04-Integracion-HioPosCloud.md`](../04-Integracion-HioPosCloud.md).
3. **Los conteos de pruebas corresponden a su fecha.** El resultado vigente está en
   [`../08-Calidad-y-Pruebas.md`](../08-Calidad-y-Pruebas.md) §1.
4. **No se actualizan.** Un documento histórico que se edita deja de ser evidencia. Si algo de
   aquí sigue siendo cierto y relevante, su lugar es la documentación vigente.
