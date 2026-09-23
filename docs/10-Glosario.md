# 10 · Glosario

| Término | Significado |
|---|---|
| **Abono** | Pago de una cuota de un crédito ya existente. En el módulo hay dos vías: desde el POS (*entrada de caja*) o de forma autónoma desde el ícono de la aplicación |
| **APK Name** (`apk_name`) | Nombre con el que ICG identifica el módulo en CloudLicense. Va embebido en cada acción del contrato y es la llave de enrutamiento del POS |
| **AVAL** | Seguro obligatorio incluido en cada cuota del crédito |
| **Broadcast de auditoría** | Mensaje que el módulo envía al POS con cada operación relevante, para que quede en la traza de HioPos |
| **CloudLicense** | Portal de ICG donde se da de alta el módulo y se provisionan sus parámetros por terminal |
| **Credinet** | Plataforma y API REST de Sistecrédito |
| **DIAN** | Autoridad tributaria colombiana. HioPosCloud reenvía a la DIAN cualquier documento que interprete como nuevo, de ahí la regla de solo modificar los medios de pago |
| **Entrada de caja** | Operación de HioPos para registrar un ingreso sin factura abierta. Es la vía por la que el POS inicia un recaudo |
| **HioPosCloud** | Software POS de ICG Software que corre en las terminales de tienda |
| **ICG Software** | Fabricante de HioPosCloud |
| **Idempotencia** | Propiedad por la cual repetir una operación no la duplica |
| **Intent** | Mecanismo de comunicación entre aplicaciones de Android. Es el canal entre HioPos y el módulo |
| **Modo autónomo** | Operación del módulo abierto desde su propio ícono, fuera de HioPos |
| **OTP** | Código de un solo uso que Credinet envía al cliente para autorizar el crédito. En este módulo llega por WhatsApp |
| **PBKDF2** | Función de derivación de claves usada para almacenar las contraseñas de los cajeros |
| **Pinning (certificate pinning)** | Exigir que el certificado del servidor coincida con una huella conocida, además de la validación TLS estándar |
| **Sandbox** | Ambiente de pruebas de Credinet. Las operaciones que se hacen ahí no existen para Sistecrédito ni para el cliente |
| **Sistecrédito** | Entidad que otorga el crédito al consumidor final |
| **SPKI** | Clave pública del certificado; es lo que se fija en el *certificate pinning* |
| **SQLCipher** | Extensión de SQLite que cifra la base de datos en disco |
| **TEA** | Tasa Efectiva Anual. Es obligatorio informarla al cliente antes de que autorice |
| **TEF** | Transferencia Electrónica de Fondos; es la categoría con la que HioPos clasifica este tipo de módulo de cobro |
| **Terminal** | Dispositivo Android de la tienda donde corren HioPos y este módulo |
| **`versionCode`** | Entero interno de versión de Android. Debe subir en cada publicación |
| **`versionName`** | Versión visible de la aplicación. Libre |
| **Versión de contrato** | Número entero que el módulo reporta al POS y que debe coincidir con lo registrado en ICG. No se deriva del `versionName` |
