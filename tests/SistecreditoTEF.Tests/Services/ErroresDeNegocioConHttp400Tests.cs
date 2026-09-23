using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Dtos;
using SistecreditoTEF.Maui.Services.Credinet;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Services;

/// <summary>
/// Credinet devuelve errores de NEGOCIO con HTTP 4xx, no solo con HTTP 200 +
/// errorCode como asumía el módulo.
///
/// Capturado en el terminal, en el flujo de creación de crédito:
///
///   HTTP 400 {"function":"/api/credit/create","errorCode":230,
///             "message":"TokenAlreadyUsed","country":"co"}
///
/// Tratarlo como fallo de transporte hacía que el cajero viera "Error de
/// comunicación con el servidor" en vez de "ese código ya se usó", que no se
/// consumiera intento (se vieron 8 intentos seguidos contra el mismo código
/// quemado) y que la traducción por errorCode nunca se aplicara.
/// </summary>
public class ErroresDeNegocioConHttp400Tests
{
    [Fact]
    public void Un_sobre_de_Credinet_con_errorCode_se_reconoce_como_negocio()
    {
        const string body = """
            {"function":"/api/credit/create","errorCode":230,
             "message":"TokenAlreadyUsed","country":"co"}
            """;

        var envelope = CredinetApiClient.TryParseCredinetEnvelope<CreditDto>(body);

        Assert.NotNull(envelope);
        Assert.Equal(230, envelope!.ErrorCode);
        Assert.Equal("TokenAlreadyUsed", envelope.Message);
        Assert.Equal("/api/credit/create", envelope.Function);
    }

    [Fact]
    public void Un_sobre_con_errorCode_cero_NO_es_error_de_negocio()
    {
        // Un 4xx con errorCode 0 no identifica ninguna regla de negocio: es un
        // problema técnico y debe tratarse como tal.
        const string body = """{"function":"/api/x","errorCode":0,"message":"","country":"co"}""";

        Assert.Null(CredinetApiClient.TryParseCredinetEnvelope<CreditDto>(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("Service Unavailable")]
    [InlineData("{ esto no es json valido")]
    public void Un_cuerpo_que_no_es_sobre_de_Credinet_devuelve_null(string? body)
    {
        // Así el llamador lo trata como fallo de transporte, que es lo correcto para
        // un error de gateway o de APIM.
        Assert.Null(CredinetApiClient.TryParseCredinetEnvelope<CreditDto>(body));
    }

    // ------------------------------------------------------------------
    // Traducción para el cajero
    // ------------------------------------------------------------------

    [Fact]
    public void El_codigo_230_se_traduce_como_codigo_ya_usado()
    {
        // Antes 230 se traducía como "El cliente no tiene cupo disponible", que
        // mandaba al cajero a buscar un problema de cupo inexistente.
        var mensaje = FriendlyMessage.FromApiError(
            new ApiError.Business(230, "TokenAlreadyUsed", "/api/credit/create"));

        Assert.Contains("ya fue usado", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reenviar", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cupo", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TokenAlreadyUsed", "ya fue usado")]
    [InlineData("TokenExpired", "expiro")]
    [InlineData("DuplicatedCredit", "ya tiene un credito")]
    [InlineData("CustomerNotFound", "No encontramos un cliente")]
    [InlineData("RequestValuesInvalid", "no son validos")]
    public void Los_mensajes_en_ingles_de_Credinet_se_traducen(string mensajeCredinet, string esperado)
    {
        // Red de seguridad para códigos que todavía no están mapeados: el cajero lee
        // español, no jerga técnica en inglés.
        var mensaje = FriendlyMessage.FromApiError(
            new ApiError.Business(9999, mensajeCredinet, "/api/x"));

        Assert.Contains(esperado, mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_error_de_negocio_desconocido_no_deja_al_cajero_sin_mensaje()
    {
        var mensaje = FriendlyMessage.FromApiError(
            new ApiError.Business(8888, "AlgoRaroQueNadieMapeo", "/api/x"));

        Assert.False(string.IsNullOrWhiteSpace(mensaje));
        Assert.DoesNotContain("CREDINET:", mensaje, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_error_de_transporte_sigue_dando_mensaje_de_comunicacion()
    {
        // Un 502 real debe seguir leyéndose como problema de comunicación: la
        // corrección no debe disfrazar fallos técnicos de errores de negocio.
        var mensaje = FriendlyMessage.FromApiError(new ApiError.Http(502, "Bad Gateway"));

        Assert.Contains("servidor", mensaje, StringComparison.OrdinalIgnoreCase);
    }
}
