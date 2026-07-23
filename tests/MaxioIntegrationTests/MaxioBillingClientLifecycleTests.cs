using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Plan change (plan.md UC3) and the lifecycle transitions (UC4) through the provider seam.
/// </summary>
public class MaxioBillingClientLifecycleTests
{
    [Fact]
    public async Task PreviewPlanChangeAsync_MapsEveryProrationFieldFromCents()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.MigrationPreviewResponse(
                chargeInCents: 27_000,
                creditAppliedInCents: 2_600,
                paymentDueInCents: 24_400,
                proratedAdjustmentInCents: 24_400));

        var (client, _) = TestClientFactory.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(60001, "eshop-pro");

        Assert.Equal(60001, preview.SubscriptionId);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(270.00m, preview.Charge);
        Assert.Equal(26.00m, preview.CreditApplied);
        Assert.Equal(244.00m, preview.PaymentDue);
        Assert.Equal(24_400L, preview.PaymentDueInCents);
        Assert.Equal(244.00m, preview.ProratedAdjustment);
        Assert.True(preview.PreviewedAt <= DateTimeOffset.UtcNow);

        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_TreatsMissingProrationFieldsAsZero()
    {
        var handler = new FakeMaxioHandler().EnqueueOk("""{"migration":{}}""");
        var (client, _) = TestClientFactory.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(60001, "basic-plan");

        Assert.Equal(0m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
    }

    [Fact]
    public async Task ChangePlanImmediatelyAsync_SendsTheTargetHandle_AndReturnsTheNewPlan()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(
                planHandle: "basic-plan", planName: "Basic Plan", planPriceInCents: 2_900)));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.ChangePlanImmediatelyAsync(60001, "basic-plan");

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);
        Assert.Contains("\"product_handle\":\"basic-plan\"", handler.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchedulePlanChangeAsync_DefersTheChange_ByMarkingItDelayed()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(
                MaxioPayloads.Subscription(nextProductHandle: "basic-plan")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.SchedulePlanChangeAsync(60001, "basic-plan");

        // The current plan is untouched; only the scheduled one changes.
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("basic-plan", subscription.ScheduledPlanHandle);
        Assert.True(subscription.HasScheduledPlanChange);

        var request = handler.LastRequest;
        Assert.Contains("\"product_change_delayed\":true", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseSubscriptionAsync_ReturnsThePausedSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(
                state: "on_hold", onHoldAt: "2026-07-20T10:00:00-04:00")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.PauseSubscriptionAsync(60001);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.NotNull(subscription.PausedAt);
        Assert.Contains("/hold", handler.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_ReturnsTheActiveSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(state: "active")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.ResumeSubscriptionAsync(60001);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Null(subscription.PausedAt);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_SendsTheReason_AndReturnsTheCancelledSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(
                state: "canceled", canceledAt: "2026-07-23T09:00:00-04:00")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.CancelSubscriptionAsync(60001, "too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.NotNull(subscription.CanceledAt);
        Assert.Contains("too expensive", handler.LastRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelSubscriptionAtPeriodEndAsync_ReReadsTheSubscription_BecauseTheEndpointOnlyReturnsAMessage()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.DelayedCancellationResponse())
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(
                MaxioPayloads.Subscription(cancelAtEndOfPeriod: true)));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.CancelSubscriptionAtPeriodEndAsync(60001, "moving on");

        // The state shown to the customer comes from the provider, never from an assumption.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task CancelSubscriptionAtPeriodEndAsync_Throws_WhenTheSubscriptionCannotBeReRead()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.DelayedCancellationResponse())
            .Enqueue(HttpStatusCode.NotFound, """{"error":"gone"}""");

        var (client, _) = TestClientFactory.Create(handler);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAtPeriodEndAsync(60001, null));
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_ReturnsTheReactivatedSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(state: "active")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.ReactivateSubscriptionAsync(60001);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Contains("/reactivate", handler.LastRequest.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LifecycleCalls_SurfaceProviderRejections_AsTypedBillingFailures()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ValidationErrors("Subscription cannot be resumed from its current state."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ResumeSubscriptionAsync(60001));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("resume subscription", exception.Operation);
        Assert.Contains("cannot be resumed", exception.Message, StringComparison.Ordinal);
    }
}
