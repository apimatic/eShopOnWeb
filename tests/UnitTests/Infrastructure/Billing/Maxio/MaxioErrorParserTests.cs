using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioErrorParserTests
{
    [Fact]
    public void ReadsTheArrayFormMaxioReturnsFromCreateEndpoints()
    {
        var errors = MaxioErrorParser.Parse(
            "{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void ReadsTheStringForm()
    {
        var errors = MaxioErrorParser.Parse("{\"errors\":\"Something went wrong\"}");

        Assert.Equal(new[] { "Something went wrong" }, errors);
    }

    [Fact]
    public void FlattensTheFieldMapFormAndKeepsTheFieldName()
    {
        var errors = MaxioErrorParser.Parse(
            "{\"errors\":{\"email\":[\"cannot be blank\",\"is invalid\"],\"last_name\":\"cannot be blank\"}}");

        Assert.Equal(
            new[] { "email: cannot be blank", "email: is invalid", "last_name: cannot be blank" },
            errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{\"message\":\"no errors member\"}")]
    public void DegradesToNoMessagesRatherThanThrowing(string? body)
    {
        Assert.Empty(MaxioErrorParser.Parse(body));
    }
}
