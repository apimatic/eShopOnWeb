using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Covers every error envelope the Maxio OpenAPI specification declares for the operations this
/// integration calls.
/// </summary>
public class MaxioErrorParserTests
{
    [Fact]
    public void Parses_ErrorListResponse()
    {
        var messages = MaxioErrorParser.Parse("""{"errors":["Reference: must be unique - that value has been taken."]}""");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, messages);
    }

    [Fact]
    public void Parses_ErrorStringMap()
    {
        var messages = MaxioErrorParser.Parse("""{"errors":{"customer":"can't be blank"}}""");

        Assert.Equal(new[] { "customer: can't be blank" }, messages);
    }

    [Fact]
    public void Parses_ErrorArrayMap()
    {
        var messages = MaxioErrorParser.Parse("""{"errors":{"email":["is invalid","is required"]}}""");

        Assert.Equal(new[] { "email: is invalid", "email: is required" }, messages);
    }

    [Fact]
    public void Parses_SingleErrorResponse()
    {
        var messages = MaxioErrorParser.Parse("""{"error":"Product not found"}""");

        Assert.Equal(new[] { "Product not found" }, messages);
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenTheResponseIsNotJson()
    {
        var messages = MaxioErrorParser.Parse("<html>502 Bad Gateway</html>");

        Assert.Equal(new[] { "<html>502 Bad Gateway</html>" }, messages);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyBody()
    {
        Assert.Empty(MaxioErrorParser.Parse(""));
        Assert.Empty(MaxioErrorParser.Parse(null));
    }
}
