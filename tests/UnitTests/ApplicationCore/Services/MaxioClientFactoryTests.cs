using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioClientFactoryTests
{
    [Theory]
    [InlineData(null, "site", "family")]
    [InlineData("key", null, "family")]
    [InlineData("key", "site", null)]
    public void ThrowsWhenRequiredSettingMissing(string? apiKey, string? subdomain, string? family)
    {
        var settings = new MaxioSettings { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = family };

        Assert.Throws<BillingNotConfiguredException>(() => MaxioClientFactory.Create(new HttpClient(), settings));
    }

    [Fact]
    public void AcceptsBaseUrlInPlaceOfSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "http://localhost:1", ProductFamilyHandle = "family" };

        Assert.NotNull(MaxioClientFactory.Create(new HttpClient(), settings));
    }
}
