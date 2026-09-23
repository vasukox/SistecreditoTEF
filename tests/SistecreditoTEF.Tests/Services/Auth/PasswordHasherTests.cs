using SistecreditoTEF.Maui.Services.Auth;
using Xunit;

namespace SistecreditoTEF.Tests.Services.Auth;

/// <summary>
/// Derivación de las claves de los cajeros.
///
/// Es el único punto donde una clave podría quedar recuperable. En un POS con adb
/// habilitado para soporte y acceso físico al terminal, una clave guardada de forma
/// reversible es una clave extraíble — y como las personas reutilizan claves, el
/// daño sale del alcance de esta aplicación.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void El_hash_no_contiene_la_clave()
    {
        var hash = PasswordHasher.Hash("MiClaveSecreta123");

        Assert.DoesNotContain("MiClaveSecreta123", hash);
    }

    [Fact]
    public void La_clave_correcta_verifica()
    {
        var hash = PasswordHasher.Hash("1234");

        Assert.True(PasswordHasher.Verify("1234", hash));
    }

    [Fact]
    public void Una_clave_distinta_no_verifica()
    {
        var hash = PasswordHasher.Hash("1234");

        Assert.False(PasswordHasher.Verify("1235", hash));
    }

    [Fact]
    public void La_misma_clave_produce_hashes_DISTINTOS()
    {
        // El salt aleatorio por clave es lo que evita que se vea en la base quién
        // comparte clave, y que una sola tabla precalculada sirva para todos.
        var a = PasswordHasher.Hash("1234");
        var b = PasswordHasher.Hash("1234");

        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("1234", a));
        Assert.True(PasswordHasher.Verify("1234", b));
    }

    [Fact]
    public void El_hash_lleva_version_e_iteraciones_para_poder_subirlas_despues()
    {
        var hash = PasswordHasher.Hash("1234");
        var partes = hash.Split('$');

        Assert.Equal(4, partes.Length);
        Assert.Equal("v1", partes[0]);
        Assert.True(int.Parse(partes[1]) >= 100_000,
            "Pocas iteraciones abaratan el ataque por fuerza bruta.");
    }

    [Fact]
    public void Es_sensible_a_mayusculas()
    {
        var hash = PasswordHasher.Hash("Clave");

        Assert.False(PasswordHasher.Verify("clave", hash));
    }

    // ------------------------------------------------------------------
    // Robustez: un registro corrupto niega el acceso, no tumba la app
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("basura")]
    [InlineData("v1$abc$def")]                  // pocas partes
    [InlineData("v2$1000$YWJj$ZGVm")]           // version desconocida
    [InlineData("v1$0$YWJj$ZGVm")]              // iteraciones invalidas
    [InlineData("v1$1000$no-es-base64!$ZGVm")]  // salt ilegible
    public void Un_hash_ilegible_niega_el_acceso_sin_lanzar(string? hash)
    {
        Assert.False(PasswordHasher.Verify("1234", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Una_clave_vacia_nunca_verifica(string? clave)
    {
        var hash = PasswordHasher.Hash("1234");

        Assert.False(PasswordHasher.Verify(clave, hash));
    }

    [Fact]
    public void Hashear_una_clave_vacia_es_un_error_de_programacion()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }
}
