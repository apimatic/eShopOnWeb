using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

public class MaxioOptionsTests
{
    [Fact]
    public void DerivesTheUsServerFromTheSubdomain()
    {
        var options = Configured();

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DerivesTheEuServerFromTheSubdomain()
    {
        var options = Configured();
        options.Environment = MaxioOptions.EuEnvironment;

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesAnExplicitBaseUrlVerbatimAndIgnoresTheSubdomain()
    {
        var options = Configured();
        options.BaseUrl = "https://maxio.test.internal/v1";

        Assert.Equal("https://maxio.test.internal/v1/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AnExplicitBaseUrlRemovesTheNeedForASubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://maxio.test.internal/"
        };

        Assert.Empty(options.Validate());
        Assert.Equal("https://maxio.test.internal/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ReportsEveryMissingSetting()
    {
        var failures = new MaxioOptions().Validate();

        Assert.Contains(failures, f => f.Contains("ApiKey"));
        Assert.Contains(failures, f => f.Contains("Subdomain"));
        Assert.Contains(failures, f => f.Contains("ProductFamilyHandle"));
    }

    [Fact]
    public void RejectsAnUnknownEnvironment()
    {
        var options = Configured();
        options.Environment = "APAC";

        Assert.Contains(options.Validate(), f => f.Contains("Environment"));
    }

    [Fact]
    public void RejectsACollectionMethodThatIsNotInTheSpecEnum()
    {
        var options = Configured();
        options.PaymentCollectionMethod = "cheque";

        Assert.Contains(options.Validate(), f => f.Contains("PaymentCollectionMethod"));
    }

    [Fact]
    public void AcceptsEveryCollectionMethodTheSpecDefines()
    {
        Assert.All(MaxioOptions.CollectionMethods, method =>
        {
            var options = Configured();
            options.PaymentCollectionMethod = method;
            Assert.Empty(options.Validate());
        });
    }

    private static MaxioOptions Configured() => new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };
}
