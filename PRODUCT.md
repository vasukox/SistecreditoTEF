# Product

<!-- impeccable:product-schema 1 -->

Historia de Usuario: HU8-973 (Azure DevOps).

## Platform

android

## Users

- **Cajero de tienda Permoda.** Usa la terminal de mostrador HioPos de pie, en la caja, con un cliente esperando enfrente. Quiere terminar rápido y sin errores una venta a crédito o el cobro de una cuota.
- **Administrador de la caja** (líder de tienda o soporte). Instala y configura cada terminal una sola vez: elige la tienda, crea el PIN de administrador, da de alta a los cajeros o copia la configuración de otra caja de la misma tienda.
- **Cliente Sistecrédito.** No toca la terminal. Ve la segunda pantalla orientada hacia él: su cupo, su plan de cuotas, el aviso del código por WhatsApp y la confirmación.

## Product Purpose

Módulo de crédito Sistecrédito (Credinet) que corre en las terminales HioPos de Permoda. Tiene dos caminos en un solo APK (`com.permoda.sistecreditotef`):

1. **Venta a crédito.** La abre la POS (HioPos) con la factura en curso: consultar cliente → cupo → simular cuotas → OTP por WhatsApp → crédito creado → devolver el control a la POS.
2. **Abonos / recaudo.** Se abre desde el ícono del lanzador, o desde HioPos como entrada de caja: ingreso del cajero → créditos activos del cliente → pago → recibo impreso.

Hay éxito cuando el cajero completa cada operación sin dudar, la caja queda configurada en minutos y el cliente entiende en todo momento qué está pasando con su crédito.

## Positioning

Es el puente entre la POS de la tienda y Sistecrédito, dentro de la misma caja. No es una app de banca para el cliente final: es una herramienta de mostrador, y la POS manda mientras hay una venta abierta.

## Operating Context

- **Terminal:** de mostrador, Android, en orientación horizontal (tipo Sunmi/PAX, arm64). Se lee de pie, a veces con reflejos de luz.
- **Segunda pantalla para el cliente:** `Presentation` nativa de Android.
- **Impresora:** térmica ESC/POS por USB (SOL801V / Epson TM-T88V), con respaldo de impresión de Android y PDF.
- **Sistemas que intervienen:**
  - HioPos (ICG) lanza el módulo por intents (`icg.actions.electronicpayment.permoda.*`) y espera un resultado. Mientras la POS tiene una operación abierta, el módulo no decide la navegación.
  - Configuración de nube (CloudLicense) por el intent INITIALIZE.
  - Catálogo de 76 tiendas.
  - Copia de configuración y de cajeros entre cajas de la misma tienda: por la red local, con un código de 6 dígitos válido 10 minutos.
- **OTP:** siempre por WhatsApp a la línea del cliente.

## Capabilities and Constraints

- **Stack:** .NET 10 MAUI, solo Android (minSdk 24), MVVM con CommunityToolkit.Mvvm. Hoy no usa CommunityToolkit.Maui, Lottie ni SkiaSharp. Este stack es obligatorio por política del Equipo de Arquitectura — DOPE.
- **Funcionamiento fuera de alcance del rediseño:** el rediseño es solo visual y de UX. No cambia contratos con HioPos, llamadas a Credinet, reglas de navegación entre POS y módulo, persistencia ni validaciones de negocio.
- **Montos:** en pesos colombianos, con separador de miles. El sandbox rechaza créditos de $120.000 o más; producción no tiene tope.
- **Documentos aceptados:** CC y CE.
- **Fuente:** se aprobó una fuente de marca empaquetada en el APK.
- **Modo oscuro:** no está en el alcance.

## Brand Commitments

- **Paleta Sistecrédito/Permoda v4, que se mantiene:**
  - Azul intenso `#4554A1`
  - Lima `#E0E275`
  - Gris oscuro `#333333`
  - Gris claro `#F2F2F2`
- **Reglas de contraste:** el lima nunca lleva texto blanco; solo el azul lleva texto blanco.
- **Ícono del APK:** el wordmark de Sistecrédito.
- **Voz:** español de Colombia, trato de "tú" con el cajero y de "usted" con el cliente en la pantalla del cliente.

## Evidence on Hand

- **Ícono:** `src/SistecreditoTEF.Maui/Resources/AppIcon/sistecredito_icon.png`, el wordmark de 1024².
- **Logo de KOAJ:** `koaj_logo.png`, hoy usado en la pantalla del cliente y como logo del medio de pago en HioPos.
- **Lo que no hay:** no existe un manual de marca en el repositorio, ni un wordmark vectorial de Sistecrédito en la app.

## Product Principles

1. **La POS manda.** Mientras HioPos tiene una venta abierta, el módulo solo ayuda a terminarla o a volver a ella.
2. **Cada toque tiene respuesta visible.** Ninguna operación de red o de impresión queda en silencio.
3. **Configurar una caja es un rito de una vez.** Debe ser claro, corto y difícil de hacer mal.
4. **Un solo lenguaje en todo el módulo.** Las mismas piezas en ambos caminos.

## Accessibility & Inclusion

- Lectura de pie y a distancia de brazo, con reflejos: contraste AA como mínimo y cifras grandes.
- Objetivos táctiles de 48dp o más.
