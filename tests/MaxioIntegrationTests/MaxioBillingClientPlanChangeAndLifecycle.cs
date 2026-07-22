using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Plan change with proration preview (UC3) and the lifecycle transitions (UC4).
/// </summary>
public class MaxioBillingClientPlanChangeAndLifecycle
{
    private const int SubscriptionId = 88001;

    private static MaxioApiStub StubReadSubscription(MaxioApiStub stub, string? subscriptionJson = null) =>
        MaxioTestHarness.StubCatalog(stub)
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(subscriptionJson ?? MaxioJson.Subscription()));

    [Fact]
    public async Task PreviewPlanChangeConvertsEveryProrationAmountFromCents()
    {
        var stub = StubReadSubscription(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathContaining("migrations", "preview"), HttpStatusCode.OK,
                MaxioJson.MigrationPreviewResponse(
                    proratedAdjustmentInCents: -24_900L,
                    chargeInCents: 29_900L,
                    paymentDueInCents: 5_000L,
                    creditAppliedInCents: 24_900L));

        using var harness = new MaxioTestHarness(stub);

        var preview = await harness.Client.PreviewPlanChangeAsync(
            SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(-249.00m, preview.ProratedAdjustment);
        Assert.Equal(299.00m, preview.Charge);
        Assert.Equal(50.00m, preview.PaymentDue);
        Assert.Equal(249.00m, preview.CreditApplied);
        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);
    }

    [Fact]
    public async Task PreviewPlanChangeSendsTheResolvedTargetProductId()
    {
        var stub = StubReadSubscription(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathContaining("migrations", "preview"), HttpStatusCode.OK,
                MaxioJson.MigrationPreviewResponse());

        using var harness = new MaxioTestHarness(stub);

        await harness.Client.PreviewPlanChangeAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        var previewCall = Assert.Single(stub.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains("\"product_id\"", previewCall.Body, StringComparison.Ordinal);
        Assert.Contains("7130998", previewCall.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPlanChangeAtNextRenewalQuotesTheNewPlanPriceAndNothingDueNow()
    {
        var stub = StubReadSubscription(new MaxioApiStub());

        using var harness = new MaxioTestHarness(stub);

        var preview = await harness.Client.PreviewPlanChangeAsync(
            SubscriptionId, "basic-plan", PlanChangeTiming.NextRenewal);

        // Deferring to the renewal raises no proration at all.
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(0m, preview.CreditApplied);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), preview.EffectiveAt);

        // No migration preview is taken; that endpoint would quote a proration that never happens.
        Assert.DoesNotContain(stub.Requests, r => r.Uri.AbsolutePath.Contains("migrations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewPlanChangeThrowsWhenTheSubscriptionDoesNotExist()
    {
        using var harness = new MaxioTestHarness(new MaxioApiStub());

        await Assert.ThrowsAsync<BillingProviderNotFoundException>(
            () => harness.Client.PreviewPlanChangeAsync(999999, "basic-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task ChangePlanImmediatelyMigratesTheSubscriptionAndReturnsTheNewPlan()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("migrations.json"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(
                    MaxioJson.Subscription(planHandle: "basic-plan", planPriceInCents: 2_900L)));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.ChangePlanAsync(
            SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);

        var request = Assert.Single(stub.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains("\"product_id\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("7130998", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePlanAtNextRenewalSchedulesADelayedProductChangeInsteadOfMigrating()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Put, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(nextProductHandle: "basic-plan")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.ChangePlanAsync(
            SubscriptionId, "basic-plan", PlanChangeTiming.NextRenewal);

        Assert.Equal("basic-plan", subscription.PendingPlanHandle);
        // The subscription keeps billing its current plan until the renewal.
        Assert.Equal("eshop-pro", subscription.PlanHandle);

        var request = Assert.Single(stub.Requests, r => r.Method == HttpMethod.Put);
        Assert.Contains("\"product_change_delayed\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("true", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("migrations", request.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePlanSurfacesTheProvidersValidationMessages()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("migrations.json"),
                HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Cannot migrate a canceled subscription."));

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => harness.Client.ChangePlanAsync(SubscriptionId, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Contains("Cannot migrate a canceled subscription.", ex.Errors);
    }

    [Fact]
    public async Task PauseSubscriptionHoldsItAndReportsThePausedState()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("hold.json"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(state: "on_hold")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.PauseSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ResumeSubscriptionReturnsItToActive()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathContaining("88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(state: "active")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.ResumeSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task CancelSubscriptionCancelsImmediatelyAndSendsTheReason()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Delete, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(
                    MaxioJson.Subscription(state: "canceled", canceledAt: "2026-07-22T12:00:00-04:00")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.CancelSubscriptionAsync(SubscriptionId, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.NotNull(subscription.CanceledAt);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("Too expensive", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelAtEndOfPeriodSchedulesTheCancellationAndRereadsTheSubscription()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("delayed_cancel.json"), HttpStatusCode.OK,
                MaxioJson.DelayedCancellationResponse())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(
                    cancelAtEndOfPeriod: true, delayedCancelAt: "2026-08-01T00:00:00-04:00")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.CancelSubscriptionAtEndOfPeriodAsync(SubscriptionId, "Switching plans");

        // The delayed-cancel endpoint only answers with a message, so the scheduled date has to
        // come from a fresh read or the customer would be shown stale state.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.DelayedCancelAt);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        Assert.Contains(stub.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains(stub.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task ReactivateSubscriptionBringsACancelledSubscriptionBack()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Put, MaxioApiStub.PathEndingWith("reactivate.json"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(state: "active")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.ReactivateSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Active, subscription.State);
    }
}
