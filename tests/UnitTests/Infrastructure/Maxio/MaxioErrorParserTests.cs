using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Covers each error envelope declared under maxio-spec/components/schemas/errors/.
/// </summary>
public class MaxioErrorParserTests
{
    [Fact]
    public void ReadsAnErrorListResponse()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":["Reference: must be unique - that value has been taken."]}""");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void ReadsACustomerErrorResponse()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":{"customer":"can't be blank"}}""");

        Assert.Equal(new[] { "customer: can't be blank" }, errors);
    }

    [Fact]
    public void ReadsAnErrorArrayMapResponse()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":{"base":["is invalid","is unsupported"]}}""");

        Assert.Equal(new[] { "base: is invalid", "base: is unsupported" }, errors);
    }

    [Fact]
    public void ReadsASingleErrorResponse()
    {
        var errors = MaxioErrorParser.Parse("""{"error":"Not Found"}""");

        Assert.Equal(new[] { "Not Found" }, errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("[1,2,3]")]
    public void ReturnsNothingForBodiesThatCarryNoStructuredDetail(string? body)
    {
        Assert.Empty(MaxioErrorParser.Parse(body));
    }
}
