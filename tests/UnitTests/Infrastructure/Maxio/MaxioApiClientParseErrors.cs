using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientParseErrors
{
    [Fact]
    public void ParsesArrayOfStringErrors()
    {
        var body = """{"errors":["No payment method was on file for the $299.00 balance"]}""";

        var errors = MaxioApiClient.ParseErrors(body);

        Assert.Single(errors);
        Assert.Contains("No payment method", errors[0]);
    }

    [Fact]
    public void ParsesObjectKeyedErrors()
    {
        var body = """{"errors":{"customer":"can't be blank"}}""";

        var errors = MaxioApiClient.ParseErrors(body);

        Assert.Contains("customer: can't be blank", errors);
    }

    [Fact]
    public void ParsesObjectWithArrayValues()
    {
        var body = """{"errors":{"email":["is invalid","is taken"]}}""";

        var errors = MaxioApiClient.ParseErrors(body);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.StartsWith("email:", e));
    }

    [Fact]
    public void ReturnsEmptyForNonErrorBody()
    {
        Assert.Empty(MaxioApiClient.ParseErrors(""));
        Assert.Empty(MaxioApiClient.ParseErrors("not json"));
        Assert.Empty(MaxioApiClient.ParseErrors("""{"subscription":{"id":1}}"""));
    }
}
