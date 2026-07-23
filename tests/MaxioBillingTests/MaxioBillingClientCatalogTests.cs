using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Catalog reads through the real seam: unit conversion from Maxio's cents, archived filtering, empty
/// results, unknown handles, and provider failures surfacing as the typed exception.
/// </summary>
public class MaxioBillingClientCatalogTests
{
    [Fact]
    public async Task ListPlansAsync_ConvertsCentsToDollars()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        var plans = await context.Client.ListPlansAsync();

        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");
        var basic = Assert.Single(plans, plan => plan.Handle == "basic-plan");

        // 29900 cents is $299.00 — not $29,900 and not $2.99.
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansAsync_MapsPlanShape()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        var plans = await context.Client.ListPlansAsync();
        var pro = Assert.Single(plans, plan => plan.Handle == "eshop-pro");

        Assert.Equal(MaxioPayloads.ProProductId, pro.Id);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.False(pro.Archived);
        Assert.Equal("$299.00 / month", pro.PriceDisplay);
    }

    [Fact]
    public async Task ListPlansAsync_OrdersByPriceAscending()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        var plans = await context.Client.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle).ToArray());
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedPlans()
    {
        using var context = new BillingTestContext().WithPlanLookup(MaxioPayloads.ProductsWithArchived);

        var plans = await context.Client.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.DoesNotContain(plans, plan => plan.Handle == "retired-plan");
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsEmpty_WhenTheFamilyHasNoProducts()
    {
        using var context = new BillingTestContext().WithPlanLookup(MaxioPayloads.EmptyList);

        var plans = await context.Client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlansAsync_TargetsTheConfiguredBaseUrl()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        await context.Client.ListPlansAsync();

        // The Maxio:BaseUrl override must actually decide where traffic goes (plan.md §2.3).
        Assert.All(context.Handler.Requests,
            request => Assert.StartsWith(BillingTestContext.MockBaseUrl, request.Uri.ToString()));
    }

    [Fact]
    public async Task ListPlansAsync_ThrowsBillingConfiguration_WhenTheFamilyHandleDoesNotResolve()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.EmptyList);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => context.Client.ListPlansAsync());

        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task ListPlansAsync_ThrowsBillingProvider_WhenMaxioFails()
    {
        using var context = new BillingTestContext();
        context.Handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ListPlansAsync());

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsNull_ForAnUnknownHandle()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        Assert.Null(await context.Client.FindPlanByHandleAsync("no-such-plan"));
    }

    [Fact]
    public async Task FindPlanByHandleAsync_ReturnsNull_ForAnEmptyHandle_WithoutCallingMaxio()
    {
        using var context = new BillingTestContext();

        Assert.Null(await context.Client.FindPlanByHandleAsync("   "));
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_MatchesCaseInsensitively()
    {
        using var context = new BillingTestContext().WithPlanLookup();

        var plan = await context.Client.FindPlanByHandleAsync("ESHOP-PRO");

        Assert.NotNull(plan);
        Assert.Equal(299.00m, plan!.Price);
    }

    [Fact]
    public async Task FindComponentByHandleAsync_RecognisesAMeteredComponent()
    {
        using var context = new BillingTestContext().WithComponentLookup();

        var component = await context.Client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component!.IsMetered);
        Assert.Equal(MaxioPayloads.ComponentId, component.Id);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal("per_unit", component.PricingScheme);
        // Maxio publishes a component's unit price as a decimal-dollars string, not cents.
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task FindComponentByHandleAsync_ReportsANonMeteredComponentAsNotMetered()
    {
        using var context = new BillingTestContext().WithComponentLookup(MaxioPayloads.QuantityBasedComponents);

        var component = await context.Client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task FindComponentByHandleAsync_ReturnsNull_ForAnUnknownHandle()
    {
        using var context = new BillingTestContext().WithComponentLookup();

        Assert.Null(await context.Client.FindComponentByHandleAsync("no-such-component"));
    }

    [Fact]
    public async Task FindComponentByHandleAsync_ReturnsNull_WhenTheFamilyHasNoComponents()
    {
        using var context = new BillingTestContext().WithComponentLookup(MaxioPayloads.EmptyList);

        Assert.Null(await context.Client.FindComponentByHandleAsync("api-call"));
    }

    [Fact]
    public async Task ResolvedFamilyId_IsCached_SoTheLookupIsNotRepeated()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.CatalogCacheDuration = TimeSpan.FromMinutes(5);

        using var context = new BillingTestContext(settings);
        context.Handler
            .Enqueue(MaxioPayloads.ProductFamilies)
            .Enqueue(MaxioPayloads.Products)
            .Enqueue(MaxioPayloads.Products);

        await context.Client.ListPlansAsync();
        await context.Client.ListPlansAsync();

        // Three requests, not four: the family lookup happened once.
        Assert.Equal(3, context.Handler.Requests.Count);
    }
}
