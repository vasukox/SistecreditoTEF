# 03 · Ambientes y configuración

> **Documento propietario de la configuración.** Todo valor de configuración del módulo se
> define aquí; el resto de la documentación enlaza a esta página en lugar de repetirlo.

Este documento responde tres preguntas que en una integración de pagos se confunden con
facilidad y cuestan dinero cuando se confunden:

1. **¿Qué ambientes existen?** (§1)
2. **¿Cómo sabe el módulo en qué ambiente está operando?** (§2 y §3)
3. **¿Cómo verifico, en una terminal concreta, contra qué ambiente está hablando?** (§6)

---

## 1. Inventario de ambientes

Hay **tres ejes de ambiente** independientes entre sí. Casi todos los errores de despliegue
vienen de asumir que son uno solo.

```
EJE A — Ambiente de la API de Credinet     PRUEBAS (sandbox)  |  PRODUCCIÓN
EJE B — Entorno de ejecución del módulo    Desarrollo | Terminal de pruebas | Terminal productiva
EJE C — Configuración de compilación       Debug  |  Release
```

Un ejemplo legítimo de combinación: un **APK Release firmado de producción** (eje C) instalado
en una **terminal de pruebas** (eje B) apuntando al **sandbox de Credinet** (eje A). Los tres
ejes se eligen por separado.

### 1.1 Eje A — Ambientes de la API de Credinet (Sistecrédito)

Sistecrédito expone **dos ambientes**, ambos sobre Azure API Management, con el mismo contrato
REST y los mismos endpoints. Solo cambian la URL base y las credenciales.

| Aspecto | Pruebas (*sandbox*) | Producción |
|---|---|---|
| `BaseUrl` | `https://api.credinet.co/pos/` | `https://api.credinet.co/posprod/` |
| Credencial (`Ocp-Apim-Subscription-Key`) | Clave **pública** publicada en el manual del proveedor | Clave privada que entrega Sistecrédito |
| `StoreId` | No exigido (puede ir vacío) | **Obligatorio** — el módulo lo valida |
| Datos | Clientes y créditos de prueba | Clientes reales · **dinero real** |
| Canal del OTP | WhatsApp | WhatsApp |
| Restricción de red | Abierto | **Whitelist de IP** de las terminales en Sistecrédito |
| Límite conocido | Rechaza solicitudes de crédito **desde $120.000** (limitación del ambiente de pruebas, no del contrato). Para probar, usar montos menores | Sin ese límite |
| Confirmación al cliente | Depende del ambiente de pruebas | Sistecrédito notifica al cliente |

> **Consecuencia operativa:** un crédito creado en sandbox **no existe** para Sistecrédito ni
> para el cliente. El peor modo de fallo del módulo es una terminal productiva operando contra
> sandbox con el cajero convencido de que las operaciones son reales; §3 describe la barrera
> que lo impide.

### 1.2 Eje B — Entornos de ejecución

| Entorno | Dónde corre | Ambiente Credinet | Origen de la configuración | Firma del APK |
|---|---|---|---|---|
| **Desarrollo** | Emulador Android o terminal de desarrollo conectada por `adb` | Pruebas | `appsettings.json` embebido + *User Secrets* | Depuración |
| **Terminal de pruebas / piloto** | Terminal HioPos real, sin operación comercial | Pruebas | `appsettings.json` embebido (sin parámetros de CloudLicense) | Producción |
| **Terminal productiva** | Terminal HioPos en tienda | **Producción** | **CloudLicense** (ICG) | Producción |

La diferencia entre las dos últimas **no está en el APK**: es el mismo artefacto. Lo que las
distingue es si ICG provisionó o no los parámetros de producción en CloudLicense para esa
terminal (§2).

### 1.3 Eje C — Configuraciones de compilación

| Configuración | Firma | Diagnóstico | Uso |
|---|---|---|---|
| `Debug` | Llave de depuración de Android | `AddUserSecrets` activo · logging a `Debug` | Desarrollo local |
| `Release` | **Keystore de producción, obligatorio** — el build falla si no puede firmar de verdad | Sin *User Secrets* · sin `debuggable` | Todo lo que se instala en una terminal |

El detalle del proceso de firma está en [`09-Despliegue-y-Operacion.md`](09-Despliegue-y-Operacion.md).

### 1.4 Lo que *no* es un ambiente

Estos tres son **modos de operación** del mismo APK en la misma terminal y el mismo ambiente.
Se documentan en [`06-Flujos-Funcionales.md`](06-Flujos-Funcionales.md); se listan acá porque
suelen describirse por error como "ambientes":

- **Venta a crédito** — la inicia HioPos con una factura abierta.
- **Entrada de caja (recaudo)** — la inicia HioPos sin factura abierta.
- **Abono autónomo** — lo inicia el cajero desde el ícono de la aplicación, fuera de HioPos.

---

## 2. Precedencia de la configuración

`ApiConfig.FromConfiguration` construye la configuración vigente con esta precedencia. **Gana
la primera fuente que traiga un valor no vacío**, clave por clave:

```
1. CloudLicense (ICG)        ← llega en el Intent INITIALIZE y se persiste en la terminal
2. appsettings.json          ← embebido en el APK (siempre trae la configuración de PRUEBAS)
3. User Secrets              ← solo en compilaciones Debug, en la máquina del desarrollador
4. Valor por defecto en código
```

Tres consecuencias que conviene tener presentes:

- **La credencial de producción no está dentro del APK distribuido.** Se provisiona en
  CloudLicense y se puede rotar sin recompilar ni redistribuir.
- **El `appsettings.json` embebido es, por definición, el de pruebas.** No existe una variante
  productiva del archivo: producción llega exclusivamente por CloudLicense.
- **La configuración se reconstruye en cada `INITIALIZE`.** Si ICG cambia un parámetro y HioPos
  reenvía el `INITIALIZE`, la configuración nueva toma efecto sin reiniciar el proceso.

### 2.1 Parámetros que se aceptan desde CloudLicense

ICG los provisiona por terminal y HioPos los entrega en el XML del extra `Parameters` del
`INITIALIZE`:

```xml
<Parameters>
  <Param Key="API_BASE_URL">https://api.credinet.co/posprod/</Param>
  <Param Key="SUBSCRIPTION_KEY">…</Param>
  <Param Key="STORE_ID">…</Param>
  <Param Key="STORE_NAME">…</Param>
  <Param Key="ENVIRONMENT">production</Param>
</Parameters>
```

Claves reconocidas: `API_BASE_URL`, `SUBSCRIPTION_KEY`, `STORE_ID`, `STORE_NAME`, `ENVIRONMENT`,
`OTP_DESTINATION`, `CERTIFICATE_PINS`, `FREQUENCY`, `SOURCE`, `AUTH_METHOD`, `TIMEOUT_SECONDS`,
`OTP_MAX_RESENDS`, `OTP_RESEND_COOLDOWN`, `OTP_VERIFY_COOLDOWN`, `OTP_MAX_VERIFY_ATTEMPTS`,
`ENABLE_SUNMI_NATIVE`.

El XML se interpreta de forma **agnóstica al namespace** y tolerante a la capitalización del
atributo `Key`. Si el extra llega pero **no se reconoce ningún parámetro**, la condición se
registra como error y se emite un evento de auditoría `CONFIG_ERROR` hacia el POS: no se
continúa en silencio con la configuración de pruebas.

---

## 3. Barrera de coherencia de ambiente

`ApiConfig.Validate()` se evalúa antes de cada transacción. Si la configuración es incoherente,
el módulo **rechaza la transacción** y devuelve al POS `TransactionResult=Failed` con el título
*"Configuración inválida"* y un mensaje que le indica al cajero avisar al área de sistemas.

| Regla | Se rechaza cuando |
|---|---|
| Credencial presente | Falta `SubscriptionKey` |
| URL presente y cifrada | Falta `BaseUrl`, o no empieza por `https://` |
| Producción con credencial real | `Environment=production` **y** se está usando la clave pública de pruebas |
| Producción contra el endpoint correcto | `Environment=production` **y** la URL apunta a `/pos/` en lugar de `/posprod/` |
| Producción con tienda identificada | `Environment=production` **y** `StoreId` vacío |

La validación estricta aplica **solo a producción**: el ambiente de pruebas opera con la clave
pública sin objeciones. Cada rechazo queda además en la auditoría como `CONFIG_ERROR`.

---

## 4. Catálogo de claves

Sección `Credinet:` de `appsettings.json`. La columna *CloudLicense* indica la clave equivalente
que ICG puede provisionar para sobreescribir el valor.

| Clave | CloudLicense | Valor en el APK (pruebas) | Qué hace |
|---|---|---|---|
| `Environment` | `ENVIRONMENT` | `sandbox` | `sandbox` \| `production`. Activa la validación estricta de §3 y etiqueta los registros |
| `SubscriptionKey` | `SUBSCRIPTION_KEY` | `__SANDBOX__` | Credencial del header `Ocp-Apim-Subscription-Key`. El marcador `__SANDBOX__` se resuelve a la clave pública de pruebas del manual |
| `BaseUrl` | `API_BASE_URL` | `https://api.credinet.co/pos/` | Endpoint base. Se le agrega `/` final si falta |
| `StoreId` | `STORE_ID` | *(vacío)* | Identificador de la tienda en Sistecrédito. Vacío = no se envía. Obligatorio en producción |
| `StoreName` | `STORE_NAME` | `Permoda` | Nombre de la tienda impreso en el comprobante |
| `OtpDestination` | `OTP_DESTINATION` | `1` | Canal del OTP: **1 = WhatsApp**, 0 = SMS |
| `Frequency` | `FREQUENCY` | `30` | Frecuencia de cuotas: 30 = mensual, 14 = quincenal |
| `Source` | `SOURCE` | `"2"` | Canal de originación = POS. Fijo por manual del proveedor |
| `AuthMethod` | `AUTH_METHOD` | `1` | Método de autorización = OTP. Fijo por manual del proveedor |
| `TimeoutSeconds` | `TIMEOUT_SECONDS` | `30` | Techo total de cada petición HTTP, reintentos incluidos |
| `OtpResendCooldownSeconds` | `OTP_RESEND_COOLDOWN` | `60` | Espera mínima entre reenvíos del OTP |
| `OtpMaxResends` | `OTP_MAX_RESENDS` | `3` | Reenvíos permitidos por transacción |
| `OtpVerifyCooldownSeconds` | `OTP_VERIFY_COOLDOWN` | `2` | Espera mínima entre intentos de verificación |
| `OtpMaxVerifyAttempts` | `OTP_MAX_VERIFY_ATTEMPTS` | `3` | Intentos de verificación por transacción |
| `CertificatePins` | `CERTIFICATE_PINS` | `[]` | Pines SPKI (SHA-256, base64). Vacío = validación TLS estándar. Ver [`07-Seguridad.md`](07-Seguridad.md) |

Sección `Printing:`

| Clave | CloudLicense | Valor en el APK | Qué hace |
|---|---|---|---|
| `EnableSunmiNative` | `ENABLE_SUNMI_NATIVE` | `false` | Habilita la impresión nativa Sunmi. Requiere el AIDL oficial del fabricante y validación en hardware |

> En CloudLicense los valores se entregan como texto plano. `CERTIFICATE_PINS` acepta varios
> pines en una sola cadena separados por coma o punto y coma.

### 4.1 Por qué el `appsettings.json` va embebido

Se empaqueta como `EmbeddedResource` y se lee con `Assembly.GetManifestResourceStream`. Durante
el arranque de MAUI en Android, el `IFileSystem` y el `AssetManager` todavía no están
disponibles y cualquier lectura por archivo falla. El recurso se busca **por sufijo**
(`*.appsettings.json`), no por nombre exacto.

---

## 5. Configuración por ambiente — matriz de referencia

| Clave | Desarrollo | Terminal de pruebas | Terminal productiva |
|---|---|---|---|
| `Environment` | `sandbox` | `sandbox` | `production` *(CloudLicense)* |
| `BaseUrl` | `/pos/` | `/pos/` | `/posprod/` *(CloudLicense)* |
| `SubscriptionKey` | `__SANDBOX__` o *User Secret* | `__SANDBOX__` | Clave real *(CloudLicense)* |
| `StoreId` | vacío | vacío | Obligatorio *(CloudLicense)* |
| `StoreName` | `Permoda` | `Permoda` | Nombre real de la tienda *(CloudLicense)* |
| `OtpDestination` | `1` (WhatsApp) | `1` (WhatsApp) | `1` (WhatsApp) |
| `CertificatePins` | vacío | vacío | Pines de Sistecrédito, cuando los entregue |
| Firma del APK | Depuración | Producción | Producción |

En desarrollo, la credencial real nunca va al repositorio:

```powershell
dotnet user-secrets set "Credinet:SubscriptionKey" "<clave>"
```

---

## 6. Cómo verificar contra qué ambiente opera una terminal

El módulo deja constancia del ambiente en cada `INITIALIZE` (es decir, en cada arranque del POS):

```
adb logcat -s MainActivity ApiConfigProvider
```

```
INITIALIZE completado. env=production, pinning=OFF, otp=WhatsApp.
Configuracion valida (env=production, pinning=OFF).
```

| Lo que ves | Qué significa |
|---|---|
| `env=sandbox` | La terminal opera contra el **ambiente de pruebas**. Las operaciones **no son reales** |
| `env=production` + `Configuracion valida` | Opera contra **producción** con credenciales coherentes |
| `CONFIGURACION INVALIDA: …` | La configuración de CloudLicense está incompleta o incoherente: el módulo rechazará las transacciones (§3) |
| `El INITIALIZE trajo Parameters pero no se reconocio ningun parametro` | ICG provisionó parámetros con un formato que el módulo no reconoce; se está operando con la configuración de pruebas embebida |
| `Configuracion recargada desde CloudLicense: …` | Llegaron parámetros nuevos y tomaron efecto |

Nunca se registran valores de credenciales: el `StoreId` aparece como `(definido)` / `(vacio)` y
la credencial solo como *cambiada / sin cambios*.

---

## 7. Advertencias operativas

1. **Un solo `packageName` para todos los ambientes.** No existe un APK "de sandbox" y otro "de
   producción" que puedan convivir en la misma terminal: instalar uno **reemplaza** al otro.
   Para volver al anterior hay que reinstalarlo. La forma de saber cuál está instalado es el
   registro de §6.
2. **Cambiar de ambiente no requiere recompilar.** Se hace desde CloudLicense. Recompilar para
   cambiar de ambiente significa que la terminal quedó sin parámetros provisionados.
3. **Desinstalar borra los datos locales** — base de idempotencia y auditoría cifrada. No
   hacerlo con una venta o un recaudo en curso.
4. **La whitelist de IP es por ambiente.** Una terminal que funciona en pruebas puede fallar en
   producción si su IP pública no está autorizada en Sistecrédito.
5. **El canal del OTP es WhatsApp en los dos ambientes**, confirmado por Sistecrédito. No
   cambiar el canal en el paso a producción.

---

## 8. Replicación del padrón entre cajas

El despliegue son ~512 tiendas con ~3 cajas cada una: del orden de **1.536 terminales**. Casi toda
la configuración de este módulo no se toca en la caja —baja de CloudLicense en el `INITIALIZE`
(§2)—, así que lo único que hoy se teclea terminal por terminal es el **PIN de administrador** y el
**alta de cada cajero con su clave**.

Eso es lo que la replicación copia, y nada más.

### 8.1 Alcance realista

| Escenario | Config. a mano | Por copia |
|---|---|---|
| Sin replicación | 1.536 | 0 |
| Con replicación | 512 | 1.024 |

Elimina las cajas 2 y 3 de cada tienda, **no la primera**: tiene que haber una caja configurada de
la cual copiar. Es una mejora de 3×, no de infinito. Conviene decirlo antes de que alguien lo venda
de otra forma.

### 8.2 La idea en una frase

La caja configurada abre una **ventana de 10 minutos** y muestra un **código de seis dígitos**. El
código no lleva la configuración: la **autoriza**. Los datos viajan por la red local; el código
solo demuestra que quien pide estaba parado frente a la caja que ya está montada.

```
caja nueva  →  se conecta
caja vieja  →  Saludo  { versión, reto, tienda, cajeros }
caja nueva  →  Prueba  { prueba }
caja vieja  →  Entrega { sobre cifrado }  |  { error, intentosRestantes }
```

El sobre es **lo último** que se manda, y solo después de que el otro lado demostró conocer el
código. Si viajara primero, cualquiera podría conectarse, guardarlo y probar el millón de códigos
en su casa, sin límite de intentos y sin que la tienda se entere. Con este orden, **quien cuenta
los intentos es quien tiene el secreto**.

### 8.3 Qué se copia y qué no

| Se copia | No se copia |
|---|---|
| Hash del PIN de administrador | Credencial de Credinet, URL, ambiente, `Source` |
| Padrón completo de cajeros, con sus hashes | Nombre de tienda, ids de medio de pago |
| | Cualquier identidad propia de la caja |

Nunca viajan claves en claro: en este módulo no existen, se derivan al crearlas
(`PasswordHasher`). El cajero entra en la caja nueva con la de siempre.

> **El sobre no debe crecer hacia la identidad de la caja.** Todo lo de la derecha lo asigna
> CloudLicense **por terminal**. Copiarlo sería pisar con un valor prestado algo que la central ya
> definió — y dos cajas con la misma identidad se descubren semanas después, cuando la conciliación
> no cuadra. Hay un test que lo verifica por reflexión (`El_sobre_no_lleva_identidad_de_la_caja…`).

### 8.4 Las piezas

| Pieza | Responsabilidad | Archivo |
|---|---|---|
| Sobre | Qué se copia, versión, validación | `Services/Pairing/CashierRosterEnvelope.cs` |
| Secreto | Código, derivación de llaves, cifrado | `Services/Pairing/PairingSecret.cs` |
| Protocolo | Los tres mensajes | `Services/Pairing/PairingProtocol.cs` |
| Servidor | Ventana, intentos, entrega | `Services/Pairing/PairingHost.cs` |
| Cliente | Nunca lanza: todo sale como valor | `Services/Pairing/PairingClient.cs` |
| Descubrimiento | Difusión UDP, IP propia | `Services/Pairing/PairingDiscovery.cs` |
| Exportar / importar | Escritura en la BD cifrada | `Services/Auth/SqliteAuthStore.cs` |
| Pantalla | Interfaz, y apagar el socket al salir | `Views/ReplicacionPage.xaml` |

Todo menos la pantalla usa únicamente sockets del BCL, así que vive en la capa de aplicación y
**entra en la suite**: el emparejamiento completo corre sobre `127.0.0.1` en pruebas unitarias, sin
ningún dispositivo.

### 8.5 Decisiones que no son de estilo

- **El `ReplicacionViewModel` es `transient`**, y el socket se apaga en `OnDisappearing`. Como
  singleton sobreviviría a salir de la pantalla y dejaría una caja ofreciendo el padrón de la
  tienda en la red todo el día, casi siempre sin nadie mirando.
- **El sobre se relee en cada entrega.** Entre la primera caja y la tercera el administrador pudo
  dar de alta otro cajero, y la tercera tiene que recibir el padrón de verdad.
- **Los aciertos no queman intentos; los fallos sí** (3). Un código sirve para varias cajas dentro
  de la ventana, para que quien instala genere uno y camine la tienda.
- **Dos llaves derivadas del código**, no una: la prueba viaja en claro, y no puede calcularse con
  la llave que cifra el padrón. Comparación en tiempo fijo.
- **El receptor confirma antes de guardar.** Primero muestra de qué tienda es y cuántos cajeros
  trae; recién después escribe. Guardar sin confirmar deja una caja operando con el padrón de otra
  tienda, y eso no se nota hasta que alguien no puede ingresar.
- **El padrón se escribe completo, no se mezcla.** Una caja que recibe configuración se está
  montando; arrastrar cajeros de una instalación anterior es como se cuelan usuarios que nadie dio
  de alta ahí.
- **La IP propia se muestra siempre**, no solo cuando el descubrimiento falla. La difusión puede
  estar bloqueada aunque las cajas se vean entre sí: que dos equipos se respondan el ping no dice
  nada sobre si el switch deja pasar *broadcast*.

### 8.6 El modelo de seguridad, y su límite

Puerto `47114` (emparejamiento) y `47115` (descubrimiento). Ventana de 10 minutos, solo con acción
humana, 3 intentos, reto-respuesta, PBKDF2 310k, AES-GCM, comparación en tiempo fijo.

> **Lo que esto NO protege:** quien esté capturando el tráfico de la tienda durante la ventana y
> vea un emparejamiento exitoso se lleva el reto y la prueba, y puede romper un código de seis
> dígitos por fuerza bruta **fuera de línea**. Se acepta a conciencia: el atacante tiene que estar
> dentro de la red de la tienda, capturando, justo en esos minutos, y lo que obtiene son los hashes
> del padrón de esa tienda — no una credencial de Credinet, que aquí no viaja.
>
> La palanca es una constante: `PairingSecret.CodeLength`. Ocho dígitos multiplican ese costo por
> cien y no cambian nada más del diseño.

### 8.7 Qué falta verificar en terminal

Las pruebas sobre loopback no dicen nada sobre la red de una tienda. Antes de repartir:

1. ¿Se encontraron solas, o hubo que teclear la IP? Determina si el instructivo puede confiar en el
   descubrimiento.
2. ¿Se puede entrar en la caja nueva con un usuario de la primera, sin volver a crearlo?
3. ¿Un código equivocado quema intentos y lo informa?
