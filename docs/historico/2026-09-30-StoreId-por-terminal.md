# StoreId por terminal — informe de trabajo

> **Para el siguiente developer.** Todo lo que sigue está verificado en hardware
> (terminal Sunmi C9H, Android 13, `B5AC009H02300343`) y con 861 pruebas en verde.
>
> Fecha: 30/09/2026 · Autor: sesión con opencode · Sustituye a la sesión anterior
> que quedó corta por límite de sesión.

---

## 1. El síntoma que se reportó

Un crédito hecho en la tienda de **Suba** apareció registrado en la plataforma de
Sistecrédito bajo la tienda **037**.

Eso solo puede significar una cosa: **el `StoreId` que salió en el `create` fue el de
la 037**, no el de Suba.

---

## 2. Causa raíz (confirmada)

`CloudConfigStore` guardaba los parámetros de CloudLicense en `Preferences` con el
nombre de clave **tal como lo mandaba ICG**, y los leía de vuelta con las constantes
**en mayúsculas** de `ICloudConfig`.

`Preferences` sobre Android es `SharedPreferences`, que es **sensible a mayúsculas**.

```
ICG manda:   <Param Key="Store_Id">…</Param>
se guardaba: cloudparam_Store_Id
se leía:     cloudparam_STORE_ID      ← nunca se encontraba
```

La consecuencia la producía `ApiConfig.FromConfiguration` (`ApiConfig.cs:166-167`):

```csharp
string? Value(string cloudKey, string settingsKey) =>
    Blank(cloud.Get(cloudKey)) ?? Blank(section[settingsKey]);
```

Como `cloud.Get("STORE_ID")` devolvía `null`, caía al archivo embebido. Y en
`appsettings.produccion.json` estaba el de la 037:

```json
"StoreId": "607af8e38c91f70001436058"   // 037 MULTIMARCA PUNTO CALLE 18
```

**El APK decía qué tienda era.** Una tienda de las 76 sin provisionar tomaba ese
valor y reportaba sus créditos a la 037.

### Por qué era invisible

Cuatro capas que se tapaban entre sí:

1. El log `Parametros Cloud guardados: 5 [API_BASE_URL, Store_Id, …]` **parece éxito**.
2. `ApiConfigProvider` solo logueaba el StoreId como `(definido)`, y únicamente
   dentro de `HasMeaningfulChange` — que en arranque en frío es `false`. **Nunca se
   registraba.**
3. `HandleInitialize` era `if (parameters != null)` **sin `else`**: un INITIALIZE sin
   `Parameters` no dejaba ni una línea.
4. `ApiConfig.Validate()` solo comprobaba `string.IsNullOrWhiteSpace(StoreId)`. Como
   el APK traía uno, **una caja operando como la tienda equivocada pasaba la
   validación**. La barrera existente detectaba sandbox-vs-producción y StoreId
   faltante, pero **no StoreId equivocado**.

---

## 3. Archivos modificados

### 3.1 Corrección de la normalización

#### `src/…/Services/Platform/CloudConfigParser.cs` — **MODIFICADO**

Agregué `NombreCanonico(string)`, que es **puro y testeable** (el archivo ya entraba al
proyecto de tests).

```csharp
public static string NombreCanonico(string key)
{
    if (string.IsNullOrWhiteSpace(key)) return key;
    return Canonicos.TryGetValue(Reducir(key), out var canonica)
        ? canonica
        : key.Trim().ToUpperInvariant();
}
```

**Detalle no obvio:** subir a mayúsculas **no alcanza**. `StoreId` queda `STOREID` y
sigue sin ser `STORE_ID`. Por eso `Reducir` también ignora guiones bajos y espacios:

```csharp
private static string Reducir(string key) =>
    key.Replace("_", "").Replace(" ", "").Trim().ToUpperInvariant();
```

Esto hace que `STORE_ID`, `Store_Id`, `StoreId`, `storeid` y `store id` sean **la misma
clave**. El mapa `Canonicos` cubre los 14 parámetros del contrato más 4 que solo se
leen por literal (`OTP_RESEND_COOLDOWN`, `OTP_VERIFY_COOLDOWN`,
`OTP_MAX_VERIFY_ATTEMPTS`, `ENABLE_SUNMI_NATIVE`) — estos últimos **no quedaban
normalizados** en el primer intento y los tests los detectaron.

**Por qué vive acá y no en el almacenamiento:** la regla que decide a nombre de qué
tienda se venden no puede depender de que cada extremo del store se acuerde de
normalizar. Y desde acá es pura y se prueba en xUnit.

#### `src/…/Services/Platform/CloudConfigStore.cs` — **MODIFICADO**

Tres cambios:

1. `Guardar` / `Leer` / `Borrar` pasan por `Clave()`, que delega en
   `CloudConfigParser.NombreCanonico`.
2. `BorrarClavesLegacy` en `SaveFromXml`: si el parámetro quedó guardado antes bajo
   otra capitalización, se borra la vieja. Si no, cuando ICG retire un parámetro la
   caja seguiría usando el valor viejo.
3. **Migración en lectura**: `Get` busca las variantes heredadas y **reescribe con el
   nombre canónico**, avisando por log.

```csharp
private static IEnumerable<string> VariantesHeredadas(string canonica)
{
    var sufijo = canonica[Prefix.Length..];
    var pegada   = sufijo.Replace("_", string.Empty);
    if (!pegada.Equals(sufijo, StringComparison.Ordinal)) yield return Prefix + pegada;
    var separada = sufijo.Replace("_", " ");
    if (!separada.Equals(sufijo, StringComparison.Ordinal)) yield return Prefix + separada;
}
```

> **Por qué no se enumeran las claves de Preferences:** no se puede. `IPreferences` no
> expone `Keys` (lo verifiqué: `CS1061 'IPreferences' no contiene una definición para
> 'Keys'`). Intenté leer el XML de `shared_prefs` por dentro y sería atar la clase a
> la plataforma justo en el punto que hay que poder probar. Las dos variantes de
> separador son dos accesos, no un recorrido.
>
> **Nota:** la migración es una red de seguridad, no el camino normal. En cuanto HioPos
> manda un INITIALIZE, `SaveFromXml` reescribe el parámetro con el nombre canónico.

### 3.2 Que el APK deje de decir qué tienda es

#### `src/…/appsettings.produccion.json` — **MODIFICADO**

```diff
- "StoreId": "607af8e38c91f70001436058",
+ "StoreId": "",
```

Con el valor vacío, la barrera que **ya existía** en `ApiConfig.Validate` pasa a
servir de algo. Verificado en hardware: una caja que dice `production` pero no recibe
`STORE_ID` **rechaza la transacción**.

> ⚠️ **Este archivo NO está en git** (`.gitignore:37` → `appsettings.*.json`, porque
> trae la `SubscriptionKey` real). **El cambio no está versionado y el APK de
> producción solo se puede construir en esta máquina.** Hay que resolverlo antes de
> producir en las 3 cajas — si alguien compila producción en otro equipo, usa el
> archivo viejo **con la 037 horneada** y el bug vuelve sin que nadie lo note.

#### `src/…/Services/Credinet/ApiConfig.cs` — **MODIFICADO**

Solo el mensaje de la barrera (línea ~144). Antes decía `"ENVIRONMENT=production sin
STORE_ID configurado."`, que no le dice nada a nadie. Ahora dice qué falta, dónde, y
qué pasa si se deja pasar:

> `ENVIRONMENT=production sin STORE_ID: HioPosCloud no entrego el parametro STORE_ID
> en el INITIALIZE de este terminal, y el APK no lo trae horneado a proposito. Esta
> caja no esta provisionada como tienda y NO puede operar: si se dejara pasar, los
> creditos se reportarian a nombre de la tienda que tenga configurada el modulo. ICG
> debe definir STORE_ID en CloudLicense para esta terminal.`

### 3.3 Mostrar la tienda en pantalla

Pedido explícito: que al configurar la caja y al iniciar el módulo se vea con qué
tienda se está operando.

#### `src/…/Common/TextoDeTienda.cs` — **NUEVO**

Formateo puro. Recibe texto plano, **no `ApiConfig`**, para poder probarlo sin montar
nada (y para no invertir la capa: `Common` no debe depender de `Services/Credinet`).

- `Linea(storeName, storeId)` → `"nombre  ·  storeId"`
- `Aviso(storeId, esProduccion)` → vacío si hay StoreId; si no, en producción dice
  *"ESTA CAJA NO ESTA PROVISIONADA COMO TIENDA… No cobre hasta que sistemas lo
  habilite."*; en sandbox *"Sin StoreId. Es normal en el ambiente de pruebas."*

> **El aviso vacío es deliberado:** permite enlazar el `Label` tal cual, sin lógica de
> "ocultar si vacío".
>
> **El StoreId se muestra COMPLETO, sin recortar.** Contra la hoja hay que comparar
> los 24 caracteres; un `607af8e…036058` no descarta nada. En el log **sí** va
> recortado (lo hace `ApiConfigProvider.Recortar`) porque logcat lo lee cualquiera con
> un cable USB. En pantalla lo ve el personal de la tienda.

#### `src/…/Services/Platform/TiendaEnOperacion.cs` — **NUEVO**

Singleton que resuelve la configuración vigente.

> **Por qué NO recibe `ApiConfig` por constructor:** `ApiConfig` es *transient* y lo
> reconstruye `ApiConfigProvider.Reload()` en cada INITIALIZE. Si lo tomara por
> constructor guardaría la del primer arranque y la pantalla mostraría el StoreId
> viejo **para siempre** — el mismo defecto un nivel más arriba. Leyendo
> `IApiConfigSource` en cada acceso, lo que se ve es lo que el módulo está usando.
>
> **Nunca lanza.** Describir la tienda no puede impedir que la caja abra: si la
> lectura falla, muestra `(no se pudo leer)` y sigue.

#### `src/…/ViewModels/ConfigurarAdminViewModel.cs` — **MODIFICADO**
#### `src/…/ViewModels/HomeViewModel.cs` — **MODIFICADO**

Agregué el parámetro `ITiendaEnOperacion tienda` al constructor primary y tres
propiedades: `Tienda`, `AvisoTienda`, `MostrarAvisoTienda`.

> Rompió `PuertaDeEntradaTests` (4 call sites). Los actualicé con un `TiendaStub`.

#### `src/…/Views/ConfigurarAdminPage.xaml` — **MODIFICADO** (20 líneas)
#### `src/…/Views/HomePage.xaml` — **MODIFICADO** (23 líneas)

Tarjeta arriba de todo. En la de configuración va **antes del formulario**: es lo
primero que el instalador tiene que comprobar.

### 3.4 Segundo bug encontrado: `appsettings.json` nunca se cargaba

Este **no estaba en el encargo**, lo encontré al validar en la caja.

```
W MauiProgram: appsettings.json no encontrado o no se pudo cargar: ObjectDisposed_StreamClosed
```

`AddJsonStream` **no lee el stream en el momento de la llamada** — guarda la referencia
y lo parsea **después**, cuando alguien resuelve `IConfiguration`. El `using` ya lo
había cerrado para entonces.

```csharp
// ANTES — el stream ya estaba cerrado cuando tocaba parsear
using var stream = assembly.GetManifestResourceStream(manifestName);
config.AddJsonStream(stream);
```

**El fallo era invisible en el peor sentido posible:** la excepción se comía en un
`catch` y la app **arrancaba igual, con el contenedor de configuración vacío**. Como
`Environment` queda en `"sandbox"` por defecto:

- una caja de **producción operaba en modo pruebas**
- y **la barrera de coherencia no se activaba**

Es decir: un error de arranque dejaba al módulo sin protección y sin aviso, en un POS
que cobra plata real.

#### `src/…/AppServicesRegistration.cs` — **MODIFICADO**

Copiar a memoria antes de entregar el stream:

```csharp
byte[] contenido;
using (var stream = assembly.GetManifestResourceStream(manifestName))
{
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    contenido = buffer.ToArray();
}
config.AddJsonStream(new MemoryStream(contenido));
```

Además subí los tres `Log.Warn` a `Log.Error` y les puse el texto del síntoma, porque
un `Warn` con un mensaje genérico es exactamente lo que permitió que esto pasara
desapercibido.

> **Por qué importa ahora más que antes:** con el `StoreId` de producción vacío, esta
> corrección es **lo que sostiene la barrera**. Si `appsettings.json` no carga,
> `Environment` queda en `sandbox` y la validación de producción no corre — es decir,
> una caja no provisionada volvería a operar sin quejarse.

---

## 4. Archivos de prueba

| Archivo | Qué fija |
|---|---|
| `tests/…/Services/Platform/CloudConfigParserTests.cs` **MODIFICADO** | +113 líneas: variantes de capitalización y separador, el round-trip guardar→leer, que las 14 claves del contrato ya son canónicas, y que **CloudLicense le gane al appsettings** (el caso de Suba) |
| `tests/…/Services/ApiConfigTests.cs` **MODIFICADO** | +69 líneas: que `appsettings.produccion.json` **no** tenga StoreId horneado, y que el mensaje de la barrera mencione `STORE_ID`, `INITIALIZE` y `CloudLicense` |
| `tests/…/Common/TextoDeTiendaTests.cs` **NUEVO** | 8 pruebas del texto en pantalla: StoreId completo, sin recortar; nombre ausente explícito; aviso solo cuando corresponde |
| `tests/…/Services/CargaDeAppsettingsTests.cs` **NUEVO** | Reproduce el patrón del stream cerrado (falla) y el de copia a memoria (funciona) |
| `tests/…/ViewModels/PuertaDeEntradaTests.cs` **MODIFICADO** | `TiendaStub` para el constructor nuevo |
| `tests/…/SistecreditoTEF.Tests.csproj` **MODIFICADO** | Agrega `Services/Platform/TiendaEnOperacion.cs` a la compilación de tests |

**Total: 861/861 en verde** (partía de 821).

> Verifiqué que el test de `appsettings.produccion.json` **realmente falla** si se
> devuelve el StoreId horneado: lo puse a propósito, corrí el test, falló, lo restauré.
> Un test que nunca se ha visto fallar no es un test.

---

## 5. Cómo se verificó en hardware

Terminal: **Sunmi C9H, Android 13**, `B5AC009H02300343`, con la app de soporte remoto
`com.goto.resolve.customer` presente (salta al frente y estorba para capturar).

### 5.1 El bug de capitalización, reproducido

Simulé un INITIALIZE de HioPosCloud mandando la clave **como la escribe un humano en la
hoja de ICG**:

```xml
<Configuration><Parameters>
  <Param Key="Store_Id">607af8e38c91f70001436058</Param>
  <Param Key="STORE_NAME">037 MULTIMARCA PUNTO CALLE 18</Param>
  <Param Key="ENVIRONMENT">production</Param>
  <Param Key="API_BASE_URL">https://api.credinet.co/posprod/</Param>
</Parameters></Configuration>
```

Log resultante:

```
CloudConfigStore: Parametros Cloud guardados: 4 [Store_Id, STORE_NAME, ENVIRONMENT, API_BASE_URL]
ApiConfigProvider: TIENDA: storeId=607af8…436058 origen=CloudLicense, nombre='037 MULTIMARCA PUNTO CALLE 18'
```

**Con el código viejo esa caja habría seguido con el StoreId horneado de la 037**, y el
log habría dicho "4 parámetros guardados" sin que nada se viera raro.

### 5.2 La persistencia

Un segundo INITIALIZE **sin `STORE_ID`** (solo `STORE_NAME` y `ENVIRONMENT`): el
StoreId **follows igual**, `origen=CloudLicense`. La caja no depende de que ICG lo
reenvíe.

### 5.3 La barrera

INITIALIZE con `ENVIRONMENT=production` y **sin** `STORE_ID`:

```
CONFIGURACION INVALIDA: ENVIRONMENT=production sin STORE_ID: HioPosCloud no entrego el
parametro STORE_ID en el INITIALIZE de este terminal, y el APK no lo trae horneado a
proposito. Esta caja no esta provisionada como tienda y NO puede operar: si se dejara
pasar, los creditos se reportarian a nombre de la tienda que tenga configurada el
modulo. ICG debe definir STORE_ID en CloudLicense para esta terminal.
```

### 5.4 La pantalla

`Configuración inicial`, arriba del todo:

> **ESTA CAJA VA A OPERAR COMO**
> **037 MULTIMARCA PUNTO CALLE 18 · 607af8e38c91f70001436058**

Y sin provisionar, en sandbox:

> **ESTA CAJA VA A OPERAR COMO**
> Permoda · (sin StoreId)
> *Sin StoreId. Es normal en el ambiente de pruebas.*

---

## 6. Hallazgos que NO son código nuestro (para que nadie los investigue de más)

### 6.1 En esta caja, HioPosCloud no le habla a Sistecrédito

Al reiniciar HioPosCloud y observar qué manda a quién:

```
icg.actions.electronicpayment.permoglobal.INITIALIZE        → com.permoda.tefogloba/…
icg.actions.electronicpayment.permoglobal.GET_VERSION       → com.permoda.tefogloba/…
icg.actions.electronicpayment.permoglobal.GET_BEHAVIOR      → com.permoda.tefogloba/…
icg.actions.electronicpayment.permoglobal.GET_CUSTOM_PARAMS → com.permoda.tefogloba/…

Sistecreditotef: NINGÚN intent recibido.
```

Es un tema **separado y ajeno a este módulo** (Oglobal es otro cliente del mismo
distribuidor). Pero es relevante para una pregunta concreta: **en esta terminal no se
puede ver el StoreId real de CloudLicense, porque nadie se lo entrega a este módulo.**

El `StoreId` real de la 037 sale de la hoja `STOREID-SISTECREDITO` del Excel
`CREDENCIALES Y PARAMETROS TEF-PERMODA (1).xlsx` (hoja `STOREID-SISTECREDITO`, 76
tiendas). Para Suba:

| Tienda | Nombre | StoreId |
|---|---|---|
| 037 | MULTIMARCA PUNTO CALLE 18 | `607af8e38c91f70001436058` |
| 197 | TIENDA KOAJ SUBA GAITANA | `607d8d208c91f70001439630` |
| 530 | TIENDA KOAJ SUBA RINCON | `607d9eef8475180001b167a6` |

> Para leer ese Excel: las hojas están indexadas por **rId**, no por número. El
> `<sheet name="STOREID-SISTECREDITO" r:id="rId6"/>` corresponde a **`sheet6.xml`**, no
> a `sheet2.xml` (que es `PARAMETROS-ADDI`). Es fácil leer la hoja equivocada.

### 6.2 Cómo lanzar la app desde adb (perdí tiempo en esto)

`adb shell am start -n com.permoda.sistecreditotef/...MainActivity` **sin `-a`** manda un
Intent **sin action**, y `MainActivity` lo toma como *"acción no soportada"* →
`BuildCanceled` → **`Finish()`**. La app se abre y se cierra sola, y parece un cuelgue.

Hay que lanzarla como el usuario, por el ícono:

```bash
adb shell monkey -p com.permoda.sistecreditotef -c android.intent.category.LAUNCHER 1
```

> **No es un bug del módulo**, es el contrato de HioPos hablando. Pero si alguna vez
> "la app no abre" en una caja, descartar esto antes de buscar un cuelgue.

Además: `uiautomator` tarda ~10 s en arrancar su propio proceso en esta terminal, y
para entonces el launcher ya tomó el foco. La captura reliable es `screencap` en un
bucle corto desde el dispositivo.

### 6.3 Dos displays

`mDisplayId=0` (1920×1080, principal) y `mDisplayId=2` (1280×800, `Pantalla HDMI` = la
del cliente). El módulo corre en el 0.

---

## 7. Lo que falta antes de producción

| # | Qué | Por qué importa |
|---|---|---|
| 1 | **Resolver el versionado de `appsettings.produccion.json`** | Está en `.gitignore`. El `StoreId: ""` **no está en git** y el APK de producción solo se construye en esta máquina. Si se compila en otro equipo, sale el archivo viejo **con la 037**. Ver 3.2. |
| 2 | **Confirmar que las 3 cajas están provisionadas** en CloudLicense con su `STORE_ID` | Si alguna no lo está, **va a empezar a fallar**. Antes "funcionaba" (como 037). Con el cambio, la caja dice exactamente qué falta. Hay que tener el StoreId de las 3 a mano para el cotejo en pantalla. |
| 3 | **Compilar producción** y llevar a las 3 cajas | Las 3 siguen con el APK viejo. |
| 4 | Reiniciar HioPosCloud después de instalar | Para que reenvíe el `INITIALIZE`. |
| 5 | **Cerrar HioPos antes de abrir** | Sino la app queda detrás del POS. |

**Comando de build de producción:**

```powershell
dotnet build src\SistecreditoTEF.Maui\SistecreditoTEF.Maui.csproj `
  -c Release -f net10.0-android -t:SignAndroidPackage -p:Ambiente=produccion
```

Requiere `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS` en el entorno; sin ellas el
target `ValidarFirmaDeRelease` **falla el build** a propósito (para que no salga un
APK firmado con la llave de depuración, que es pública y rechazable por ICG).

---

## 8. Cómo verificar, en 10 segundos, en cualquier caja

```bash
adb logcat -c
# reiniciar HioPosCloud
adb logcat -v threadtime | grep -E "ApiConfigProvider|CloudConfigStore"
```

Buscar esta línea:

```
TIENDA: storeId=607af8…436058 origen=CloudLicense, nombre='037 MULTIMARCA PUNTO CALLE 18', env=production
```

- `origen=CloudLicense` → bien, la caja tomó el StoreId de HioPos.
- `origen=APK (CloudLicense NO mando STORE_ID)` → **la caja no está provisionada**.
  Los créditos se van a registrar a nombre de la tienda del APK.
- `storeId=(vacio)` → no hay tienda. En producción eso **rechaza la venta**.

Y en la pantalla, la tarjeta **ESTA CAJA VA A OPERAR COMO** dice lo mismo, con el
StoreId completo para cotejarlo contra la hoja.

---

## 9. Nota sobre el estado del working tree

Había cambios previos de la sesión anterior sin commitear (pantalla del cliente,
replicación entre cajas, etc.). **No los toqué ni los commiteé** — este trabajo vive
junto a ellos en el mismo working tree, así que conviene separarlos antes de commitear:

```
Archivos míos (12 modificados + 3 nuevos en src, 6 en tests)
Archivos de la sesión anterior (App.xaml.cs, MauiProgram.cs, MainActivity.cs,
  ApiConfigProvider.cs, AdminCajeros*, Pago*, ReciboPago*, Replicacion*,
  ActiveCredit.cs, Services/PantallaCliente/, PantallaClienteAndroid.cs, ...)
```

> `ApiConfigProvider.cs` (11:32) y `MainActivity.cs` (11:31) son de la sesión
> **anterior** — ya traían el `DescribirTienda` y el `else` del INITIALIZE. Yo solo
> los usé y los verifiqué.
>
> La caja quedó con `pm clear` y sin los archivos de prueba que usé.
