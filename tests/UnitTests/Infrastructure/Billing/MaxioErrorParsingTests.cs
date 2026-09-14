using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioErrorParsingTests
{
    [Fact]
    public void ReadsTheArrayFormTheApiUsesForMostFailures()
    {
        var errors = MaxioApiClient.ParseErrors("""{"errors":["Reference: must be unique - that value has been taken."]}""");

        Assert.Single(errors);
        Assert.Contains("must be unique", errors[0]);
    }

    [Fact]
    public void ReadsTheKeyedFormTheApiUsesForFieldFailures()
    {
        var errors = MaxioApiClient.ParseErrors("""{"errors":{"customer":"is invalid"}}""");

        Assert.Single(errors);
        Assert.Equal("customer: is invalid", errors[0]);
    }

    [Fact]
    public void PassesUnrecognisedBodiesThroughRatherThanSwallowingThem()
    {
        var errors = MaxioApiClient.ParseErrors("<html>gateway timeout</html>");

        Assert.Single(errors);
        Assert.Contains("gateway timeout", errors[0]);
    }

    [Fact]
    public void ReturnsNothingForAnEmptyBody()
    {
        Assert.Empty(MaxioApiClient.ParseErrors(null));
        Assert.Empty(MaxioApiClient.ParseErrors(string.Empty));
    }
}
