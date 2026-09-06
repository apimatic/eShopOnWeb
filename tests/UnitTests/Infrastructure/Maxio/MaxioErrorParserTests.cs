using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioErrorParserTests
{
    [Fact]
    public void ReadsTheErrorArrayShape()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":["Reference: must be unique - that value has been taken."]}""");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void ReadsTheCustomerErrorShapeAndKeepsTheFieldName()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":{"customer":"Customer must exist"}}""");

        Assert.Equal(new[] { "customer: Customer must exist" }, errors);
    }

    [Fact]
    public void ReadsTheErrorArrayMapShape()
    {
        var errors = MaxioErrorParser.Parse("""{"errors":{"email":["is invalid","is too long"]}}""");

        Assert.Equal(new[] { "email: is invalid", "email: is too long" }, errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("""{"message":"nope"}""")]
    public void ReturnsNothingWhenThereIsNoStructuredDetail(string? body)
    {
        Assert.Empty(MaxioErrorParser.Parse(body));
    }
}
