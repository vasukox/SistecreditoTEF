using SistecreditoTEF.Maui.Services.Platform;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Platform;

/// <summary>
/// La red de seguridad contra caidas.
///
/// Estas pruebas cubren lo que se puede ejecutar sin Android: el registro, el
/// guardado y —lo que mas importa— que NINGUN fallo dentro de la propia red de
/// seguridad se propague. Un manejador global que lanza es peor que no tener uno.
///
/// El enganche con las tres puertas de Android ([CrashGuard]) no entra: eso se
/// verifica en terminal, provocando un fallo a proposito.
/// </summary>
public class RedDeSeguridadTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "fallos-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static Exception Romper(string mensaje = "boom")
    {
        try { throw new InvalidOperationException(mensaje); }
        catch (Exception ex) { return ex; }   // asi trae pila de verdad
    }

    // ==================================================================
    // El registro
    // ==================================================================

    [Fact]
    public void Un_fallo_queda_guardado_y_se_puede_leer_despues()
    {
        var store = new FileCrashStore(_carpeta);
        var handler = new CrashHandler(store)
        {
            PantallaActual = () => "PagoPage",
            Build = () => "20260923-1400 · sandbox"
        };

        handler.Registrar(Romper("algo se rompio"), CrashOrigin.Android);

        var archivos = store.Listar();
        Assert.Single(archivos);

        var texto = store.Leer(archivos[0])!;
        Assert.Contains("algo se rompio", texto, StringComparison.Ordinal);
        Assert.Contains("PagoPage", texto, StringComparison.Ordinal);
        Assert.Contains("20260923-1400", texto, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", texto, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sin esto, el POS se queda esperando, agota su tiempo y —verificado en
    /// terminal— cierra la venta SIN COBRAR. Es la parte de la red de seguridad
    /// que evita perder una factura por una excepcion de interfaz.
    /// </summary>
    [Fact]
    public void Se_le_avisa_al_POS_cuando_hay_una_operacion_viva()
    {
        var aviso = 0;
        var handler = new CrashHandler(new FileCrashStore(_carpeta))
        {
            AvisarAlPos = () => aviso++
        };

        handler.Registrar(Romper(), CrashOrigin.Android);

        Assert.Equal(1, aviso);
    }

    /// <summary>
    /// Lo mas importante del archivo: un manejador global que lanza convierte un
    /// fallo recuperable en una caida segura.
    /// </summary>
    [Fact]
    public void Si_todo_lo_de_adentro_falla_el_manejador_no_propaga_nada()
    {
        var handler = new CrashHandler(new StoreQueExplota())
        {
            PantallaActual = () => throw new InvalidOperationException("tampoco esto"),
            Build = () => throw new InvalidOperationException("ni esto"),
            AvisarAlPos = () => throw new InvalidOperationException("ni avisar"),
        };

        var fallo = handler.Registrar(Romper(), CrashOrigin.Runtime);

        Assert.NotNull(fallo);
        Assert.Null(fallo.Pantalla);
        Assert.Null(fallo.Build);
    }

    private sealed class StoreQueExplota : ICrashStore
    {
        public void Guardar(CrashRecord fallo) => throw new IOException("disco lleno");
        public IReadOnlyList<string> Listar() => throw new IOException("disco lleno");
        public string? Leer(string nombre) => throw new IOException("disco lleno");
        public void Limpiar() => throw new IOException("disco lleno");
    }

    [Fact]
    public void El_contador_permite_cortar_un_bucle_de_fallos()
    {
        var handler = new CrashHandler(new FileCrashStore(_carpeta));

        for (var i = 0; i < 7; i++) handler.Registrar(Romper(), CrashOrigin.Android);

        Assert.Equal(7, handler.Contador);
    }

    // ==================================================================
    // El guardado
    // ==================================================================

    /// <summary>
    /// Si algo falla en cada arranque, esto escribe uno por arranque. Sin tope, un
    /// bucle de fallos llena el disco del terminal — y un terminal sin espacio no
    /// cobra.
    /// </summary>
    [Fact]
    public void Se_conservan_solo_los_ultimos_y_no_crece_sin_limite()
    {
        var store = new FileCrashStore(_carpeta, maximo: 5);

        for (var i = 0; i < 20; i++)
        {
            store.Guardar(new CrashRecord(
                new DateTimeOffset(2026, 9, 23, 10, 0, i, TimeSpan.Zero),
                CrashOrigin.Android, "T", "m", "pila", null, null));
        }

        Assert.Equal(5, store.Listar().Count);
    }

    [Fact]
    public void Los_fallos_se_listan_del_mas_reciente_al_mas_viejo()
    {
        var store = new FileCrashStore(_carpeta);

        foreach (var seg in new[] { 10, 30, 20 })
        {
            store.Guardar(new CrashRecord(
                new DateTimeOffset(2026, 9, 23, 10, 0, seg, TimeSpan.Zero),
                CrashOrigin.Android, "T", "m", "pila", null, null));
        }

        var lista = store.Listar();

        Assert.Equal("crash-20260923-100030-000.txt", lista[0]);
        Assert.Equal("crash-20260923-100010-000.txt", lista[^1]);
    }

    /// <summary>
    /// Dos fallos encadenados —lo normal cuando algo se rompe— caen en el mismo
    /// segundo. Sin milisegundos en el nombre, el segundo pisaba al primero y se
    /// perdia justo el original.
    /// </summary>
    [Fact]
    public void Dos_fallos_en_el_mismo_segundo_no_se_pisan()
    {
        var store = new FileCrashStore(_carpeta);
        var t = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        store.Guardar(new CrashRecord(t, CrashOrigin.Android, "A", "primero", null, null, null));
        store.Guardar(new CrashRecord(t.AddMilliseconds(40), CrashOrigin.Android, "B", "segundo", null, null, null));

        Assert.Equal(2, store.Listar().Count);
    }

    [Fact]
    public void Leer_no_se_sale_de_su_carpeta()
    {
        var store = new FileCrashStore(_carpeta);
        store.Guardar(new CrashRecord(
            DateTimeOffset.Now, CrashOrigin.Android, "T", "m", null, null, null));

        Assert.Null(store.Leer(@"..\..\..\secretos.txt"));
        Assert.Null(store.Leer("no-existe.txt"));
    }

    [Fact]
    public void Guardar_en_una_carpeta_imposible_no_lanza()
    {
        var store = new FileCrashStore(Path.Combine("Z:", "no", "existe"));

        var ex = Record.Exception(() => store.Guardar(new CrashRecord(
            DateTimeOffset.Now, CrashOrigin.Android, "T", "m", null, null, null)));

        Assert.Null(ex);
        Assert.Empty(store.Listar());
    }

    // ==================================================================
    // El contenido
    // ==================================================================

    /// <summary>
    /// Una pila de .NET en Android pasa los 20 KB y lo que resuelve un caso son los
    /// primeros marcos. Guardar todo solo llena el disco del terminal.
    /// </summary>
    [Fact]
    public void La_pila_se_recorta()
    {
        var larga = new InvalidOperationException(new string('x', 20_000));

        var fallo = CrashRecord.De(larga, CrashOrigin.Android);

        Assert.True(fallo.Pila!.Length <= CrashRecord.MaxPila + 20);
        Assert.EndsWith("(recortado)", fallo.Pila, StringComparison.Ordinal);
    }

    /// <summary>
    /// En logcat un mensaje multilinea se parte y se mezcla con los de otros
    /// procesos: el resumen tiene que caber en una linea.
    /// </summary>
    [Fact]
    public void El_resumen_es_de_una_sola_linea()
    {
        var fallo = CrashRecord.De(
            new InvalidOperationException("con\nsalto\r\nde linea"), CrashOrigin.Android);

        var resumen = fallo.ToSummary();

        Assert.DoesNotContain('\n', resumen);
        Assert.DoesNotContain('\r', resumen);
    }
}
