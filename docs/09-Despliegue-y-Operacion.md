# 09 · Despliegue y operación

> Procedimiento completo para generar, verificar, instalar, configurar y soportar el módulo en
> una terminal. Los valores por ambiente están en
> [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md).

---

## 1. Requisitos del entorno de compilación

| Requisito | Detalle |
|---|---|
| SDK | **.NET 10** con el workload de Android (`dotnet workload install maui-android`) |
| Herramientas Android | `adb`, `apksigner`, `keytool`, `aapt2` (del SDK de Android / JDK) |
| Keystore de producción | En la raíz de la solución, o indicado con `-p:PermodaKeystore=<ruta>` |
| Contraseñas de firma | Variables de entorno `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS` |
| Terminal o emulador | Android conectado por `adb` para instalar |

Compilación e instalación de desarrollo:

```powershell
dotnet build src/SistecreditoTEF.Maui -f net10.0-android            # Debug
dotnet build src/SistecreditoTEF.Maui -f net10.0-android -t:Run     # despliega en el dispositivo conectado
dotnet test  tests/SistecreditoTEF.Tests                            # pruebas
```

---

## 2. Generar el artefacto de producción

```powershell
# 1) Contraseñas de firma. Con keystore PKCS12 el MISMO valor va en las dos variables.
setx PERMODA_KEYSTORE_PASS "<clave>"
setx PERMODA_KEY_PASS      "<clave>"
#    setx no afecta la terminal actual: abrir una nueva.
#    En un pipeline: usar variables secretas del agente, no setx.

# 2) Verificación previa
dotnet test tests/SistecreditoTEF.Tests
dotnet list src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj package --include-transitive --vulnerable

# 3) Artefacto firmado
dotnet publish src/SistecreditoTEF.Maui/SistecreditoTEF.Maui.csproj -c Release -f net10.0-android
```

Qué garantiza el proyecto en `Release`:

- Si **falta el keystore**, el build falla con la ruta esperada y las alternativas.
- Si **faltan las contraseñas**, el build falla indicando qué variables definir.
- Si la firma está lista, lo informa en el registro: `[FIRMA] Release se firmara con …`.
- Se **elimina el artefacto firmado previo** para que la firma se aplique siempre, incluso si
  nada más cambió.
- Para un build local de prueba sin firma real: `-p:AllowUnsignedRelease=true`. Queda una
  advertencia explícita en el registro y **ese artefacto no se distribuye**.

### 2.1 Crear o rotar el keystore

```powershell
keytool -genkeypair -v -keystore permoda-release-v2.keystore -alias sistecredito `
  -keyalg RSA -keysize 2048 -validity 10000

# Huellas para el alta en ICG
keytool -list -v -keystore permoda-release-v2.keystore -alias sistecredito
```

> `keytool` usa por defecto la misma contraseña para el keystore y para la llave privada. PKCS12
> no admite contraseñas distintas.

**El keystore es único e irremplazable.** Android exige que todas las versiones de un paquete se
firmen con el mismo certificado: si se pierde, no se puede actualizar la aplicación en las
terminales instaladas. Guardarlo con respaldo, y las contraseñas en un gestor seguro. Está
excluido del control de versiones.

El certificado obsoleto se conserva como registro histórico de lo que alguna vez se distribuyó,
pero no se usa para compilar y conviene mantenerlo fuera del directorio de build.

---

## 3. Verificar el artefacto (obligatorio)

No basta con que el build diga que firmó: hay que comprobarlo sobre el binario.

```powershell
apksigner verify --print-certs <ruta-al-apk>
keytool -printcert -jarfile <ruta-al-apk>
```

| Criterio | Debe cumplirse |
|---|---|
| Verificación de firma | Esquemas v2 y v3 correctos |
| Propietario del certificado | El certificado de producción de Permoda. **Si dice `CN=Android Debug`, no distribuir** |
| Huella SHA-1 | Debe coincidir con la del keystore vigente dada de alta en ICG |

La huella vigente se obtiene del keystore (§2.1) y es la que se entrega a ICG
(ver [`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md) §5).

---

## 4. Instalar en la terminal

| Situación de la terminal | Procedimiento |
|---|---|
| Sin el módulo instalado | `adb install <apk>` |
| Con una versión firmada con **el mismo** certificado | `adb install -r <apk>` |
| Con una versión firmada con **otro** certificado | `adb uninstall com.permoda.sistecreditotef` y luego instalar. Android no permite cambiar la firma de un paquete existente |

> **Desinstalar borra los datos locales**: la base de idempotencia, la auditoría cifrada y las
> credenciales de los cajeros. No hacerlo con una venta o un recaudo en curso.

### 4.1 Gestión de versiones

| Número | Para qué | Regla |
|---|---|---|
| `versionCode` | Entero interno de Android | **Debe subir** en cada publicación que se instale sobre una versión previa. Android rechaza la instalación de una versión menor, y algunos gestores de dispositivos no aplican el reemplazo si el número no cambia |
| `versionName` | Versión visible | Libre |
| Versión de contrato (`GET_VERSION`) | Lo que compara HioPos | **Fija y coordinada con ICG.** Es independiente de las dos anteriores (ver [`04`](04-Integracion-HioPosCloud.md) §1.2) |

Confundir estos tres números tiene consecuencias visibles en caja: derivar la versión de
contrato del `versionName` hacía que el POS pidiera "actualizar el módulo" en cada arranque.

---

## 5. Configurar y confirmar en la terminal

### 5.1 Provisión en CloudLicense (lo hace ICG)

Parámetros a provisionar por terminal productiva:
`API_BASE_URL` (`/posprod/`), `SUBSCRIPTION_KEY`, `STORE_ID`, `STORE_NAME`,
`ENVIRONMENT=production` y, cuando estén disponibles, `CERTIFICATE_PINS`.

Formato y claves reconocidas: [`03-Ambientes-y-Configuracion.md`](03-Ambientes-y-Configuracion.md) §2.1.

Si algo falta o es incoherente, el módulo **rechaza la transacción** con un mensaje explícito al
cajero en lugar de operar contra el ambiente equivocado.

### 5.2 Lista de verificación en la terminal

1. **Ambiente:** el registro del `INITIALIZE` dice `env=production` y `Configuracion valida`
   (ver [`03`](03-Ambientes-y-Configuracion.md) §6).
2. **Lanzamiento:** HioPos abre el módulo al elegir Sistecrédito como medio de pago. Valida
   identificadores y huella de firma en ICG.
3. **Venta completa:** un ciclo de facturación de punta a punta, verificando que la factura
   cuadre y que el POS imprima.
4. **Entrada de caja:** un recaudo, verificando que el comprobante salga por la térmica y el
   movimiento quede registrado.
5. **Convivencia de modos:** el ícono de abonos accesible con la venta cerrada y bloqueado con
   una venta en curso.
6. **Nota de crédito:** un `REFUND` debe rechazarse con mensaje claro.

### 5.3 Requisitos externos antes del despliegue amplio

| Responsable | Pendiente |
|---|---|
| Sistecrédito | Credencial de producción y `StoreId` reales; **whitelist de las IP públicas** de las terminales; pines TLS (opcional) |
| ICG | Alta del módulo, asignación por terminal y provisión de los parámetros en CloudLicense |
| Permoda | Custodia del keystore y las contraseñas; distribución del artefacto a las terminales |

---

## 6. Diagnóstico

### 6.1 Registros útiles

```powershell
adb logcat -s MainActivity ApiConfigProvider MauiProgram SistecreditoService ReciboPagoViewModel IReceiptPrinter
```

| Etiqueta | Qué contiene |
|---|---|
| `MainActivity` | Acción recibida, descripción de los extras, ambiente resuelto, decisión venta/recaudo |
| `ApiConfigProvider` | Configuración vigente, recargas y problemas de coherencia |
| `SistecreditoService` | Operaciones de negocio, aciertos de idempotencia, auditoría |
| `ReciboPagoViewModel` | Cierre del recaudo, quién imprime, resultado devuelto al POS |
| `IReceiptPrinter` | Recorrido de la cadena de impresión |

Nunca aparecen credenciales ni datos personales completos.

### 6.2 Síntomas frecuentes

| Síntoma | Causa probable | Qué revisar |
|---|---|---|
| La venta se cierra **sin cobrar** y el POS no muestra el módulo | El `apk_name` no coincide con el alta en ICG, o hay otro módulo declarando las mismas acciones | [`04`](04-Integracion-HioPosCloud.md) §1.1. En la terminal solo puede haber uno |
| El POS pide "actualizar el módulo" en cada arranque | La versión de contrato no coincide con la registrada en ICG | [`04`](04-Integracion-HioPosCloud.md) §1.2 |
| *"Configuración inválida"* en pantalla | Parámetros de CloudLicense incompletos o incoherentes | Registro de `ApiConfigProvider`; [`03`](03-Ambientes-y-Configuracion.md) §3 |
| Las operaciones no aparecen en Sistecrédito | La terminal está operando contra el ambiente de pruebas | `env=` en el registro del `INITIALIZE` |
| *"No autorizado"* al consultar el cliente | Credencial inválida, o IP de la terminal fuera de la whitelist | Provisión en CloudLicense y whitelist en Sistecrédito |
| *"No encontramos un cliente con ese documento"* con cédula válida | Separadores en la cédula, o cliente inexistente en ese ambiente | El módulo ya sanea la entrada; verificar el ambiente |
| *"Sistecrédito no financia ese monto"* | No hay plan de crédito para ese valor | No es el cupo del cliente y el plazo no interviene ([`05`](05-Integracion-Credinet.md) §4.1). En pruebas, usar montos menores a $120.000 |
| *"Ese código ya fue usado"* | El código de autorización se consumió | Reenviar código |
| El abono no imprime | La térmica no está alcanzable | Registro de `IReceiptPrinter`; el cajero puede reintentar. El abono **ya quedó cobrado** |
| El módulo pide clave de cajero en medio de una factura | Regresión del contexto de arranque | No debería ocurrir; está cubierto por pruebas ([`02`](02-Arquitectura.md) §8) |
| Aviso del sistema sobre librerías no alineadas a 16 KB | Dependencias nativas sin binarios alineados | Sin efecto en las versiones de Android en operación ([`08`](08-Calidad-y-Pruebas.md) §5) |

### 6.3 Operación en duda

Si el módulo informa que la operación **quedó registrada** pero no pudo devolver el resultado al
POS: verificar en Sistecrédito antes de reintentar. Repetir a ciegas está protegido por la
idempotencia local, pero la confirmación evita una discrepancia en el cierre de caja.
