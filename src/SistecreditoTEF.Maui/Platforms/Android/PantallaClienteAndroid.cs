using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.PantallaCliente;
using AApplication = Android.App.Application;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace SistecreditoTEF.Maui.Platforms.Android;

/// <summary>
/// PINTA LA PANTALLA DE 11 PULGADAS QUE MIRA AL CLIENTE.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// COMO SE LLEGA A ESA PANTALLA
/// ─────────────────────────────────────────────────────────────────────────────
/// Android expone las pantallas secundarias por [DisplayManager]. Las que sirven
/// para mostrarle algo a otra persona —justamente el caso del display del
/// cliente— vienen en la categoria <c>DISPLAY_CATEGORY_PRESENTATION</c>, y se
/// pintan con una [Presentation], que es un Dialog atado a un display distinto
/// del principal.
///
/// Se usa una Presentation con VISTAS NATIVAS de Android y no una pagina de MAUI.
/// Una segunda pagina de MAUI seria una segunda Window con su propio Shell, y el
/// ruteo de esta app —que ya tuvo su historia con dos navegaciones compitiendo—
/// no tiene por que enterarse de que existe un cartel. Aca se dibujan seis
/// TextView y se termina.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// EL CLIENTE MIRA, NO TOCA
/// ─────────────────────────────────────────────────────────────────────────────
/// La ventana se declara no enfocable, se traga los toques y no responde a
/// teclas. No es solo "que no haga nada util": si los toques PASARAN de largo,
/// irian a parar a lo que haya detras en ese display —el launcher de Android, por
/// ejemplo— y cualquiera podria ponerse a navegar el terminal desde el mostrador.
/// Por eso se consumen aca en vez de declarar la ventana intocable.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// NADA DE ESTO PUEDE COSTAR UNA VENTA
/// ─────────────────────────────────────────────────────────────────────────────
/// Todo va envuelto. Terminal sin segunda pantalla, fabricante que la maneja con
/// su propio servicio, display que se desconecta a mitad de una factura: en todos
/// los casos se registra y la venta sigue. Ver [IPantallaCliente].
/// </summary>
public sealed class PantallaClienteAndroid : Java.Lang.Object,
    IPantallaCliente, DisplayManager.IDisplayListener
{
    private const string Tag = "Vitrina";

    private readonly DisplayManager? _pantallas;
    private VitrinaPresentation? _ventana;
    private VitrinaCliente _ultima = VitrinaCliente.Reposo;

    /// <summary>
    /// Para no repetir "no hay segunda pantalla" en cada navegacion. En un
    /// terminal de una sola pantalla eso serian diez lineas por venta.
    /// </summary>
    private bool _yaSeAvisoQueNoHay;

    public PantallaClienteAndroid()
    {
        try
        {
            _pantallas = AApplication.Context.GetSystemService(Context.DisplayService)
                as DisplayManager;

            if (_pantallas is null)
            {
                Log.Warn(Tag, "No se pudo obtener el DisplayManager; no habra pantalla de cliente.");
                return;
            }

            _pantallas.RegisterDisplayListener(this, (Handler?)null);
            Inventariar();
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"No se pudo inicializar la pantalla del cliente: {ex.Message}");
        }
    }

    public bool Disponible
    {
        get
        {
            try { return Buscar() is not null; }
            catch { return false; }
        }
    }

    public void Mostrar(VitrinaCliente vitrina)
    {
        if (vitrina is null) return;
        _ultima = vitrina;
        EnHiloDeUi(() => Pintar(vitrina));
    }

    public void Ocultar() => EnHiloDeUi(Cerrar);

    // ------------------------------------------------------------------
    // Busqueda de la pantalla
    // ------------------------------------------------------------------

    /// <summary>
    /// Deja en el log TODAS las pantallas que ve el sistema, con su id, su nombre
    /// y sus banderas.
    ///
    /// Vale la cuota de log: si en alguna terminal la pantalla del cliente no
    /// aparece en la categoria de presentacion —porque el fabricante la maneja
    /// aparte—, esto es lo unico que lo dice sin tener que ir a la tienda con un
    /// cable. Se escribe una sola vez, al arrancar.
    /// </summary>
    private void Inventariar()
    {
        try
        {
            var todas = _pantallas?.GetDisplays();
            if (todas is null || todas.Length == 0)
            {
                Log.Info(Tag, "El sistema no reporta ninguna pantalla.");
                return;
            }

            foreach (var d in todas)
            {
                Log.Info(Tag,
                    $"Pantalla id={d.DisplayId} nombre='{d.Name}' estado={d.State} " +
                    $"banderas={d.Flags} valida={d.IsValid}");
            }

            var elegida = Buscar();
            Log.Info(Tag, elegida is null
                ? "Ninguna sirve como pantalla de cliente (categoria presentacion)."
                : $"Pantalla del cliente: id={elegida.DisplayId} ('{elegida.Name}').");
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"No se pudo inventariar las pantallas: {ex.Message}");
        }
    }

    /// <summary>
    /// La primera pantalla de presentacion que no sea la principal y este viva.
    ///
    /// Se comprueba el id contra la principal ademas de pedir la categoria: hay
    /// terminales que devuelven la propia pantalla del cajero en la lista, y
    /// montar una Presentation ahi taparia la pantalla con la que se trabaja.
    /// </summary>
    private Display? Buscar()
    {
        var candidatas = _pantallas?.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        if (candidatas is null) return null;

        foreach (var d in candidatas)
        {
            if (d is null) continue;
            if (d.DisplayId == Display.DefaultDisplay) continue;
            if (!d.IsValid) continue;
            return d;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Pintado
    // ------------------------------------------------------------------

    private void Pintar(VitrinaCliente vitrina)
    {
        try
        {
            var pantalla = Buscar();
            if (pantalla is null)
            {
                Cerrar();
                if (!_yaSeAvisoQueNoHay)
                {
                    _yaSeAvisoQueNoHay = true;
                    Log.Info(Tag, "Este terminal no expone una segunda pantalla: no se muestra nada al cliente.");
                }
                return;
            }

            _yaSeAvisoQueNoHay = false;

            // La Presentation es un Dialog: necesita el token de ventana de una
            // Activity VIVA. Con el contexto de la aplicacion revienta con
            // BadTokenException, y con una Activity que se esta yendo tambien.
            //
            // CurrentActivity LANZA cuando no hay ninguna, no devuelve null; por eso
            // va en su propio try y no en una comprobacion de null.
            Activity? actividad = null;
            try { actividad = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity; }
            catch { /* la app no tiene Activity al frente */ }

            if (actividad is null || actividad.IsFinishing || actividad.IsDestroyed)
            {
                Cerrar();
                return;
            }

            if (_ventana is not null && !_ventana.SigueSirviendo(actividad, pantalla))
                Cerrar();

            if (_ventana is null)
            {
                _ventana = new VitrinaPresentation(actividad, pantalla);
                _ventana.Show();
                Log.Info(Tag, $"Pantalla del cliente abierta en el display {pantalla.DisplayId}.");
            }

            _ventana.Pintar(vitrina);
        }
        catch (Exception ex)
        {
            // WindowManager.InvalidDisplayException cuando el display se fue entre
            // que lo encontramos y lo usamos; BadTokenException si la Activity se
            // cerro en el medio. Las dos son normales en un terminal real.
            Log.Warn(Tag, $"No se pudo pintar la pantalla del cliente: {ex.Message}");
            Cerrar();
        }
    }

    private void Cerrar()
    {
        var ventana = _ventana;
        _ventana = null;

        if (ventana is null) return;

        try
        {
            if (ventana.IsShowing) ventana.Dismiss();
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"No se pudo cerrar la pantalla del cliente: {ex.Message}");
        }
    }

    private static void EnHiloDeUi(Action accion)
    {
        try
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                try { accion(); }
                catch (Exception ex) { Log.Warn(Tag, $"Fallo en el hilo de UI: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"No se pudo despachar al hilo de UI: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // El display se conecta y se desconecta en caliente
    // ------------------------------------------------------------------

    public void OnDisplayAdded(int displayId)
    {
        Log.Info(Tag, $"Se conecto la pantalla {displayId}.");
        Mostrar(_ultima);
    }

    public void OnDisplayRemoved(int displayId)
    {
        Log.Info(Tag, $"Se desconecto la pantalla {displayId}.");
        EnHiloDeUi(Cerrar);
    }

    /// <summary>
    /// Se ignora a proposito. Llega por cualquier cambio —rotacion, brillo,
    /// encendido— y repintar en cada uno haria parpadear el cartel sin motivo.
    /// </summary>
    public void OnDisplayChanged(int displayId) { }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _pantallas?.UnregisterDisplayListener(this); }
            catch (Exception ex) { Log.Warn(Tag, $"No se pudo soltar el listener: {ex.Message}"); }

            Cerrar();
        }

        base.Dispose(disposing);
    }

    // ==================================================================
    // LA VENTANA
    // ==================================================================

    /// <summary>
    /// El cartel en si. Se construye una vez y despues solo se le cambian los
    /// textos: recrear la jerarquia de vistas en cada navegacion haria un
    /// parpadeo negro en una pantalla que el cliente esta mirando.
    /// </summary>
    private sealed class VitrinaPresentation : Presentation
    {
        // Paleta de marca. Los mismos valores que [Colors.xaml]; aca no hay
        // ResourceDictionary porque esto son vistas nativas.
        private static readonly AColor Fondo      = AColor.ParseColor("#F2F2F2");
        private static readonly AColor Superficie = AColor.ParseColor("#FFFFFF");
        private static readonly AColor Texto      = AColor.ParseColor("#333333");
        private static readonly AColor TextoSuave = AColor.ParseColor("#6B6B6B");
        private static readonly AColor Azul       = AColor.ParseColor("#4554A1");
        private static readonly AColor Verde      = AColor.ParseColor("#2E7D52");

        private readonly Activity _dueno;
        private readonly int _displayId;

        private TextView? _titulo;
        private TextView? _subtitulo;
        private TextView? _etiqueta;
        private TextView? _monto;
        private TextView? _aviso;
        private LinearLayout? _tarjeta;

        private VitrinaCliente? _pintada;

        public VitrinaPresentation(Activity dueno, Display pantalla)
            : base(dueno, pantalla)
        {
            _dueno = dueno;
            _displayId = pantalla.DisplayId;
        }

        /// <summary>
        /// ¿Esta ventana sigue siendo la correcta? Deja de serlo si Android recreo
        /// la Activity —el Dialog muere con ella—, si la pantalla del cliente paso
        /// a ser otra, o si ya no se esta mostrando.
        /// </summary>
        public bool SigueSirviendo(Activity actividad, Display pantalla) =>
            ReferenceEquals(_dueno, actividad)
            && _displayId == pantalla.DisplayId
            && IsShowing;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            RequestWindowFeature((int)WindowFeatures.NoTitle);
            SetCancelable(false);
            SetCanceledOnTouchOutside(false);

            var ventana = Window;
            if (ventana is not null)
            {
                // NotFocusable: no le roba el foco de teclado a la pantalla del
                // cajero. KeepScreenOn: el cliente no tiene como despertar esta
                // pantalla si se apaga sola en medio de la venta.
                ventana.AddFlags(WindowManagerFlags.NotFocusable
                                 | WindowManagerFlags.KeepScreenOn
                                 | WindowManagerFlags.Fullscreen);
                ventana.SetBackgroundDrawable(new ColorDrawable(Fondo));
                ventana.SetLayout(ViewGroup.LayoutParams.MatchParent,
                                  ViewGroup.LayoutParams.MatchParent);
            }

            SetContentView(Construir());
        }

        /// <summary>Se traga el toque. Ver el encabezado de la clase externa.</summary>
        public override bool OnTouchEvent(MotionEvent? e) => true;

        /// <summary>Ninguna tecla —incluido "atras"— cierra ni altera el cartel.</summary>
        public override bool OnKeyDown(Keycode keyCode, KeyEvent? e) => true;

        public void Pintar(VitrinaCliente vitrina)
        {
            // Idempotente: el Shell dispara Navigated mas de una vez por
            // navegacion y no hay por que tocar las vistas si no cambio nada.
            if (vitrina == _pintada) return;
            _pintada = vitrina;

            var acento = vitrina.Paso == PasoDeLaVitrina.Cierre ? Verde : Azul;

            Poner(_titulo, vitrina.Titulo);
            Poner(_subtitulo, vitrina.Subtitulo);
            Poner(_etiqueta, vitrina.EtiquetaDelMonto);
            Poner(_monto, vitrina.Monto);
            Poner(_aviso, vitrina.Aviso);

            _monto?.SetTextColor(acento);
            _aviso?.SetTextColor(acento);

            if (_tarjeta is not null)
                _tarjeta.Visibility = vitrina.TieneMonto ? ViewStates.Visible : ViewStates.Gone;
        }

        private static void Poner(TextView? vista, string? texto)
        {
            if (vista is null) return;

            if (string.IsNullOrWhiteSpace(texto))
            {
                vista.Visibility = ViewStates.Gone;
                return;
            }

            vista.Text = texto;
            vista.Visibility = ViewStates.Visible;
        }

        // --------------------------------------------------------------
        // Construccion de las vistas
        // --------------------------------------------------------------

        private AView Construir()
        {
            var columna = new LinearLayout(Context)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent)
            };
            columna.SetGravity(GravityFlags.Center);
            columna.SetBackgroundColor(Fondo);
            columna.SetPadding(Dp(56), Dp(40), Dp(56), Dp(40));

            // El toque muere en la raiz: Clickable evita que se propague a lo que
            // haya detras en este display.
            columna.Clickable = true;
            columna.Focusable = false;

            // Con [Margen], y no con AddView(logo) a secas: un LinearLayout vertical
            // le da MATCH_PARENT de ancho a los hijos que se agregan sin parametros,
            // asi que la tarjeta blanca del logo se estiraba de borde a borde de la
            // pantalla del cliente con el logo pegado a la izquierda. Se vio recien
            // al capturar el display de la terminal.
            var logo = ConstruirLogo();
            if (logo is not null) columna.AddView(logo, Margen(0));

            _titulo = Letrero(46, Texto, negrita: true);
            columna.AddView(_titulo, Margen(logo is null ? 0 : 28));

            _subtitulo = Letrero(24, TextoSuave, negrita: false);
            columna.AddView(_subtitulo, Margen(14));

            _tarjeta = ConstruirTarjeta();
            columna.AddView(_tarjeta, Margen(32));

            _aviso = Letrero(24, Azul, negrita: true);
            columna.AddView(_aviso, Margen(22));

            var raiz = new FrameLayout(Context);
            raiz.SetBackgroundColor(Fondo);
            raiz.AddView(columna);
            return raiz;
        }

        /// <summary>
        /// El logo sobre una tarjeta blanca. Va en tarjeta y no suelto porque el
        /// PNG de marca trae fondo blanco horneado (es el mismo archivo que el
        /// icono del launcher): puesto directo sobre el gris se veria un recuadro
        /// blanco accidental, y en tarjeta se ve deliberado.
        /// </summary>
        private AView? ConstruirLogo()
        {
            try
            {
                var bytes = global::SistecreditoTEF.Maui.Resources.Images.SistecreditoLogo.PngBytes;
                if (bytes.Length == 0) return null;

                var mapa = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (mapa is null) return null;

                var imagen = new ImageView(Context);
                imagen.SetImageBitmap(mapa);
                imagen.SetAdjustViewBounds(true);
                imagen.LayoutParameters = new LinearLayout.LayoutParams(Dp(104), Dp(104));

                var tarjeta = new LinearLayout(Context) { Orientation = Orientation.Vertical };
                tarjeta.Background = Redondeado(Superficie, Dp(22));
                tarjeta.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
                tarjeta.SetGravity(GravityFlags.Center);
                tarjeta.AddView(imagen);
                return tarjeta;
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"No se pudo dibujar el logo en la pantalla del cliente: {ex.Message}");
                return null;
            }
        }

        /// <summary>La tarjeta blanca con el importe. Se oculta cuando no hay.</summary>
        private LinearLayout ConstruirTarjeta()
        {
            var tarjeta = new LinearLayout(Context) { Orientation = Orientation.Vertical };
            tarjeta.SetGravity(GravityFlags.CenterHorizontal);
            tarjeta.Background = Redondeado(Superficie, Dp(26));
            tarjeta.SetPadding(Dp(44), Dp(24), Dp(44), Dp(28));

            _etiqueta = Letrero(19, TextoSuave, negrita: false);
            _etiqueta.SetAllCaps(true);
            _etiqueta.LetterSpacing = 0.12f;
            tarjeta.AddView(_etiqueta);

            _monto = Letrero(68, Azul, negrita: true);
            tarjeta.AddView(_monto, Margen(6));

            return tarjeta;
        }

        private TextView Letrero(float tamano, AColor color, bool negrita)
        {
            var vista = new TextView(Context);
            vista.SetTextSize(ComplexUnitType.Sp, tamano);
            vista.SetTextColor(color);
            vista.SetTypeface(Typeface.Default, negrita ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            // global:: obligatorio: estamos DENTRO de un namespace que termina en
            // ".Android", asi que "Android.Views" resolveria a este, no al del SDK.
            vista.TextAlignment = global::Android.Views.TextAlignment.Center;
            vista.Gravity = GravityFlags.Center;
            vista.Visibility = ViewStates.Gone;
            return vista;
        }

        private LinearLayout.LayoutParams Margen(int arribaDp)
        {
            var p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = Dp(arribaDp)
            };
            p.Gravity = GravityFlags.CenterHorizontal;
            return p;
        }

        private static GradientDrawable Redondeado(AColor relleno, float radio)
        {
            var forma = new GradientDrawable();
            forma.SetShape(ShapeType.Rectangle);
            forma.SetColor(relleno);
            forma.SetCornerRadius(radio);
            return forma;
        }

        /// <summary>
        /// Convierte a pixeles USANDO LA DENSIDAD DE ESTA PANTALLA, no la del
        /// terminal. El Context de una Presentation ya viene ajustado al display
        /// en el que vive, y las dos pantallas de un POS no tienen la misma
        /// densidad: medir con la del cajero daria un cartel desproporcionado.
        /// </summary>
        private int Dp(float valor)
        {
            var densidad = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            return (int)((valor * densidad) + 0.5f);
        }
    }
}
