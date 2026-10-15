using System.Net;
using System.Text.Json.Nodes;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests;

public class SubscriptionPlanTests
{
    private const string ListProducts = "GET /product_families/{family}/products.json";

    [Fact]
    public async Task ListsOfferedPlansAndSkipsArchivedOnes()
    {
        var maxio = FakeMaxio.WithDefaultCatalog();
        maxio.AddProduct("old-plan", "Old", 100, archived: true);
        var harness = new BillingHarness(maxio);

        var catalog = await harness.NewService().ListPlansAsync();

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task RequestsTheConfiguredFamilyByHandle()
    {
        var harness = new BillingHarness();

        await harness.NewService().ListPlansAsync();

        Assert.Equal(1, harness.Maxio.Count(ListProducts));
    }

    [Fact]
    public async Task CachesTheCatalog()
    {
        var harness = new BillingHarness();

        await harness.NewService().ListPlansAsync();
        await harness.NewService().ListPlansAsync();

        Assert.Equal(1, harness.Maxio.Count(ListProducts));
    }

    [Fact]
    public async Task WalksPagesUntilAShortPage()
    {
        var maxio = new FakeMaxio();
        for (var i = 0; i < 250; i++)
        {
            maxio.AddProduct($"plan-{i}", $"Plan {i}", 100);
        }
        var harness = new BillingHarness(maxio);

        var catalog = await harness.NewService().ListPlansAsync();

        Assert.Equal(250, catalog.Plans.Count);
        Assert.False(catalog.IsTruncated);
        Assert.Equal(2, maxio.Count(ListProducts));
    }

    [Fact]
    public async Task FlagsTheCatalogAsTruncatedAtThePageCap()
    {
        var maxio = new FakeMaxio();
        for (var i = 0; i < 450; i++)
        {
            maxio.AddProduct($"plan-{i}", $"Plan {i}", 100);
        }
        var harness = new BillingHarness(maxio, s => s.MaxPlanPages = 2);

        var catalog = await harness.NewService().ListPlansAsync();

        Assert.True(catalog.IsTruncated);
        Assert.Equal(400, catalog.Plans.Count);
        Assert.Equal(2, maxio.Count(ListProducts));
    }

    [Fact]
    public async Task UnknownProductFamilyIsAProviderError()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, call, _) => call == ListProducts ? FakeMaxio.Json(HttpStatusCode.NotFound, JsonValue.Create("Not Found")!) : null;

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingErrorKind.ProviderError, ex.Kind);
        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task RejectedCredentialsAreUnavailableNotTheCallersFault()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, _, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("HTTP Basic: Access denied.") };

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);
        Assert.Equal(401, ex.ProviderStatusCode);
        Assert.DoesNotContain("maxio.test", ex.Message);
    }

    [Fact]
    public async Task UnreadableSuccessBodyIsAProviderError()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, _, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json") };

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingErrorKind.ProviderError, ex.Kind);
    }

    [Fact]
    public async Task UnreachableProviderIsUnavailable()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, _, _) => throw new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().ListPlansAsync());

        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);
    }
}
