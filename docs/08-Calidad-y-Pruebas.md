# 08 · Calidad y pruebas

> Qué está verificado, cómo se verificó y qué no lo está. Los informes de auditoría anteriores,
> ya superados, se conservan en [`historico/`](historico/README.md).

---

## 1. Estado de verificación

| Verificación | Resultado | Fecha | Cómo se obtuvo |
|---|---|---|---|
| Suite de pruebas automatizadas | **564 / 564 correctas**, 0 fallos, 0 omitidas | 2026-09-02 | `dotnet test tests/SistecreditoTEF.Tests` |
| Resolución del grafo de dependencias | Verificada con el contenedor real y validación estricta | 2026-09-02 | Prueba automatizada dentro de la suite |
| Compilación `Debug` y `Release` | Sin errores | 2026-07-28 | `dotnet build` / `dotnet publish` |
| Dependencias vulnerables | Ninguna | 2026-07-28 | `dotnet list package --include-transitive --vulnerable` |
| Firma del artefacto | Certificado de producción, esquemas v2 y v3 | 2026-07-28 | `apksigner verify --print-certs` sobre el APK |
| Arquitecturas nativas | `arm64-v8a` + `x86_64` | 2026-07-28 | `aapt2 dump badging` |
| Permisos declarados | Cuatro, más uno interno de firma de AndroidX | 2026-07-28 | `aapt2 dump permissions` |
| Piloto en terminal HioPos real | Handshake con el POS validado en hardware | 2026-07-07 | Ejecución en terminal de tienda |

Las verificaciones sobre el binario se rehacen en cada publicación: forman parte del
procedimiento de [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md) §2 y §3.

---

## 2. Cómo ejecutar la verificación

```powershell
# Suite completa (net10.0: corre en Windows y en un agente de CI, sin emulador)
dotnet test tests/SistecreditoTEF.Tests

# Dependencias vulnerables
dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable

# Compilación del artefacto firmado (requiere las variables de entorno de firma)
dotnet publish src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj -c Release -f net10.0-android

# Verificación de la firma SOBRE EL BINARIO (obligatoria)
apksigner verify --print-certs <ruta-al-apk>
```

La suite tarda pocos segundos y no requiere emulador: es apta como puerta de un pipeline.

---

## 3. Enfoque de pruebas

`tests/SistecreditoTEF.Tests` — **xUnit** con **dobles escritos a mano**, sin biblioteca de
simulación. Es una decisión consciente: los dobles explícitos hacen evidente qué se está
sustituyendo, a costa de escribir un poco más.

La arquitectura hace testeable lo que importa: como el dominio depende de interfaces, se inyectan
dobles del repositorio de Credinet, la auditoría, el almacén de idempotencia o el estado, y se
verifican los caminos correcto y de fallo sin red ni Android.

### 3.1 Qué está cubierto

| Área | Cubre |
|---|---|
| **Dinero y formato** | Aritmética en centavos, conversión de importes, entrada con separador de miles, porcentajes, números del contrato con el POS |
| **Resultados y errores** | `ApiResult` y sus transformaciones, errores de negocio que llegan con HTTP 4xx |
| **Configuración y ambiente** | Precedencia nivel por nivel, validación de coherencia, interpretación del XML de CloudLicense (sin namespace, con namespace por defecto y con prefijo), diagnóstico cuando no se reconoce ningún parámetro |
| **Contrato con el POS** | Interpretación de extras, construcción de la respuesta, modificación del documento fiscal, comprobantes, saneamiento de campos fiscales, protección entre modos, versión de contrato, salida hacia el POS, diagnóstico de extras |
| **Credinet** | Construcción de las peticiones, inyección de la credencial, redacción en las trazas, reutilización del código de autorización |
| **Seguridad** | Enmascarado de datos personales, derivación de contraseñas, validación de pines de certificado |
| **Dominio** | Idempotencia de créditos y de abonos, nombre del cajero en el recaudo, auditoría |
| **Identificación de cajeros** | Reglas de configuración inicial, alta y verificación |
| **Presentación** | Plazos autorizados, montos de abono, bloqueo y desbloqueo del cobro, selección de cuotas |
| **Plataforma** | Lectura del documento de venta, contexto de arranque, generación ESC/POS, cadena de impresión de abonos |
| **Infraestructura** | Resolución completa del grafo de dependencias con validación estricta |

### 3.2 Brechas conocidas

| Brecha | Impacto | Mitigación actual |
|---|---|---|
| **Sin pruebas instrumentadas en Android** | La interacción real con el POS y con el hardware no está automatizada | Piloto en terminal real y lista de verificación manual en [`09`](09-Despliegue-y-Operacion.md) §5 |
| **Sin pruebas de extremo a extremo contra Credinet** | El contrato se verifica con dobles, no contra el servicio | Validación manual contra el ambiente de pruebas |
| **Impresión nativa Sunmi sin validar en hardware** | Vía opcional, deshabilitada por defecto | La térmica USB es el camino real y está verificada |
| **La capa pura se compila por globs** | El proyecto de pruebas queda acoplado a la estructura de carpetas | Mejora pendiente: extraer un proyecto `Core` (ver [`02`](02-Arquitectura.md) §9) |

---

## 4. Batería de pruebas manuales recomendada

A ejecutar en terminal, antes de un despliegue amplio.

**Dinero y redondeo**

1. Venta con importe con centavos: la factura del POS debe cuadrar exactamente.
2. Abono por el mínimo, por el saldo total y por un monto intermedio.
3. Abono cuyo mínimo excede el saldo restante: el cobro debe poder completarse.

**Idempotencia y consistencia**

4. Repetir la misma venta sin cerrar el POS: no debe crearse un segundo crédito.
5. Repetir el mismo abono, mismo crédito y monto, dentro de 30 minutos: no debe cobrarse dos veces.
6. Cortar la red justo después de crear el crédito: verificar el mensaje y el estado real en
   Sistecrédito.

**Configuración y ambiente**

7. Terminal con `ENVIRONMENT=production` y credencial de pruebas: la operación debe rechazarse.
8. Terminal sin parámetros de CloudLicense: debe quedar registrado `env=sandbox`.
9. Cambiar un parámetro en CloudLicense y reiniciar el POS: debe tomar efecto.

**Contrato con el POS**

10. Venta a crédito completa, con impresión y cierre de factura desde el POS.
11. Entrada de caja: el comprobante debe salir por la térmica y el movimiento quedar registrado.
12. Nota de crédito (`REFUND`): debe rechazarse con mensaje claro, sin abrir la lista de créditos.
13. Abrir el ícono de abonos con una venta de HioPos en curso: debe quedar bloqueado.
14. Abrirlo con la venta ya cerrada: debe funcionar.

**Código de autorización**

15. Reenviar el código hasta agotar el tope; iniciar una venta nueva: los contadores deben venir
    frescos.
16. Digitar un código ya usado: el mensaje debe indicar pedir uno nuevo.

**Impresión**

17. Abono con la térmica desconectada: el cajero debe ver el aviso y poder reintentar.

> En el ambiente de pruebas, usar montos **menores a $120.000**: el sandbox rechaza las
> solicitudes desde ese valor (ver [`03`](03-Ambientes-y-Configuracion.md) §1.1).

---

## 5. Deuda técnica registrada

| Prioridad | Ítem |
|---|---|
| Media | Extraer un proyecto `Core` en `net10.0` con la capa pura |
| Media | Validar la impresión nativa Sunmi en hardware, si se decide habilitarla |
| Baja | **Alineación de páginas de 16 KB**: Android 16 lo exigirá para las librerías nativas. Depende de que los mantenedores de las dependencias publiquen binarios alineados. Sin efecto en las versiones de Android en operación |
| Baja | Automatizar la batería manual de §4 en pruebas instrumentadas |
