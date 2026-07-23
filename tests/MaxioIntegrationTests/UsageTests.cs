using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC2 — pay-as-you-go usage: the metered-kind precondition, recording, and read-back.</summary>
public class UsageTests
{
    private const int SubscriptionId = 93482336;
    private static string UsageRoute => $"subscriptions/{SubscriptionId}/components/handle:api-call/usages.json";
    private static string SubscriptionComponentRoute => $"subscriptions/{SubscriptionId}/components/handle:api-call.json";

    private static MaxioTestContext ContextWithMeteredComponent()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet(MaxioTestContext.ComponentRoute, FakeResponse.Ok(MaxioPayloads.MeteredComponent));

        return context;
    }

    [Fact]
    public async Task GetComponentByHandleReadsThePerUnitPriceAsWholeCurrencyUnits()
    {
        var context = ContextWithMeteredComponent();

        var component = await context.Client.GetComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        // unit_price arrives as the decimal string "0.01" — a penny per call, not 1 cent-of-a-cent.
        Assert.Equal(0.01m, component!.UnitPrice);
        Assert.Equal("metered_component", component.Kind);
        Assert.True(component.IsMetered);
        Assert.Equal("per_unit", component.PricingScheme);
        Assert.False(component.Archived);
        Assert.Equal(3062733, component.Id);
    }

    [Fact]
    public async Task GetComponentByHandleReturnsNullWhenTheHandleDoesNotResolve()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet($"product_families/{MaxioTestContext.ProductFamilyId}/components/handle:missing.json",
            FakeResponse.NotFound());

        Assert.Null(await context.Client.GetComponentByHandleAsync("missing"));
    }

    [Fact]
    public async Task RecordUsageReportsTheQuantityAndReturnsTheRecord()
    {
        var context = ContextWithMeteredComponent();
        context.Server.MapPost(UsageRoute, FakeResponse.Ok(MaxioPayloads.UsageRecorded));

        var record = await context.Client.RecordUsageAsync(SubscriptionId, "api-call", 150, "Reported from the storefront");

        Assert.Equal(3633653747, record.Id);
        Assert.Equal(150m, record.Quantity);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(SubscriptionId, record.SubscriptionId);
        Assert.Equal("Reported from the storefront", record.Memo);

        var request = context.Server.LastRequest(HttpMethod.Post, UsageRoute);
        Assert.Contains("\"quantity\":150", request!.Body);
        Assert.Contains("\"memo\":\"Reported from the storefront\"", request.Body);
    }

    [Fact]
    public async Task RecordUsageReadsAQuantityReturnedAsAString()
    {
        var context = ContextWithMeteredComponent();
        context.Server.MapPost(UsageRoute, FakeResponse.Ok(MaxioPayloads.UsageRecordedWithStringQuantity));

        var record = await context.Client.RecordUsageAsync(SubscriptionId, "api-call", 20.5m, "batch");

        // The provider documents quantity as either a number or a string; 20.5 must survive both.
        Assert.Equal(20.5m, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheConfiguredHandleIsNotAMeteredComponent()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet(MaxioTestContext.ComponentRoute, FakeResponse.Ok(MaxioPayloads.QuantityBasedComponent));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.RecordUsageAsync(SubscriptionId, "api-call", 1, null));

        Assert.Contains("quantity_based_component", exception.Message);
        Assert.Contains("not metered", exception.Message);
        // The precondition must stop the call, not merely complain after billing something.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, UsageRoute));
    }

    [Fact]
    public async Task RecordUsageRefusesWhenTheConfiguredHandleDoesNotResolveAtAll()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.Ok(MaxioPayloads.ProductFamily));
        context.Server.MapGet(MaxioTestContext.ComponentRoute, FakeResponse.NotFound());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.RecordUsageAsync(SubscriptionId, "api-call", 1, null));

        Assert.Contains("does not resolve", exception.Message);
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, UsageRoute));
    }

    [Fact]
    public async Task TheMeteredPreconditionIsCheckedOncePerProcessNotOncePerReport()
    {
        var context = ContextWithMeteredComponent();
        context.Server.MapPost(UsageRoute, FakeResponse.Ok(MaxioPayloads.UsageRecorded));

        await context.Client.RecordUsageAsync(SubscriptionId, "api-call", 1, null);
        await context.Client.RecordUsageAsync(SubscriptionId, "api-call", 1, null);
        await context.Client.RecordUsageAsync(SubscriptionId, "api-call", 1, null);

        Assert.Equal(3, context.Server.CountRequests(HttpMethod.Post, UsageRoute));
        Assert.Equal(1, context.Server.CountRequests(HttpMethod.Get, MaxioTestContext.ComponentRoute));
        Assert.True(context.ValidationCache.IsValidated);
    }

    [Fact]
    public async Task GetUsageSummaryReturnsTheRunningPeriodToDateBalance()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(SubscriptionComponentRoute,
            FakeResponse.Ok(MaxioPayloads.SubscriptionComponentWithBalance));

        var summary = await context.Client.GetUsageSummaryAsync(SubscriptionId, "api-call");

        Assert.NotNull(summary);
        Assert.Equal(150m, summary!.UnitBalance);
        Assert.Equal("API Calls", summary.ComponentName);
        Assert.Equal("api-call", summary.ComponentHandle);
        Assert.Equal(SubscriptionId, summary.SubscriptionId);
        Assert.Equal(3062733, summary.ComponentId);
    }

    [Fact]
    public async Task GetUsageSummaryReturnsNullWhenTheComponentIsNotOnTheSubscription()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(SubscriptionComponentRoute, FakeResponse.NotFound());

        // Null, not an exception: a missing read-back must not fail the usage that already stands.
        Assert.Null(await context.Client.GetUsageSummaryAsync(SubscriptionId, "api-call"));
    }

    [Fact]
    public async Task GetUsageSummaryTreatsAnAbsentBalanceAsZeroRatherThanFailing()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(SubscriptionComponentRoute, FakeResponse.Ok("""
            { "component": { "component_id": 3062733, "subscription_id": 93482336,
              "component_handle": "api-call", "name": "API Calls", "kind": "metered_component" } }
            """));

        var summary = await context.Client.GetUsageSummaryAsync(SubscriptionId, "api-call");

        Assert.Equal(0m, summary!.UnitBalance);
    }

    [Fact]
    public async Task RecordUsageSurfacesAProviderRejection()
    {
        var context = ContextWithMeteredComponent();
        context.Server.MapPost(UsageRoute,
            FakeResponse.Unprocessable("""{"errors":["Price point: could not be found."]}"""));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.RecordUsageAsync(SubscriptionId, "api-call", 10, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Price point", exception.Message);
    }

    [Fact]
    public async Task ResolvingTheFamilyForComponentLookupUsesItsHandleAndIsCached()
    {
        var context = ContextWithMeteredComponent();

        await context.Client.GetComponentByHandleAsync("api-call");
        await context.Client.GetComponentByHandleAsync("api-call");

        Assert.Equal(1, context.Server.CountRequests(HttpMethod.Get, MaxioTestContext.FamilyRoute));
        Assert.Equal(2, context.Server.CountRequests(HttpMethod.Get, MaxioTestContext.ComponentRoute));
    }

    [Fact]
    public async Task AMissingProductFamilyIsReportedAsAConfigurationError()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(MaxioTestContext.FamilyRoute, FakeResponse.NotFound());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.GetComponentByHandleAsync("api-call"));

        Assert.Contains("eshop-subscribe", exception.Message);
        Assert.Contains("does not resolve", exception.Message);
    }
}
