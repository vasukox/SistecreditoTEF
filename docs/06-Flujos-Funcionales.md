# 06 · Flujos funcionales

> Qué ve y qué hace el cajero, y qué ocurre por detrás en cada paso. Los contratos técnicos
> están en [`04-Integracion-HioPosCloud.md`](04-Integracion-HioPosCloud.md) y
> [`05-Integracion-Credinet.md`](05-Integracion-Credinet.md).

---

## 1. Los tres modos de operación

El mismo APK atiende tres situaciones distintas. El modo se resuelve al arrancar, según **cómo
se abrió la aplicación**, y determina quién imprime y cómo se cierra la operación.

| Modo | Cómo empieza | Hay factura en el POS | Identifica al cajero | Imprime | Al terminar |
|---|---|---|---|---|---|
| **A · Venta a crédito** | El cajero elige Sistecrédito como medio de pago | Sí | No: ya se identificó en el POS | El POS, con el XML que devuelve el módulo | Devuelve el resultado al POS, que cierra la factura |
| **B · Recaudo desde el POS** | El cajero entra por *Caja → Entradas de caja* | No | No | El módulo, por la impresora térmica | Devuelve el resultado al POS, que registra el movimiento |
| **C · Abono autónomo** | El cajero abre el ícono de la aplicación | No | **Sí**: usuario y clave | El módulo, por la impresora térmica | Cierra la aplicación; no hay nadie esperando resultado |

La pantalla raíz es neutra —una pantalla de marca— y resuelve el destino antes de mostrar
ningún módulo. Si la operación la originó HioPos, la raíz no decide nada: la navegación la
conduce la actividad que recibió el Intent.

> **Protección entre modos:** mientras hay una operación de HioPos viva, un arranque desde el
> ícono del lanzador se descarta para no pisar la venta que el POS está esperando. A la inversa,
> al comenzar cada operación del POS se limpia el estado residual del modo anterior.

---

## 2. Modo A — Venta a crédito

```
1. El cajero elige "Sistecredito" en HioPos            → el POS lanza la operación al módulo
2. El módulo lee la venta (importe, identificador, documento del cliente si viene en la factura)
3. Consultar cliente     · el cajero digita la cédula  → consulta el cupo del cliente
4. Validación del cliente· se muestran nombre y cupo   → el cajero confirma y continúa
5. Selección de cuotas   · monto y plazo               → consulta plazos posibles
                                                       → consulta cuota, tasa efectiva anual y aval
6. Código de autorización· se solicita el código       → Credinet envía el OTP por WhatsApp
                          el cajero digita el código   → se crea el crédito
7. Confirmación          · resultado en pantalla       → resultado ACEPTADO al POS + comprobantes
8. El POS imprime y cierra la factura.
```

Detalles que importan en caja:

- **La cédula se sanea al capturarla.** El teclado numérico de algunas terminales inserta
  separadores de miles; enviarlos hacía que Credinet respondiera "cliente no encontrado" con una
  cédula perfectamente válida.
- **Si la factura trae el documento del cliente, se autocompleta.** El cajero solo confirma.
- **La tasa efectiva anual y el aval se muestran siempre** antes de que el cliente autorice: es
  obligatorio informarlos.
- **La consulta de plazos válidos dispara varias peticiones en paralelo**, acotadas a cuatro
  conexiones simultáneas para no saturar el enlace de la terminal.
- **El importe que se devuelve al POS es el efectivamente cobrado.**
- **Un reintento de la misma venta no crea un segundo crédito** (ver `05` §6).

Si algo falla, el módulo siempre devuelve una respuesta al POS: nunca lo deja esperando.

---

## 3. Modo B — Recaudo desde el POS (entrada de caja)

```
1. El cajero entra por Caja → Entradas de caja y elige Sistecrédito
   → el POS lanza la operación SIN documento de venta
2. El módulo detecta la ausencia de documento: es un recaudo, no una venta
3. Créditos activos  · el cajero digita la cédula   → lista los créditos activos del cliente
4. Pago              · elige el crédito y el monto  → registra el pago
5. Comprobante       · se imprime localmente        → resultado al POS
```

Particularidades:

- **No se pasa por el flujo de crédito nuevo.** Nada de simulación de cuotas ni OTP: pagar una
  cuota de un crédito existente no lo requiere.
- **El tipo de operación que se devuelve es el que pidió el POS.** Reescribirlo hacía que HioPos
  relanzara la operación con un identificador nuevo, y el recaudo no quedaba registrado como
  movimiento de caja.
- **Quién imprime depende de si el POS tiene documento:**
  - *Entrada de caja* (sin documento): el POS descarta cualquier comprobante que se le envíe, así
    que **imprime el módulo** por la térmica.
  - *Venta abandonada para hacer un abono* (hay documento activo): el POS sí puede anexar e
    imprimir; se le envían los comprobantes y el módulo no imprime.
- **Un fallo de impresión no bloquea la respuesta al POS.** El abono ya está cobrado: dejar la
  caja trabada por un problema de papel sería peor que un comprobante faltante. Se informa lo
  que pasó y se responde igual.
- **La respuesta incluye el medio de pago fijo y su importe**, para que el POS registre el
  movimiento correctamente.

---

## 4. Modo C — Abono autónomo (desde el ícono)

```
1. El cajero abre la aplicación desde su ícono
2. ¿Primera vez en la terminal?  → Configuración inicial: crear el PIN de administración
   ¿Sin sesión abierta?          → Ingreso del cajero: usuario y clave
3. Créditos activos · cédula del cliente  → lista los créditos activos
4. Pago             · crédito y monto     → registra el pago
5. Comprobante      · se imprime localmente
6. La aplicación se cierra por completo (no hay resultado que devolver a nadie)
```

### 4.1 Identificación del cajero

Solo los abonos autónomos la piden, y por una razón concreta: se hacen **fuera de HioPos**, sin
que nadie haya validado quién está operando. En una venta el cajero ya se identificó en el POS y
volver a pedirle la clave sería frenar la facturación por nada.

| Pantalla | Cuándo aparece | Qué hace |
|---|---|---|
| **Configuración inicial** | Primera apertura en la terminal | Fija el PIN de administración. Solo se permite si no había uno; cambiarlo exige el anterior |
| **Ingreso del cajero** | Antes de cada abono, si no hay sesión | Identifica a quién opera |
| **Administración de cajeros** | Detrás del PIN | Alta, baja y cambio de clave |

- Las claves se guardan **derivadas con PBKDF2 (210.000 iteraciones)** en la base local cifrada;
  nunca en claro.
- **No hay bloqueo por intentos fallidos.** Se quitó por pedido explícito de operación: un cajero
  que olvida la clave frenaba la caja, y en atención al público esperar un bloqueo es peor que el
  riesgo que evitaba. El freno que queda es el costo de cada verificación —cientos de
  milisegundos de CPU—, que acota por sí solo a unos pocos intentos por segundo.
- **El nombre del cajero identificado viaja a Credinet** en el registro del recaudo, así que la
  traza deja de ser genérica para toda la cadena.

---

## 5. Cadena de impresión

Cuando imprime el módulo (modos B y C), se recorre esta cadena y se usa **el primer medio que
funcione de verdad**:

| Orden | Medio | Estado |
|---|---|---|
| 1 | **USB ESC/POS** — la térmica de la terminal | Camino real y verificado en hardware |
| 2 | **Nativo Sunmi** | Deshabilitado por defecto: requiere el AIDL oficial del fabricante y validación en hardware |
| 3 | **Android Print** | Solo sirve si la terminal tiene una impresora registrada en Ajustes; en las revisadas no hay ninguna |

Aprendizajes que explican el diseño y conviene no revertir:

- **La elección se hace en cada impresión, no al arrancar.** Elegir una vez dejaba al abono sin
  alternativa si ese medio fallaba.
- **La generación de un PDF ya no cuenta como impresión.** Devolvía éxito al crear el archivo, la
  cadena se detenía ahí y el cajero terminaba el abono sin que saliera nada por la térmica y sin
  aviso. El generador de PDF sigue disponible como acción manual de compartir o guardar, pero no
  participa de la impresión automática.
- **Si no se pudo imprimir, el cajero se entera** y puede reintentar. Si decide continuar sin
  imprimir, queda registrado.
- **El comprobante lleva nombre y cédula del cliente y el nombre real de la tienda**, tomado de
  la configuración de la terminal.

---

## 6. Mapa de pantallas

| Pantalla | Modo | Rol |
|---|---|---|
| Pantalla de marca (raíz) | Todos | Neutra; resuelve el destino sin mostrar un módulo equivocado |
| Menú | C | Dos opciones: venta a crédito y abonos |
| Configuración inicial · Ingreso del cajero · Administración de cajeros | C | Identificación (§4.1) |
| Consultar cliente | A | Captura y saneamiento de la cédula |
| Validación del cliente | A | Nombre y cupo disponible |
| Selección de cuotas | A | Monto, plazo, cuota, tasa efectiva anual y aval |
| Código de autorización | A | Solicitud, reenvío y verificación del OTP |
| Confirmación | A | Resultado del crédito y cierre hacia el POS |
| Créditos activos | B · C | Créditos vigentes del cliente |
| Pago | B · C | Monto a abonar, con los mínimos y el saldo a la vista |
| Comprobante del pago | B · C | Comprobante, impresión y cierre |

Los importes se digitan con separador de miles y se validan contra el mínimo y el saldo del
crédito; el botón de cobrar se habilita cuando el monto es válido, incluso cuando el mínimo
excede el saldo restante.
