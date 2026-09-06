using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesTheUsHostFromTheSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData("https://acme.ebilling.maxio.com", "https://acme.ebilling.maxio.com/")]
    [InlineData("https://acme.ebilling.maxio.com/", "https://acme.ebilling.maxio.com/")]
    [InlineData("http://localhost:9099/maxio/", "http://localhost:9099/maxio/")]
    public void ResolveBaseAddress_UsesAnExplicitBaseUrlVerbatim(string baseUrl, string expected)
    {
        var options = new MaxioOptions { Subdomain = "ignored", BaseUrl = baseUrl };

        Assert.Equal(expected, options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Validate_AcceptsABaseUrlWithoutASubdomain()
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions
        {
            ApiKey = "key",
            BaseUrl = "https://acme.ebilling.maxio.com",
            ProductFamilyHandle = "demo-plans"
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null, "acme", "demo-plans", "Maxio:ApiKey")]
    [InlineData("key", null, "demo-plans", "Maxio:Subdomain")]
    [InlineData("key", "acme", null, "Maxio:ProductFamilyHandle")]
    public void Validate_NamesTheMissingSetting(string? apiKey, string? subdomain, string? familyHandle, string expectedSetting)
    {
        var result = new MaxioOptionsValidator().Validate(null, new MaxioOptions
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = familyHandle
        });

        Assert.True(result.Failed);
        Assert.Contains(expectedSetting, result.FailureMessage);
    }

    [Fact]
    public void CustomerReference_IsStableAndCaseInsensitiveForTheSameUser()
    {
        var lower = MaxioSubscriptionBillingService.CustomerReferenceFor(
            new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com"));
        var upper = MaxioSubscriptionBillingService.CustomerReferenceFor(
            new SubscriberIdentity(" DemoUser@Microsoft.com ", "demouser@microsoft.com"));

        Assert.Equal("eshoponweb-demouser@microsoft.com", lower);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void UniquenessToken_IsStablePerSubscriberAndPlan()
    {
        var first = MaxioSubscriptionBillingService.UniquenessToken("eshoponweb-a@b.com", "eshop-pro", null);
        var again = MaxioSubscriptionBillingService.UniquenessToken("eshoponweb-a@b.com", "eshop-pro", null);
        var otherPlan = MaxioSubscriptionBillingService.UniquenessToken("eshoponweb-a@b.com", "basic-plan", null);
        var otherUser = MaxioSubscriptionBillingService.UniquenessToken("eshoponweb-c@d.com", "eshop-pro", null);
        var keyed = MaxioSubscriptionBillingService.UniquenessToken("eshoponweb-a@b.com", "eshop-pro", "caller-key");

        Assert.Equal(first, again);
        Assert.NotEqual(first, otherPlan);
        Assert.NotEqual(first, otherUser);
        Assert.NotEqual(first, keyed);
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "Demouser", "Shopper")]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("jane_doe@example.com", "Jane", "Doe")]
    public void DeriveName_FillsTheNamesMaxioRequiresFromTheEmail(string email, string firstName, string lastName)
    {
        var (first, last) = MaxioSubscriptionBillingService.DeriveName(new SubscriberIdentity(email, email));

        Assert.Equal(firstName, first);
        Assert.Equal(lastName, last);
    }

    [Fact]
    public void DeriveName_PrefersNamesTheCallerSupplied()
    {
        var (first, last) = MaxioSubscriptionBillingService.DeriveName(
            new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com", "Grace", "Hopper"));

        Assert.Equal("Grace", first);
        Assert.Equal("Hopper", last);
    }

    [Theory]
    [InlineData("""{"errors":["Reference: must be unique - that value has been taken."]}""", "Reference: must be unique - that value has been taken.")]
    [InlineData("""{"errors":{"customer":"is invalid"}}""", "is invalid")]
    [InlineData("""{"error":"something broke"}""", "something broke")]
    public void ParseErrors_UnderstandsEveryShapeMaxioUses(string body, string expected)
    {
        Assert.Contains(expected, MaxioApiClient.ParseErrors(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void ParseErrors_IsQuietWhenThereIsNothingToParse(string body)
    {
        Assert.Empty(MaxioApiClient.ParseErrors(body));
    }
}
