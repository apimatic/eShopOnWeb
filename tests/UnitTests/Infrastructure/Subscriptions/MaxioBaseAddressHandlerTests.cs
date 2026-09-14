using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

/// <summary>
/// <c>Maxio:BaseUrl</c> has to be honoured verbatim, including a non-default port and a path prefix,
/// while leaving the resource path and query string the SDK produced untouched.
/// </summary>
public class MaxioBaseAddressHandlerTests
{
    [Theory]
    [InlineData(
        "https://acme.chargify.com/subscriptions.json",
        "https://localhost:8443",
        "https://localhost:8443/subscriptions.json")]
    [InlineData(
        "https://acme.chargify.com/customers/lookup.json?reference=abc",
        "https://maxio.internal",
        "https://maxio.internal/customers/lookup.json?reference=abc")]
    [InlineData(
        "https://acme.chargify.com/product_families/1/products.json?per_page=200",
        "https://gateway.internal/maxio",
        "https://gateway.internal/maxio/product_families/1/products.json?per_page=200")]
    [InlineData(
        "https://acme.chargify.com/site.json",
        "https://gateway.internal/maxio/",
        "https://gateway.internal/maxio/site.json")]
    [InlineData(
        "https://acme.chargify.com/site.json",
        "http://localhost:3000",
        "http://localhost:3000/site.json")]
    public void RebasePointsTheRequestAtTheConfiguredAddress(string requestUri, string baseUrl, string expected)
    {
        var rebased = MaxioBaseAddressHandler.Rebase(new Uri(requestUri), new Uri(baseUrl));

        Assert.Equal(expected, rebased.ToString());
    }

    [Fact]
    public void RebaseKeepsTheDefaultPortImplicit()
    {
        var rebased = MaxioBaseAddressHandler.Rebase(
            new Uri("https://acme.chargify.com/site.json"),
            new Uri("https://maxio.internal"));

        Assert.True(rebased.IsDefaultPort);
        Assert.DoesNotContain(":443", rebased.ToString());
    }
}
