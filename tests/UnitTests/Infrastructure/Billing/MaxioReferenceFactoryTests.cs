using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class MaxioReferenceFactoryTests
{
    private static readonly MaxioOptions Options = new()
    {
        ApiKey = "test-not-a-secret",
        Subdomain = "site-one",
        ProductFamilyHandle = "family-one"
    };

    [Fact]
    public void ReferencesAreDeterministicAndDoNotExposeTheUserId()
    {
        var factory = new MaxioReferenceFactory(Options);

        var first = factory.Customer("private-user-id");
        var second = factory.Customer("private-user-id");

        Assert.Equal(first, second);
        Assert.DoesNotContain("private-user-id", first);
    }

    [Fact]
    public void ProductHandleScopesSubscriptionIdempotency()
    {
        var factory = new MaxioReferenceFactory(Options);

        Assert.NotEqual(
            factory.Subscription("user-one", "eshop-pro"),
            factory.Subscription("user-one", "basic-plan"));
    }

    [Fact]
    public void SiteAndCatalogScopeTheLedger()
    {
        var first = new MaxioReferenceFactory(Options);
        var second = new MaxioReferenceFactory(WithSite("site-two"));

        Assert.NotEqual(first.IntegrationScope, second.IntegrationScope);
        Assert.NotEqual(first.Customer("user-one"), second.Customer("user-one"));
    }

    private static MaxioOptions WithSite(string subdomain) => new()
    {
        ApiKey = Options.ApiKey,
        Subdomain = subdomain,
        ProductFamilyHandle = Options.ProductFamilyHandle
    };
}
