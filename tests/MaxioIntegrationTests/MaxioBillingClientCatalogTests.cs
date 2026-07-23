using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Catalog reads through the provider seam: plans, plan lookup by handle, and the metered component —
/// including the money conversions, which are the easiest thing to get silently wrong.
/// </summary>
public class MaxioBillingClientCatalogTests
{
    [Fact]
    public async Task ListPlansAsync_ReturnsThePlans_WithPricesConvertedFromCentsToDollars()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((3026728, "eshop-subscribe")))
            .EnqueueOk(MaxioPayloads.ProductList(
                MaxioPayloads.Product(7130993, "eshop-pro", "Pro Plan", 29_900),
                MaxioPayloads.Product(7130994, "basic-plan", "Basic Plan", 2_900)));

        var (client, _) = TestClientFactory.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29_900L, pro.PriceInCents);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.Equal("$ 299.00 / month", pro.BillingDescription);

        var basic = Assert.Single(plans, plan => plan.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);
        Assert.Equal("$ 29.00 / month", basic.BillingDescription);
    }

    [Fact]
    public async Task ListPlansAsync_TargetsTheConfiguredBaseUrl_AndAuthenticatesWithTheApiKey()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((3026728, "eshop-subscribe")))
            .EnqueueOk(MaxioPayloads.ProductList(MaxioPayloads.Product()));

        var (client, _) = TestClientFactory.Create(handler);

        await client.ListPlansAsync();

        Assert.All(handler.Requests, request =>
        {
            // Proves Maxio:BaseUrl is honored: nothing leaked to the subdomain-derived production host.
            Assert.Equal("localhost", request.Uri.Host);
            Assert.Equal(8080, request.Uri.Port);
            Assert.Equal("Basic", request.AuthorizationScheme);
            Assert.Equal(TestClientFactory.ApiKey, request.BasicAuthUserName);
        });
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedPlans_SoARetiredPlanCannotBeSubscribedTo()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((3026728, "eshop-subscribe")))
            .EnqueueOk(MaxioPayloads.ProductList(
                MaxioPayloads.Product(7130993, "eshop-pro", "Pro Plan", 29_900),
                MaxioPayloads.Product(7100000, "retired-plan", "Retired", 100, archivedAt: "2026-01-01T00:00:00-05:00")));

        var (client, _) = TestClientFactory.Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsAnEmptyList_WhenTheFamilyHasNoProducts()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((3026728, "eshop-subscribe")))
            .EnqueueOk("[]");

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Empty(await client.ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansAsync_ResolvesTheFamilyByHandle_NotByTheConfiguredId()
    {
        // The live id differs from the configured one — handles are durable, ids are reassigned on a
        // re-seed, so the handle must win.
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((4_444_444, "eshop-subscribe")))
            .EnqueueOk(MaxioPayloads.ProductList(MaxioPayloads.Product()));

        var (client, _) = TestClientFactory.Create(handler);

        await client.ListPlansAsync();

        Assert.Contains("4444444", handler.LastRequest.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("3026728", handler.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPlansAsync_FallsBackToTheConfiguredId_WhenTheHandleIsNotOnTheSite()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((999, "some-other-family")))
            .EnqueueOk(MaxioPayloads.ProductList(MaxioPayloads.Product()));

        var (client, logger) = TestClientFactory.Create(handler);

        await client.ListPlansAsync();

        Assert.Contains("3026728", handler.LastRequest.Path, StringComparison.Ordinal);
        Assert.Contains(logger.Warnings, warning => warning.Contains("did not resolve", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListPlansAsync_Throws_WhenNeitherTheHandleNorAConfiguredIdResolves()
    {
        var settings = TestClientFactory.Settings();
        settings.ProductFamilyId = null;

        var handler = new FakeMaxioHandler().EnqueueOk(MaxioPayloads.ProductFamilies((999, "other")));
        var (client, _) = TestClientFactory.Create(handler, settings);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindPlanAsync_ReturnsThePlan_WhenTheHandleResolves()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductResponse(MaxioPayloads.Product()));

        var (client, _) = TestClientFactory.Create(handler);

        var plan = await client.FindPlanAsync("eshop-pro");

        Assert.NotNull(plan);
        Assert.Equal("eshop-pro", plan!.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("eshop-subscribe", plan.ProductFamilyHandle);
    }

    [Fact]
    public async Task FindPlanAsync_ReturnsNull_ForAnUnknownHandle()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"Product not found"}""");

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Null(await client.FindPlanAsync("does-not-exist"));
    }

    [Fact]
    public async Task FindPlanAsync_ReturnsNull_ForABlankHandle_WithoutCallingTheProvider()
    {
        var handler = new FakeMaxioHandler();
        var (client, _) = TestClientFactory.Create(handler);

        Assert.Null(await client.FindPlanAsync("   "));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindPlanAsync_ReturnsNull_WhenThePlanBelongsToAnotherProductFamily()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductResponse(
                MaxioPayloads.Product(handle: "someone-elses-plan", familyHandle: "unrelated-family")));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Null(await client.FindPlanAsync("someone-elses-plan"));
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ConvertsTheDollarUnitPriceIntoWholeCents()
    {
        // The live site reports unit_price as the dollar string "0.01" with no *_in_cents sibling.
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ComponentResponse(unitPrice: "0.01", pricePerUnitInCents: null));

        var (client, _) = TestClientFactory.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.Equal(1L, component.UnitPriceInCents);
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Theory]
    [InlineData("0.01", 1L)]
    [InlineData("1.00", 100L)]
    [InlineData("2.5", 250L)]
    [InlineData("12.345", 1235L)]
    public async Task GetMeteredComponentAsync_RoundsDollarStringsToCentsCorrectly(string dollars, long expectedCents)
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ComponentResponse(unitPrice: dollars, pricePerUnitInCents: null));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(expectedCents, (await client.GetMeteredComponentAsync()).UnitPriceInCents);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_PrefersTheCentsField_WhenTheProviderSuppliesBoth()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ComponentResponse(unitPrice: "0.01", pricePerUnitInCents: 3L));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(3L, (await client.GetMeteredComponentAsync()).UnitPriceInCents);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ReportsNotMetered_ForAQuantityBasedComponent()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ComponentResponse(kind: "quantity_based_component"));

        var (client, _) = TestClientFactory.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.False(component.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ThrowsAConfigurationError_WhenTheHandleDoesNotResolve()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"Component not found"}""");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.GetMeteredComponentAsync());

        Assert.Contains("api-call", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ResolvesOncePerInstance_SoRepeatedUseCostsOneCall()
    {
        var handler = new FakeMaxioHandler().EnqueueOk(MaxioPayloads.ComponentResponse());
        var (client, _) = TestClientFactory.Create(handler);

        await client.GetMeteredComponentAsync();
        await client.GetMeteredComponentAsync();

        Assert.Single(handler.Requests);
    }
}
