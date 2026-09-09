using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class MaxioApiExceptionTests
{
    [Fact]
    public void ParseErrors_ReadsErrorsArray()
    {
        var messages = MaxioApiException.ParseErrors("{\"errors\":[\"First name: cannot be blank.\",\"Email: is invalid.\"]}");

        Assert.Equal(2, messages.Count);
        Assert.Contains("First name: cannot be blank.", messages);
    }

    [Fact]
    public void ParseErrors_ReadsErrorsObjectMap()
    {
        var messages = MaxioApiException.ParseErrors("{\"errors\":{\"customer\":\"can't be blank\"}}");

        Assert.Single(messages);
        Assert.Contains("customer: can't be blank", messages);
    }

    [Fact]
    public void ParseErrors_ReadsBareString()
    {
        var messages = MaxioApiException.ParseErrors("\"A valid product_family_id is required\"");

        Assert.Contains("A valid product_family_id is required", messages);
    }

    [Fact]
    public void ParseErrors_ToleratesNonJson()
    {
        var messages = MaxioApiException.ParseErrors("<html>502 Bad Gateway</html>");

        Assert.Single(messages);
        Assert.Contains("502", messages.First());
    }

    [Fact]
    public void ParseErrors_ReturnsEmpty_ForEmptyBody()
    {
        Assert.Empty(MaxioApiException.ParseErrors(""));
        Assert.Empty(MaxioApiException.ParseErrors(null));
    }
}
