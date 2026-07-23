using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Pay-as-you-go usage (UC2) and plan change with proration (UC3).</summary>
public class MaxioBillingClientUsageAndPlanChangeTests
{
    [Fact]
    public async Task GetMeteredComponentAsync_ResolvesTheConfiguredHandle_AndPricesItInMajorUnits()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.MeteredComponentJson);
        var client = TestBillingClient.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.NotNull(component);
        Assert.Equal(MaxioPayloads.ApiCallComponentId, component!.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal("metered_component", component.Kind);
        Assert.True(component.IsMetered);
        Assert.Equal("per_unit", component.PricingScheme);
        // Components are priced in decimal major units as a string: "0.01" is one cent, not $0.0001.
        Assert.Equal(0.01m, component.UnitPrice);

        Assert.Equal("/components/lookup.json", handler.LastRequest.Path);
        Assert.Contains("handle=api-call", handler.LastRequest.PathAndQuery);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ReportsANonMeteredComponentAsSuch_RatherThanPretendingItIsMetered()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.QuantityBasedComponentJson);
        var client = TestBillingClient.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_ReturnsNull_WhenTheConfiguredHandleDoesNotResolve()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson);
        var client = TestBillingClient.Create(handler);

        Assert.Null(await client.GetMeteredComponentAsync());
    }

    [Fact]
    public async Task GetMeteredComponentAsync_Throws_WhenNoComponentHandleIsConfigured()
    {
        var settings = TestBillingClient.Settings();
        settings.MeteredComponentHandle = null;

        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.MeteredComponentJson);
        var client = TestBillingClient.Create(handler, settings);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetMeteredComponentAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RecordUsageAsync_PostsTheQuantityAndMemo_AndProjectsTheRecordedUsage()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.UsageJson);
        var client = TestBillingClient.Create(handler);

        var record = await client.RecordUsageAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, 25, "API calls");

        Assert.Equal(3633658896L, record.Id);
        Assert.Equal(25m, record.Quantity);
        Assert.Equal("API calls", record.Memo);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(MaxioPayloads.SubscriptionId, record.SubscriptionId);
        Assert.Equal(MaxioPayloads.ApiCallComponentId, record.ComponentId);
        Assert.NotNull(record.CreatedAt);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/components/{MaxioPayloads.ApiCallComponentId}/usages.json", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement.GetProperty("usage");
        Assert.Equal(25m, body.GetProperty("quantity").GetDecimal());
        Assert.Equal("API calls", body.GetProperty("memo").GetString());
    }

    [Fact]
    public async Task RecordUsageAsync_ReadsAQuantityBack_EvenWhenMaxioReportsItAsADecimalString()
    {
        // Maxio returns quantity as a number on create but as a string such as "20.0" on list.
        var handler = StubHttpMessageHandler.ReturningOk("""
            { "usage": { "id": 1, "quantity": "20.5", "component_id": 3062732, "component_handle": "api-call", "subscription_id": 93482504 } }
            """);
        var client = TestBillingClient.Create(handler);

        var record = await client.RecordUsageAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, 20.5m, null);

        Assert.Equal(20.5m, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Price point: could not be found."]}""");
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId, 1, null));

        Assert.Contains("Price point: could not be found.", exception.ProviderErrors);
    }

    [Fact]
    public async Task GetUsageTotalAsync_ReadsTheAccruedUnitBalanceForTheCurrentPeriod()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.SubscriptionComponentJson);
        var client = TestBillingClient.Create(handler);

        var total = await client.GetUsageTotalAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId);

        Assert.Equal(35m, total);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/components/{MaxioPayloads.ApiCallComponentId}.json", handler.LastRequest.Path);
    }

    [Fact]
    public async Task GetUsageTotalAsync_ReportsZero_WhenNothingHasAccruedYet()
    {
        var handler = StubHttpMessageHandler.ReturningOk("""
            { "component": { "component_id": 3062732, "subscription_id": 93482504, "unit_balance": null } }
            """);
        var client = TestBillingClient.Create(handler);

        Assert.Equal(0m, await client.GetUsageTotalAsync(MaxioPayloads.SubscriptionId, MaxioPayloads.ApiCallComponentId));
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Immediate_ReturnsTheProratedAmountsInBothUnits()
    {
        var handler = StubHttpMessageHandler.Routing(
            ("/products/handle/basic-plan.json", HttpStatusCode.OK, MaxioPayloads.BasicPlanJson),
            ("/migrations/preview.json", HttpStatusCode.OK, MaxioPayloads.MigrationPreviewJson));
        var client = TestBillingClient.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(ActiveProSubscription(), "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("eshop-pro", preview.CurrentPlan.Handle);
        Assert.Equal("basic-plan", preview.TargetPlan.Handle);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);

        Assert.Equal(-29900, preview.ProratedAdjustmentInCents);
        Assert.Equal(2934, preview.ChargeInCents);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(-26966, preview.CreditAppliedInCents);

        Assert.Equal(-299.00m, preview.ProratedAdjustment);
        Assert.Equal(29.34m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(-269.66m, preview.CreditApplied);

        var previewRequest = handler.Requests[^1];
        Assert.Equal(HttpMethod.Post, previewRequest.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/migrations/preview.json", previewRequest.Path);

        // Preserving the period is what makes the change prorated rather than a full re-charge.
        var body = JsonDocument.Parse(previewRequest.Body!).RootElement.GetProperty("migration");
        Assert.Equal("basic-plan", body.GetProperty("product_handle").GetString());
        Assert.True(body.GetProperty("preserve_period").GetBoolean());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_AtNextRenewal_QuotesTheNewPlanPriceWithNothingDueNow_AndDoesNotAskForAProration()
    {
        var handler = StubHttpMessageHandler.Routing(
            ("/products/handle/basic-plan.json", HttpStatusCode.OK, MaxioPayloads.BasicPlanJson));
        var client = TestBillingClient.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(ActiveProSubscription(), "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(2900, preview.ChargeInCents);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(0, preview.ProratedAdjustmentInCents);
        Assert.Equal(0, preview.CreditAppliedInCents);

        // A delayed change is never prorated, so no migration preview is requested at all.
        Assert.DoesNotContain(handler.Requests, r => r.Path.Contains("migrations"));
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Throws_WhenTheTargetPlanHandleDoesNotResolve()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, MaxioPayloads.NotFoundJson);
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.PreviewPlanChangeAsync(ActiveProSubscription(), "no-such-plan", PlanChangeTiming.Immediate));

        Assert.Contains("no-such-plan", exception.Message);
    }

    [Fact]
    public async Task ChangePlanAsync_Immediate_MigratesTheSubscriptionPreservingTheBillingPeriod()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.SubscriptionWithPendingPlanChangeJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", subscription.Plan.Handle);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}/migrations.json", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement.GetProperty("migration");
        Assert.Equal("basic-plan", body.GetProperty("product_handle").GetString());
        Assert.True(body.GetProperty("preserve_period").GetBoolean());
    }

    [Fact]
    public async Task ChangePlanAsync_AtNextRenewal_SchedulesADelayedProductChangeInsteadOfMigrating()
    {
        var handler = StubHttpMessageHandler.ReturningOk(MaxioPayloads.SubscriptionWithPendingPlanChangeJson);
        var client = TestBillingClient.Create(handler);

        var subscription = await client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.AtNextRenewal);

        // The current plan is unchanged; the new one is pending until the period rolls over.
        Assert.Equal("basic-plan", subscription.Plan.Handle);
        Assert.Equal("eshop-pro", subscription.PendingPlanHandle);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"/subscriptions/{MaxioPayloads.SubscriptionId}.json", request.Path);

        var body = JsonDocument.Parse(request.Body!).RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        Assert.True(body.GetProperty("product_change_delayed").GetBoolean());
    }

    [Fact]
    public async Task ChangePlanAsync_SurfacesTheProvidersRejection()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Subscription must be active"]}""");
        var client = TestBillingClient.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Subscription must be active", exception.ProviderErrors);
    }

    private static Subscription ActiveProSubscription()
    {
        var plan = new SubscriptionPlan(MaxioPayloads.ProPlanId, "eshop-pro", "Pro Plan", null, 29900, 1, "month");

        return new Subscription(MaxioPayloads.SubscriptionId,
            MaxioPayloads.CustomerId,
            MaxioPayloads.CustomerReference,
            plan,
            SubscriptionState.Active,
            "active",
            DateTimeOffset.Parse("2026-08-23T11:55:15+05:00"),
            DateTimeOffset.Parse("2026-08-23T11:55:15+05:00"),
            cancelAtEndOfPeriod: false,
            delayedCancelAt: null,
            balanceInCents: 29900);
    }
}
