# 07 · Seguridad

> Controles vigentes del módulo. Los procedimientos de firma e instalación están en
> [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md); la evidencia de verificación,
> en [`08-Calidad-y-Pruebas.md`](08-Calidad-y-Pruebas.md).

---

## 1. Resumen de controles

| Dominio | Control | Estado |
|---|---|---|
| Identidad del artefacto | Firma obligatoria con el certificado de producción; el build falla si no puede firmar | Activo |
| Superficie de exposición | Cuatro permisos, todos justificados; una sola actividad expuesta | Activo |
| Credenciales | La credencial de producción no viaja dentro del artefacto | Activo |
| Transporte | HTTPS obligatorio; *certificate pinning* implementado | Activo / pinning a la espera de los pines |
| Ambiente | El módulo rechaza operar con configuración de producción incoherente | Activo |
| Datos personales | Enmascarado centralizado en registros, auditoría y comprobantes | Activo |
| Cifrado en reposo | Base local cifrada con SQLCipher, llave en el Android Keystore | Activo |
| Integridad del dinero | Aritmética en enteros y doble barrera de idempotencia | Activo |
| Anti-abuso | Límites de reenvío y verificación del código de autorización | Activo |
| Trazabilidad | Auditoría hacia el POS y en base local cifrada | Activo |
| Cadena de suministro | Análisis de dependencias vulnerables en cada verificación | Activo |

---

## 2. Identidad e integridad del artefacto

- **Firma obligatoria en `Release`.** Si falta el keystore o las contraseñas, el build **falla**.
  El diseño anterior degradaba en silencio a la llave de depuración, cuyo certificado es público:
  cualquiera podría firmar un paquete con el mismo identificador y suplantar al módulo.
- **Refirma forzada.** El paso de firma de .NET Android es incremental y no considera el keystore
  como entrada, así que rotar el certificado y recompilar podía conservar el binario firmado con
  el certificado anterior sin ninguna señal. En `Release` se elimina el artefacto firmado previo
  para que la firma se aplique siempre.
- **Verificación sobre el binario, no sobre la configuración.** El procedimiento obligatorio y la
  huella vigente están en [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md) §3.
- **Historial de certificados.** El certificado anterior se conserva como registro histórico de
  lo que alguna vez se distribuyó, pero **no se usa para compilar**. Guardarlo fuera del
  directorio de build evita confundirlo con el vigente.
- **Sin bandera de depuración** en los artefactos de `Release`.

---

## 3. Superficie de exposición

| Permiso | Para qué |
|---|---|
| `INTERNET` | Hablar con la API de Credinet |
| `ACCESS_NETWORK_STATE` | Conocer el estado de la conexión |
| `READ_EXTERNAL_STORAGE` (hasta API 32) | Leer el documento de venta cuando el POS lo entrega por ruta |
| `WRITE_EXTERNAL_STORAGE` (hasta API 29) | Compatibilidad con versiones antiguas |

Se declara además el uso **opcional** de USB Host para la impresora térmica: si la terminal no
tiene impresora, la aplicación igual instala y funciona. El permiso concreto sobre el dispositivo
se solicita en la primera impresión.

> **No se declara un filtro de dispositivo USB conectado.** En una terminal con la impresora
> permanentemente conectada, ese filtro haría que Android lance esta aplicación al detectar el
> dispositivo —incluido el arranque de la terminal—, interrumpiendo a HioPos.

**Una sola actividad expuesta**, la que recibe las acciones del contrato con el POS y el
lanzador. Toda excepción que escape de un manejador se traduce en una respuesta de cancelación:
el módulo no puede tumbar al POS.

---

## 4. Gestión de credenciales

| Credencial | Dónde vive | Protección |
|---|---|---|
| **Credencial de Credinet** | Provisionada por ICG en CloudLicense y persistida en la terminal | No viaja dentro del artefacto distribuido. Nunca se registra: el manejador de trazas la redacta. Se rota sin recompilar |
| **Token de sesión de HioPos** | Almacenamiento local de la aplicación | Se persiste al `INITIALIZE` y se limpia al `FINALIZE` |
| **Llave de la base local** | `SecureStorage`, respaldado por el Android Keystore | Generada aleatoriamente en el primer arranque; nunca en el código ni en disco en claro |
| **Claves de los cajeros** | Base local cifrada | Derivadas con PBKDF2, 210.000 iteraciones. Nunca en claro |
| **Contraseñas del keystore** | Variables de entorno del equipo o del agente de compilación | Nunca en el repositorio |

La clave pública del ambiente de pruebas está en el código a propósito: es pública por
definición, está publicada en el manual del proveedor y solo sirve contra el sandbox. La barrera
de §6 impide que una terminal productiva opere con ella.

---

## 5. Transporte

- **HTTPS obligatorio.** Una URL base sin `https://` se considera configuración inválida y
  bloquea la operación.
- **Certificate pinning implementado** sobre la validación del certificado del servidor. Se
  activa cargando los pines SPKI (SHA-256, base64); con la lista vacía opera con validación TLS
  estándar, que es el comportamiento actual.
- Los pines se pueden cargar **desde CloudLicense**, sin recompilar ni redistribuir. Antes solo
  se leían de la configuración embebida, lo que hacía imposible activarlos en campo.
- **Dependencia externa:** los pines de `api.credinet.co` los debe entregar Sistecrédito. Al
  pedirlos conviene solicitar también un pin de respaldo, para que la rotación del certificado
  del servidor no deje a las terminales sin servicio.

### 5.1 Reintentos, desde la óptica del riesgo

La política de reintentos es un control de seguridad además de uno de resiliencia: reintentar
una escritura duplicaría un crédito o un cobro, y reintentar la solicitud del código de
autorización enviaría mensajes de más al cliente. Solo se reintentan lecturas seguras
(ver [`05-Integracion-Credinet.md`](05-Integracion-Credinet.md) §3).

---

## 6. Control de ambiente

Impide el escenario más costoso de una integración de pagos: una terminal productiva operando
contra el ambiente de pruebas, con el cajero convencido de que las operaciones son reales.

Las reglas y el comportamiento ante cada incoherencia están en
[`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md) §3. Desde la perspectiva de
seguridad importan tres propiedades:

1. El rechazo es **ruidoso**: mensaje al cajero, resultado fallido al POS y evento de auditoría.
2. No hay **continuación silenciosa**: si llegan parámetros que no se reconocen, se registra como
   error y se audita.
3. La precedencia de configuración está **cubierta por pruebas**, nivel por nivel: es la regla de
   negocio más delicada del módulo y no depende de una API de plataforma imposible de verificar.

---

## 7. Protección de datos personales

Marco aplicable: **Ley 1581 de 2012** (habeas data, Colombia). Los datos personales que el módulo
maneja son la cédula, el nombre, el celular y el historial de crédito del cliente.

| Punto de exposición | Tratamiento |
|---|---|
| Registros de diagnóstico | Enmascarado centralizado; nunca la cédula completa |
| Registro de peticiones HTTP | Credencial redactada y documento enmascarado |
| Auditoría hacia el POS | Enmascarada: el broadcast sale del recinto de la aplicación |
| Auditoría local | Cifrada en reposo |
| Comprobante impreso | Incluye el mínimo necesario para identificar la operación |
| Código de autorización | **Nunca se persiste**: es de un solo uso |
| Base local | Cifrada; con purga programada al arranque del POS |

El enmascarado está en un único componente, de modo que un punto nuevo de salida no reinventa la
regla.

---

## 8. Cifrado en reposo

Ver [`02-Arquitectura.md`](02-Arquitectura.md) §7 para el mecanismo. Desde la perspectiva de
seguridad:

- Cédulas, identificadores de crédito, auditoría y credenciales de cajeros quedan **cifrados en
  disco**.
- Si la base no se puede abrir con la llave —llave perdida o archivo heredado sin cifrar— se
  elimina y se recrea vacía. Es una decisión consciente: los datos locales son operativos y la
  fuente de verdad es Credinet. El efecto de un incidente así es perder la ventana local de
  idempotencia y la auditoría local, no la información del negocio.

---

## 9. Integridad transaccional del dinero

| Control | Descripción |
|---|---|
| **Aritmética** | Los importes se manejan en enteros (centavos) en las conversiones hacia el POS y hacia Credinet, sin acumular error de punto flotante |
| **Idempotencia de créditos** | Barrera local por venta más el campo remoto de factura |
| **Idempotencia de abonos** | Mismo crédito y mismo monto dentro de una ventana de 30 minutos se trata como reintento |
| **Reintento fiel** | El reintento devuelve el crédito completo guardado; reconstruirlo con importes en cero produciría un comprobante financieramente falso |
| **Coherencia del importe** | Al POS se le devuelve lo efectivamente cobrado, no el importe solicitado |
| **Operaciones en duda** | Si la respuesta al POS no se puede construir, se informa que la operación **sí quedó registrada** y hay que verificarla en Sistecrédito, en lugar de cerrar en silencio |

---

## 10. Integridad del documento fiscal

Al enriquecer el documento de venta solo se tocan los medios de pago. Agregar líneas, datos de
empresa o totales haría que HioPosCloud interprete un documento nuevo y lo reenvíe a la DIAN
(doble envío). Los campos de texto se sanean antes de emitirse. Ver
[`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md) §4.5.

---

## 11. Cadena de suministro

- El análisis de dependencias vulnerables forma parte del procedimiento de verificación previo a
  generar el artefacto (ver [`09`](09-Despliegue-y-Operacion.md) §2).
- Las versiones de los paquetes con avisos conocidos se fijan **explícitamente** para que ganen
  sobre las resoluciones transitivas.
- Se recomienda incorporar la suite de pruebas y el análisis de vulnerabilidades como puertas de
  un pipeline de integración continua.

---

## 12. Elementos que dependen de terceros

Ninguno impide el despliegue; elevan la postura de seguridad o completan funcionalidad opcional.

| # | Elemento | De quién depende | Estado |
|---|---|---|---|
| 1 | Pines SPKI de `api.credinet.co` | Sistecrédito | Mecanismo implementado y probado; se activa desde CloudLicense sin recompilar |
| 2 | Campo de idempotencia en el registro de pagos | Sistecrédito | La barrera local cubre el reintento en la misma terminal; el campo remoto cubriría dos terminales simultáneas |
| 3 | AIDL oficial de impresión Sunmi | Sunmi / Permoda | Vía nativa opcional; hoy deshabilitada |
| 4 | Alta y provisión por terminal en CloudLicense | ICG | Requiere identificadores y huella de firma (ver [`04`](04-Integracion-HioPosCloud.md) §5) |
| 5 | Whitelist de las IP públicas de las terminales | Sistecrédito | Necesaria para operar en producción |

---

## 13. Higiene recomendada

| Acción | Motivo |
|---|---|
| Rotar periódicamente las contraseñas del keystore | Buena práctica; guardarlas en un gestor o como variables secretas del pipeline |
| Mantener el keystore obsoleto fuera del directorio de build | Conserva la trazabilidad sin riesgo de confundirlo con el vigente |
| Incorporar pruebas y análisis de vulnerabilidades como puertas de CI | Detecta regresiones antes de generar el artefacto |
| Subir el `versionCode` en cada publicación | Algunos gestores de dispositivos tratan el mismo `versionCode` como "ya instalado" y no aplican el reemplazo |
| Solicitar los pines TLS con respaldo | Evita que una rotación del certificado del servidor deje terminales fuera de servicio |
