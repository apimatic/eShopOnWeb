using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Reading the plan catalogue and the metered component: mapping, money magnitude, empty and
/// unknown results, and the configuration failures that must stop usage being recorded.
/// </summary>
public class MaxioBillingClientCatalogTests
{
    [Fact]
    public async Task ListPlansAsync_TargetsTheConfiguredBaseUrlRatherThanTheSubdomainHost()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.Subdomain = "some-other-site")
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(MaxioResponses.ProductBody()));

        await builder.Build().ListPlansAsync();

        var uri = builder.Handler.Requests[0].Uri;

        Assert.Equal("http", uri.Scheme);
        Assert.Equal("localhost", uri.Host);
        Assert.Equal(8080, uri.Port);
    }

    [Fact]
    public async Task ListPlansAsync_SendsTheApiKeyAsBasicAuthentication()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(MaxioResponses.ProductBody()));

        await builder.Build().ListPlansAsync();

        var parameter = builder.Handler.Requests[0].AuthorizationParameter;
        Assert.NotNull(parameter);

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parameter!));
        Assert.Equal("test-api-key:x", decoded);
    }

    [Fact]
    public async Task ListPlansAsync_MapsPricesFromCentsWithoutLosingMagnitude()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(
                MaxioResponses.ProductBody(id: 7126957, handle: "eshop-pro", name: "Pro Plan", priceInCents: 29900)));

        var plans = await builder.Build().ListPlansAsync();

        var plan = Assert.Single(plans);

        Assert.Equal(7126957, plan.Id);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);

        // $299.00 is 29,900 cents — not 299 and not 2,990,000.
        Assert.Equal(29900L, plan.PriceInCents);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal("month", plan.BillingPeriod);
        Assert.False(plan.RequiresPaymentMethod);
        Assert.False(plan.IsArchived);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsTheCheapestPlanFirst()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(
                MaxioResponses.ProductBody(id: 1, handle: "eshop-pro", name: "Pro Plan", priceInCents: 29900),
                MaxioResponses.ProductBody(id: 2, handle: "basic-plan", name: "Basic Plan", priceInCents: 2900)));

        var plans = await builder.Build().ListPlansAsync();

        Assert.Collection(
            plans,
            plan => Assert.Equal("basic-plan", plan.Handle),
            plan => Assert.Equal("eshop-pro", plan.Handle));

        Assert.Equal(29.00m, plans.First().Price);
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedPlansFromTheSubscribableCatalogue()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(
                MaxioResponses.ProductBody(id: 1, handle: "live-plan", priceInCents: 2900),
                MaxioResponses.ProductBody(id: 2, handle: "retired-plan", priceInCents: 900, archivedAt: "2025-01-01T00:00:00Z")));

        var plans = await builder.Build().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("live-plan", plan.Handle);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsAnEmptyCollectionWhenTheFamilyHoldsNoPlans()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson("[]");

        var plans = await builder.Build().ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlansAsync_FailsWithAConfigurationErrorWhenTheFamilyHandleDoesNotResolve()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.ProductFamilyList(999, "some-other-family"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().ListPlansAsync());

        Assert.Contains(BillingClientBuilder.ProductFamilyHandle, exception.Message);
    }

    [Fact]
    public async Task ListPlansAsync_ResolvesTheFamilyOnceAndReusesItAcrossCalls()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ProductList(MaxioResponses.ProductBody()))
            .RespondWithJson(MaxioResponses.ProductList(MaxioResponses.ProductBody()));

        var client = builder.Build();
        await client.ListPlansAsync();
        await client.ListPlansAsync();

        // One family lookup plus two product lookups — the family is not re-resolved.
        Assert.Equal(3, builder.Handler.Requests.Count);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsTheMappedPlan()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Product(handle: "basic-plan", name: "Basic Plan", priceInCents: 2900));

        var plan = await builder.Build().FindPlanByHandleAsync("basic-plan");

        Assert.NotNull(plan);
        Assert.Equal("basic-plan", plan!.Handle);
        Assert.Equal(2900L, plan.PriceInCents);
        Assert.Equal(29.00m, plan.Price);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsNullForAnUnknownHandle()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Product not found"}""");

        var plan = await builder.Build().FindPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_SurfacesANonNotFoundFailureAsATypedException()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.Unauthorized, """{"error":"Unauthorized"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().FindPlanByHandleAsync("eshop-pro"));

        Assert.Equal((int)HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task FindMeteredComponentAsync_MapsTheUnitPriceInCents()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(MaxioResponses.ComponentBody()));

        var component = await builder.Build().FindMeteredComponentAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(3057195, component!.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);

        // $0.01 per unit is 1 cent.
        Assert.Equal(1L, component.UnitPriceInCents);
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task FindMeteredComponentAsync_ReturnsNullWhenTheHandleIsNotOnTheFamily()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(
                MaxioResponses.ComponentBody(handle: "something-else")));

        var component = await builder.Build().FindMeteredComponentAsync("api-call");

        Assert.Null(component);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_ReturnsTheValidatedComponent()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(MaxioResponses.ComponentBody()));

        var component = await builder.Build().GetConfiguredMeteredComponentAsync();

        Assert.True(component.IsMetered);
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_RefusesAComponentOfTheWrongKind()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(
                MaxioResponses.ComponentBody(kind: "quantity_based_component")));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().GetConfiguredMeteredComponentAsync());

        Assert.Contains("not of metered kind", exception.Message);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_RefusesAnArchivedComponent()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(
                MaxioResponses.ComponentBody(archived: true)));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().GetConfiguredMeteredComponentAsync());

        Assert.Contains("archived", exception.Message);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_FailsWhenTheComponentIsMissingEntirely()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson("[]");

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().GetConfiguredMeteredComponentAsync());

        Assert.Contains("api-call", exception.Message);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_ValidatesOnceAndCachesTheResult()
    {
        var builder = new BillingClientBuilder()
            .RespondWithProductFamilyLookup()
            .RespondWithJson(MaxioResponses.ComponentList(MaxioResponses.ComponentBody()));

        var client = builder.Build();
        await client.GetConfiguredMeteredComponentAsync();
        await client.GetConfiguredMeteredComponentAsync();

        // The stub throws on an unqueued request, so a second lookup would fail this test.
        Assert.Equal(2, builder.Handler.Requests.Count);
    }

    [Fact]
    public async Task GetConfiguredMeteredComponentAsync_FailsWhenNoComponentHandleIsConfigured()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MeteredComponentHandle = string.Empty);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().GetConfiguredMeteredComponentAsync());

        Assert.Contains(nameof(Infrastructure.Configuration.MaxioSettings.MeteredComponentHandle), exception.Message);
    }
}
