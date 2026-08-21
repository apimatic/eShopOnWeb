using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingGatewayTests
{
    [Fact]
    public void ResolveBaseAddress_UsesSubdomainWhenBaseUrlOmitted()
    {
        var uri = MaxioAdvancedBillingGateway.ResolveBaseAddress(new MaxioOptions { Subdomain = "my-site" });
        Assert.Equal("https://my-site.chargify.com/", uri.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatimWhenSet()
    {
        var uri = MaxioAdvancedBillingGateway.ResolveBaseAddress(new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://override.example.test/api"
        });
        Assert.Equal("https://override.example.test/api/", uri.ToString());
    }

    [Fact]
    public void ToHandlePath_PrefixesHandleOnce()
    {
        Assert.Equal("handle:eshop-subscribe", MaxioAdvancedBillingGateway.ToHandlePath("eshop-subscribe"));
        Assert.Equal("handle:eshop-subscribe", MaxioAdvancedBillingGateway.ToHandlePath("handle:eshop-subscribe"));
    }

    [Fact]
    public void ToMoney_ConvertsCentsToDecimal()
    {
        Assert.Equal(299.00m, MaxioAdvancedBillingGateway.ToMoney(29900));
        Assert.Equal(29.00m, MaxioAdvancedBillingGateway.ToMoney(2900));
    }

    [Fact]
    public void ExtractErrorMessage_JoinsArrayErrors()
    {
        var message = MaxioAdvancedBillingGateway.ExtractErrorMessage("""{"errors":["Name: cannot be blank.","Reference: must be unique."]}""");
        Assert.Contains("cannot be blank", message);
        Assert.Contains("must be unique", message);
    }
}
