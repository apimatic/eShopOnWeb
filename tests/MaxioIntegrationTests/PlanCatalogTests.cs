using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Reading the plan catalog (UC1 step 1). The provider denominates product prices in cents; every
/// plan must leave the seam in dollars.
/// </summary>
public class PlanCatalogTests
{
    private static StubBillingServer FamilyResolved() => new StubBillingServer()
        .Get("product_families.json", BillingJson.ProductFamilyList((3026729, BillingTestHarness.ProductFamilyHandle)));

    [Fact]
    public async Task Lists_plans_converting_the_price_from_cents_to_dollars()
    {
        var server = FamilyResolved()
            .Get("/products.json", BillingJson.ProductList(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900),
                BillingJson.Product(7130996, "basic-plan", "Basic Plan", 2900)));

        var plans = (await BillingTestHarness.Build(server).ListPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("month", pro.BillingPeriodDescription);
        Assert.False(pro.RequiresPaymentMethod);

        // $29.00, not $2900.00 and not $0.29 — the magnitude must survive the round trip.
        Assert.Equal(29.00m, plans.Single(p => p.Handle == "basic-plan").Price);
    }

    [Fact]
    public async Task Returns_an_empty_collection_when_the_family_holds_no_plans()
    {
        var server = FamilyResolved().Get("/products.json", "[]");

        var plans = await BillingTestHarness.Build(server).ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task Excludes_archived_plans_from_the_catalog()
    {
        var server = FamilyResolved()
            .Get("/products.json", BillingJson.ProductList(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900),
                BillingJson.Product(7130997, "retired-plan", "Retired", 900, archivedAt: "2026-01-01T00:00:00-05:00")));

        var plans = await BillingTestHarness.Build(server).ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task Reports_an_unresolvable_family_handle_as_a_configuration_fault()
    {
        var server = new StubBillingServer()
            .Get("product_families.json", BillingJson.ProductFamilyList((999, "some-other-family")));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).ListPlansAsync());

        Assert.Contains(BillingTestHarness.ProductFamilyHandle, exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_a_single_plan_by_its_durable_handle()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900)));

        var plan = await BillingTestHarness.Build(server).GetPlanAsync("eshop-pro");

        Assert.Equal(7130995, plan.Id);
        Assert.Equal(299.00m, plan.Price);
        Assert.Contains("eshop-pro", server.RequestsFor("products/handle").Single().Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_an_unknown_plan_handle_as_a_configuration_fault()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.NotFound(), HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetPlanAsync("no-such-plan"));

        Assert.Contains("no-such-plan", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_plan_that_belongs_to_a_different_product_family()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900, familyHandle: "someone-elses-family")));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetPlanAsync("eshop-pro"));

        Assert.Contains("someone-elses-family", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_archived_plan()
    {
        var server = new StubBillingServer()
            .Get("products/handle", BillingJson.ProductEnvelope(
                BillingJson.Product(7130995, "eshop-pro", "Pro Plan", 29900, archivedAt: "2026-01-01T00:00:00-05:00")));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingTestHarness.Build(server).GetPlanAsync("eshop-pro"));
    }

    [Fact]
    public async Task Surfaces_a_provider_failure_reading_the_catalog_as_a_typed_billing_exception()
    {
        var server = new StubBillingServer()
            .Get("products/handle", """{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).GetPlanAsync("eshop-pro"));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task Surfaces_an_unreachable_provider_as_billing_unavailable()
    {
        var client = BillingTestHarness.Build(new UnreachableBillingServer());

        var exception = await Assert.ThrowsAsync<BillingUnavailableException>(() => client.GetPlanAsync("eshop-pro"));

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
