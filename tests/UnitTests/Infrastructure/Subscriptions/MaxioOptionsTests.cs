using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "plans",
    };

    [Fact]
    public void ValidOptionsProduceNoErrors()
    {
        Assert.Empty(Valid().Validate());
        Assert.True(Valid().IsConfigured);
    }

    [Fact]
    public void EveryMissingRequiredValueIsReportedAtOnce()
    {
        var errors = new MaxioOptions().Validate();

        Assert.Contains(errors, error => error.Contains("Maxio:ApiKey"));
        Assert.Contains(errors, error => error.Contains("Maxio:Subdomain"));
        Assert.Contains(errors, error => error.Contains("Maxio:ProductFamilyHandle"));
        Assert.False(new MaxioOptions().IsConfigured);
    }

    [Fact]
    public void BaseUrlIsOptionalButMustBeAbsoluteWhenSet()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:BaseUrl"));

        options.BaseUrl = "https://maxio.internal";
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("US")]
    [InlineData("eu")]
    public void KnownEnvironmentsAreAccepted(string environment)
    {
        var options = Valid();
        options.Environment = environment;

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void UnknownEnvironmentIsRejected()
    {
        var options = Valid();
        options.Environment = "APAC";

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:Environment"));
    }

    [Fact]
    public void TimeoutMustBePositive()
    {
        var options = Valid();
        options.Timeout = TimeSpan.Zero;

        Assert.Contains(options.Validate(), error => error.Contains("Maxio:Timeout"));
    }
}
