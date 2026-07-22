using SistecreditoTEF.Maui.Common;
using Xunit;

namespace SistecreditoTEF.Maui.Tests.Common;

/// <summary>
/// Tests del ApiResult&lt;T&gt;: cubre mapeo, propagacion de errores y
/// discrimination por pattern matching.
/// </summary>
public class ApiResultTests
{
    [Fact]
    public void Ok_carries_data()
    {
        var result = new ApiResult<int>.Ok<int>(42);
        Assert.Equal(42, result.Data);
    }

    [Fact]
    public void Failure_carries_cause()
    {
        var ex = new ApiError.Http(500, "boom");
        var result = new ApiResult<int>.Failure<int>(ex);
        Assert.Equal(ex, result.Cause);
    }

    [Fact]
    public void Map_on_Ok_transforms_data_and_preserves_type()
    {
        var ok = new ApiResult<int>.Ok<int>(10);
        var mapped = ok.Map(x => x.ToString());
        Assert.IsType<ApiResult<string>.Ok<string>>(mapped);
        Assert.Equal("10", ((ApiResult<string>.Ok<string>)mapped).Data);
    }

    [Fact]
    public void Map_on_Failure_passes_through_unchanged()
    {
        var cause = new ApiError.Http(503, "down");
        var failure = new ApiResult<int>.Failure<int>(cause);
        var mapped = failure.Map(x => x.ToString());
        Assert.IsType<ApiResult<string>.Failure<string>>(mapped);
        Assert.Equal(cause, ((ApiResult<string>.Failure<string>)mapped).Cause);
    }
}
