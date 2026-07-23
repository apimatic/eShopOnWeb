using System.Text;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

public class AuthenticationAndTargetingTests
{
    [Fact]
    public async Task AuthenticatesWithBasicUsingTheApiKeyAsUsernameAndLiteralXAsPassword()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.Build(handler);

        await client.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.AuthenticationScheme);

        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.AuthenticationParameter!));
        Assert.Equal($"{BillingClientBuilder.TestApiKey}:x", decoded);
    }

    [Fact]
    public async Task SendsRequestsToTheConfiguredMockTargetWhenABaseUrlIsSet()
    {
        var settings = BillingClientBuilder.DefaultSettings();
        settings.BaseUrl = "http://localhost:8080";

        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.BuildWithoutBaseAddress(handler, settings);

        await client.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:8080/product_families/handle:eshop-subscribe/products.json",
            request.Uri.ToString());
    }

    [Fact]
    public async Task DerivesTheTargetFromTheSubdomainWhenNoBaseUrlIsSet()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ProductsInFamily);
        var client = BillingClientBuilder.BuildWithoutBaseAddress(handler);

        await client.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("example-site.chargify.com", request.Uri.Host);
    }

    [Fact]
    public void ExposesTheConfiguredMeteredComponentHandleToTheDomain()
    {
        var client = BillingClientBuilder.Build(new StubHttpMessageHandler());

        Assert.Equal(BillingClientBuilder.TestComponentHandle, client.MeteredComponentHandle);
    }
}
