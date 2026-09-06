using Microsoft.eShopWeb.Infrastructure.Maxio;

using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void SubstitutesTheSubdomainIntoTheSpecificationServerTemplate()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesAnExplicitBaseUrlVerbatim()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", BaseUrl = "https://acme.ebilling.maxio.com" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void KeepsAPathOnAnExplicitBaseUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "https://gateway.example.com/maxio" };

        Assert.Equal("https://gateway.example.com/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var settings = new MaxioSettings { BaseUrl = "not-a-url" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }

    [Fact]
    public void RequiresASubdomainWhenNoBaseUrlIsSet()
    {
        Assert.Throws<InvalidOperationException>(() => new MaxioSettings().ResolveBaseAddress());
    }
}
