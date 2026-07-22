using System.Text;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class TargetServerAndAuthentication
{
    [Fact]
    public async Task SendsTheApiKeyAsBasicCredentialsWithThePasswordX()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.Build().ListPlansAsync();

        var request = builder.Handler.LastRequest;
        Assert.Equal("Basic", request.AuthScheme);

        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.AuthParameter!));
        Assert.Equal("test-api-key:x", decoded);
    }

    [Fact]
    public async Task TargetsTheExplicitBaseUrlWhenOneIsConfigured()
    {
        var builder = new MaxioClientBuilder().WithBaseUrl("http://localhost:8080");
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.Build().ListPlansAsync();

        Assert.Equal("http://localhost:8080/", builder.Handler.LastRequest.Uri.GetLeftPart(UriPartial.Authority) + "/");
        Assert.StartsWith("http://localhost:8080/", builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task TargetsTheSubdomainDerivedHostWhenNoOverrideIsConfigured()
    {
        var builder = new MaxioClientBuilder().WithBaseUrl(null);
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.Build().ListPlansAsync();

        Assert.StartsWith("https://test-site.chargify.com/", builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task ResolvesItsOwnBaseAddressWhenTheHostDidNotPresetOne()
    {
        var builder = new MaxioClientBuilder().WithBaseUrl("http://localhost:9999");
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.BuildWithoutPresetBaseAddress().ListPlansAsync();

        Assert.StartsWith("http://localhost:9999/", builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task ScopesThePlanListingToTheConfiguredProductFamilyHandle()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.Build().ListPlansAsync();

        Assert.Contains("product_families/handle:eshop-subscribe/products.json",
            builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task FallsBackToTheSiteWideProductListWhenNoFamilyIsConfigured()
    {
        var builder = new MaxioClientBuilder().WithProductFamilyHandle(null);
        builder.Handler.RespondWithOk("products.json", MaxioJson.ProductList());

        await builder.Build().ListPlansAsync();

        Assert.EndsWith("/products.json", builder.Handler.LastRequest.Uri.AbsolutePath);
        Assert.DoesNotContain("product_families", builder.Handler.LastRequest.Uri.ToString());
    }
}
