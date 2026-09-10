using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioApiClientParseErrorsTests
{
    [Fact]
    public void ParseErrors_ReadsArrayShape()
    {
        var messages = MaxioApiClient.ParseErrors("{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");
        Assert.Single(messages);
        Assert.Contains("payment method", messages[0]);
    }

    [Fact]
    public void ParseErrors_ReadsObjectShape()
    {
        var messages = MaxioApiClient.ParseErrors("{\"errors\":{\"customer\":\"can't be blank\"}}");
        Assert.Single(messages);
        Assert.Equal("customer: can't be blank", messages[0]);
    }

    [Fact]
    public void ParseErrors_ReadsStringShape()
    {
        var messages = MaxioApiClient.ParseErrors("{\"errors\":\"A valid product_family_id is required\"}");
        Assert.Single(messages);
        Assert.Equal("A valid product_family_id is required", messages[0]);
    }

    [Fact]
    public void ParseErrors_ReturnsEmpty_ForNonJsonOrMissing()
    {
        Assert.Empty(MaxioApiClient.ParseErrors("not json"));
        Assert.Empty(MaxioApiClient.ParseErrors("{\"foo\":\"bar\"}"));
        Assert.Empty(MaxioApiClient.ParseErrors(""));
    }
}
