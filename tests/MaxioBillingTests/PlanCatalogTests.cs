using System.Net;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class PlanCatalogTests
{
    [Fact]
    public async Task Lists_non_archived_plans_of_the_family_by_handle()
    {
        var fake = new FakeMaxio
        {
            Products = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK,
                Wire.Products((1, "eshop-pro", 29900, false), (2, "basic-plan", 2900, false), (3, "retired", 100, true)))),
        };
        var harness = new Harness(fake.CreateHandler());

        var catalog = await harness.NewCatalog().GetPlansAsync(CancellationToken.None);

        Assert.False(catalog.Truncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);

        var request = Assert.Single(harness.Stub.Requests);
        Assert.Contains("handle", request.Uri.AbsolutePath);
        Assert.Contains(Harness.FamilyHandle, request.Uri.AbsolutePath);
        Assert.Contains("per_page=200", request.Uri.Query);
        Assert.Contains("page=1", request.Uri.Query);
    }

    [Fact]
    public async Task Falls_back_to_the_numeric_family_id_when_the_handle_form_is_not_found()
    {
        var fake = new FakeMaxio
        {
            Products = r => Task.FromResult(r.Uri.AbsolutePath == "/product_families/3023074/products.json"
                ? StubHandler.Json(HttpStatusCode.OK, Wire.StandardPlans())
                : StubHandler.Json(HttpStatusCode.NotFound, "\"Not Found\"")),
            Families = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK,
                """[{"product_family":{"id":1,"handle":"other"}},{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]""")),
        };
        var harness = new Harness(fake.CreateHandler());
        var catalog = harness.NewCatalog();

        var plans = await catalog.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Plans.Count);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Get, "/product_families.json"));

        // The resolved id is cached: the next call goes straight to it.
        await catalog.GetPlansAsync(CancellationToken.None);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Get, "/product_families.json"));
        Assert.Equal(2, harness.Stub.Count(HttpMethod.Get, "/product_families/3023074/products.json"));
    }

    [Fact]
    public async Task A_full_page_at_the_page_cap_is_reported_as_truncated()
    {
        var fullPage = Wire.Products(Enumerable.Range(1, MaxioPlanCatalog.PageSize).Select(i => (i, $"plan-{i}", 100L, false)).ToArray());
        var fake = new FakeMaxio { Products = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, fullPage)) };
        var harness = new Harness(fake.CreateHandler());

        var catalog = await harness.NewCatalog().GetPlansAsync(CancellationToken.None);

        Assert.True(catalog.Truncated);
        Assert.Equal(MaxioPlanCatalog.MaxPages, harness.Stub.Requests.Count);
    }

    [Fact]
    public async Task Paging_stops_at_the_first_short_page()
    {
        var fullPage = Wire.Products(Enumerable.Range(1, MaxioPlanCatalog.PageSize).Select(i => (i, $"plan-{i}", 100L, false)).ToArray());
        var fake = new FakeMaxio
        {
            Products = FakeMaxio.Sequence(
                _ => StubHandler.Json(HttpStatusCode.OK, fullPage),
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.StandardPlans())),
        };
        var harness = new Harness(fake.CreateHandler());

        var catalog = await harness.NewCatalog().GetPlansAsync(CancellationToken.None);

        Assert.False(catalog.Truncated);
        Assert.Equal(MaxioPlanCatalog.PageSize + 2, catalog.Plans.Count);
        Assert.Equal(2, harness.Stub.Requests.Count);
        Assert.Contains("page=2", harness.Stub.Requests[1].Uri.Query);
    }
}
