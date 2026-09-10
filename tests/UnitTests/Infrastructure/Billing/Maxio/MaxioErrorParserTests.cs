using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioErrorParserTests
{
    [Fact]
    public void ParsesArrayOfStrings()
    {
        var messages = MaxioErrorParser.Parse("{\"errors\":[\"First name: cannot be blank.\",\"Email: cannot be blank.\"]}");

        Assert.Equal(2, messages.Count);
        Assert.Contains("First name: cannot be blank.", messages);
        Assert.Contains("Email: cannot be blank.", messages);
    }

    [Fact]
    public void ParsesObjectOfFieldMessages()
    {
        var messages = MaxioErrorParser.Parse("{\"errors\":{\"customer\":\"can't be blank\"}}");

        Assert.Single(messages);
        Assert.Equal("customer: can't be blank", messages[0]);
    }

    [Fact]
    public void ParsesBareString()
    {
        var messages = MaxioErrorParser.Parse("{\"errors\":\"something went wrong\"}");

        Assert.Single(messages);
        Assert.Equal("something went wrong", messages[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("{\"unrelated\":true}")]
    public void ReturnsEmptyForUnparseableOrAbsentErrors(string? body)
    {
        var messages = MaxioErrorParser.Parse(body);

        Assert.Empty(messages);
    }
}
