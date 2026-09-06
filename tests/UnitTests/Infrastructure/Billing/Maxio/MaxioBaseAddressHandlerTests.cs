using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioBaseAddressHandlerTests
{
    private static string Rebase(string original, string baseAddress) =>
        MaxioBaseAddressHandler.Rebase(new Uri(original), new Uri(baseAddress)).ToString();

    [Fact]
    public void MovesTheRequestToTheConfiguredHost()
    {
        Assert.Equal(
            "https://gateway.internal/subscriptions.json",
            Rebase("https://acme.chargify.com/subscriptions.json", "https://gateway.internal/"));
    }

    [Fact]
    public void KeepsAPathPrefixOnTheConfiguredBaseAddress()
    {
        Assert.Equal(
            "https://gateway.internal/billing/subscriptions.json",
            Rebase("https://acme.chargify.com/subscriptions.json", "https://gateway.internal/billing/"));
    }

    [Fact]
    public void KeepsTheQueryString()
    {
        Assert.Equal(
            "https://gateway.internal/customers/lookup.json?reference=abc",
            Rebase("https://acme.chargify.com/customers/lookup.json?reference=abc", "https://gateway.internal/"));
    }

    [Fact]
    public void KeepsNestedPathSegments()
    {
        Assert.Equal(
            "https://gateway.internal/product_families/handle:plans/products.json",
            Rebase("https://acme.chargify.com/product_families/handle:plans/products.json", "https://gateway.internal/"));
    }

    [Fact]
    public void KeepsANonDefaultPort()
    {
        Assert.Equal(
            "http://localhost:8080/site.json",
            Rebase("https://acme.chargify.com/site.json", "http://localhost:8080/"));
    }

    [Fact]
    public void IsANoOpWhenTheBaseAddressAlreadyMatches()
    {
        Assert.Equal(
            "https://acme.chargify.com/subscriptions.json",
            Rebase("https://acme.chargify.com/subscriptions.json", "https://acme.chargify.com/"));
    }
}
