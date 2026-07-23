using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC3 — plan change with a proration preview. UC4 — the four lifecycle transitions.</summary>
public class PlanChangeAndLifecycleTests
{
    private const int SubscriptionId = 93482336;
    private static string SubscriptionRoute => $"subscriptions/{SubscriptionId}.json";
    private static string MigrationRoute => $"subscriptions/{SubscriptionId}/migrations.json";
    private static string MigrationPreviewRoute => $"subscriptions/{SubscriptionId}/migrations/preview.json";

    private static MaxioTestContext ContextOnProPlan()
    {
        var context = new MaxioTestContext();
        context.Server.MapGet(SubscriptionRoute, FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        return context;
    }

    // --- UC3 ---------------------------------------------------------------------------------

    [Fact]
    public async Task ImmediatePreviewConvertsProrationAmountsIncludingNegativesToWholeUnits()
    {
        var context = ContextOnProPlan();
        context.Server.MapPost(MigrationPreviewRoute, FakeResponse.Ok(MaxioPayloads.MigrationPreview));

        var preview = await context.Client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        // -29900 minor units is a $299.00 credit, and 3049 is a $30.49 charge.
        Assert.Equal(-299.00m, preview.ProratedAdjustment);
        Assert.Equal(30.49m, preview.Charge);
        Assert.Equal(0.00m, preview.PaymentDue);
        Assert.Equal(-268.51m, preview.CreditApplied);
        // Net is what the customer is actually shown and what the commit is checked against.
        Assert.Equal(-268.51m, preview.NetAmount);
        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);
    }

    [Fact]
    public async Task ImmediatePreviewAsksTheProviderToPreserveTheBillingPeriod()
    {
        var context = ContextOnProPlan();
        context.Server.MapPost(MigrationPreviewRoute, FakeResponse.Ok(MaxioPayloads.MigrationPreview));

        await context.Client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        var request = context.Server.LastRequest(HttpMethod.Post, MigrationPreviewRoute);
        // preserve_period true is what makes this a proration rather than a period reset.
        Assert.Contains("\"preserve_period\":true", request!.Body);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
    }

    [Fact]
    public async Task AtRenewalPreviewShowsTheNewPlanPriceAndNoProration()
    {
        var context = ContextOnProPlan();
        context.Server.MapGet(MaxioTestContext.PlansRoute, FakeResponse.Ok(MaxioPayloads.PlanList));

        var preview = await context.Client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan",
            PlanChangeTiming.AtNextRenewal);

        // Deferring to the boundary charges nothing now; the new price simply starts next period.
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(29.00m, preview.NetAmount);
        // No proration is computed, so the provider's preview endpoint is not called at all.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, MigrationPreviewRoute));
    }

    [Fact]
    public async Task ImmediatePlanChangeMigratesTheSubscriptionAndReturnsTheNewPlan()
    {
        var context = ContextOnProPlan();
        context.Server.MapPost(MigrationRoute, FakeResponse.Ok(MaxioPayloads.ActiveBasicSubscription));

        var subscription = await context.Client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal("Basic Plan", subscription.PlanName);
        Assert.Equal(29.00m, subscription.PlanPrice);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        var request = context.Server.LastRequest(HttpMethod.Post, MigrationRoute);
        Assert.Contains("\"preserve_period\":true", request!.Body);
    }

    [Fact]
    public async Task AtRenewalPlanChangeSchedulesADelayedProductChangeInsteadOfMigrating()
    {
        var context = ContextOnProPlan();
        context.Server.Map(HttpMethod.Put, SubscriptionRoute, FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        await context.Client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var request = context.Server.LastRequest(HttpMethod.Put, SubscriptionRoute);
        Assert.Contains("\"product_change_delayed\":true", request!.Body);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
        // Migrating would prorate immediately, which is exactly what "at renewal" must avoid.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Post, MigrationRoute));
    }

    [Fact]
    public async Task PlanChangeSurfacesAProviderRejection()
    {
        var context = ContextOnProPlan();
        context.Server.MapPost(MigrationRoute,
            FakeResponse.Unprocessable("""{"errors":["Subscription must be active"]}"""));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Subscription must be active", exception.Message);
        Assert.Equal(422, exception.StatusCode);
    }

    // --- UC4 ---------------------------------------------------------------------------------

    [Fact]
    public async Task PauseHoldsTheSubscription()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost($"subscriptions/{SubscriptionId}/hold.json",
            FakeResponse.Ok(MaxioPayloads.OnHoldSubscription));

        var subscription = await context.Client.PauseAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.OnHold, subscription.State);
        Assert.True(subscription.IsPaused);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task ResumeReturnsAHeldSubscriptionToActive()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost($"subscriptions/{SubscriptionId}/resume.json",
            FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var subscription = await context.Client.ResumeAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task ImmediateCancelDeletesTheSubscriptionAndCarriesTheReason()
    {
        var context = new MaxioTestContext();
        context.Server.Map(HttpMethod.Delete, SubscriptionRoute, FakeResponse.Ok(MaxioPayloads.CanceledSubscription));

        var subscription = await context.Client.CancelAsync(SubscriptionId, CancellationTiming.Immediate,
            "Cancelled from the storefront");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.True(subscription.IsCanceled);

        var request = context.Server.LastRequest(HttpMethod.Delete, SubscriptionRoute);
        Assert.Contains("Cancelled from the storefront", request!.Body);
    }

    [Fact]
    public async Task EndOfPeriodCancelDefersToTheBoundaryAndReportsTheEffectiveDate()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost($"subscriptions/{SubscriptionId}/delayed_cancel.json",
            FakeResponse.Ok(MaxioPayloads.DelayedCancellationMessage));
        context.Server.MapGet(SubscriptionRoute, FakeResponse.Ok(MaxioPayloads.PendingCancellationSubscription));

        var subscription = await context.Client.CancelAsync(SubscriptionId, CancellationTiming.EndOfPeriod, "later");

        // The delayed-cancel endpoint only returns a message, so the state must be re-read.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 11, 44, 53, TimeSpan.FromHours(5)), subscription.DelayedCancelAt);
        Assert.True(subscription.IsActive);
        // The subscription must not be deleted outright.
        Assert.Equal(0, context.Server.CountRequests(HttpMethod.Delete, SubscriptionRoute));
    }

    [Fact]
    public async Task ReactivateRestartsACancelledSubscription()
    {
        var context = new MaxioTestContext();
        context.Server.Map(HttpMethod.Put, $"subscriptions/{SubscriptionId}/reactivate.json",
            FakeResponse.Ok(MaxioPayloads.ActiveProSubscription));

        var subscription = await context.Client.ReactivateAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task AnIllegalTransitionRejectedByTheProviderSurfacesItsMessage()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost($"subscriptions/{SubscriptionId}/resume.json",
            FakeResponse.Unprocessable("""{"errors":["Subscription cannot be resumed"]}"""));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ResumeAsync(SubscriptionId));

        Assert.Contains("cannot be resumed", exception.Message);
    }

    [Fact]
    public async Task AnEmptyProviderResponseIsReportedRatherThanReturningAnEmptySubscription()
    {
        var context = new MaxioTestContext();
        context.Server.MapPost($"subscriptions/{SubscriptionId}/hold.json", FakeResponse.Ok("{}"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.PauseAsync(SubscriptionId));

        Assert.Contains("did not return a subscription", exception.Message);
    }
}
