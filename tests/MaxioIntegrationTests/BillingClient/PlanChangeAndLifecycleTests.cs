using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

public class PlanChangeAndLifecycleTests
{
    [Fact]
    public async Task PreviewConvertsEveryProrationAmountFromCentsIncludingCredits()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.MigrationPreview);
        var client = BillingClientBuilder.Build(handler);

        var preview = await client.PreviewPlanChangeAsync(93491347, "basic-plan");

        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(-299.00m, preview.ProratedAdjustment);
        Assert.Equal(31.00m, preview.Charge);
        Assert.Equal(0.00m, preview.PaymentDue);
        Assert.Equal(-268.00m, preview.CreditApplied);
    }

    [Fact]
    public async Task PreviewDoesNotCommitTheChange()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.MigrationPreview);
        var client = BillingClientBuilder.Build(handler);

        await client.PreviewPlanChangeAsync(93491347, "basic-plan");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/subscriptions/93491347/migrations/preview.json", request.Path);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
    }

    [Fact]
    public async Task AnImmediateChangeMigratesTheSubscriptionSoProrationApplies()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.MigratedSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.ChangePlanAsync(93491347, "basic-plan", PlanChangeTiming.Immediate);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/93491347/migrations.json", request.Path);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);
    }

    [Fact]
    public async Task AChangeAtRenewalIsScheduledWithoutProrationAndLeavesTheCurrentPlanInPlace()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.DelayedPlanChangeSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.ChangePlanAsync(93491347, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/subscriptions/93491347.json", request.Path);
        Assert.Contains("\"product_change_delayed\":true", request.Body);

        // The subscription stays on its current plan until the renewal.
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("basic-plan", subscription.NextPlanHandle);
    }

    [Fact]
    public async Task PauseHoldsTheSubscription()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.OnHoldSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.PauseAsync(93491347, null);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/93491347/hold.json", request.Path);
        Assert.Equal(SubscriptionStatus.OnHold, subscription.Status);
    }

    [Fact]
    public async Task PauseCanScheduleAnAutomaticResume()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.OnHoldSubscription);
        var client = BillingClientBuilder.Build(handler);

        var resumeAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        await client.PauseAsync(93491347, resumeAt);

        Assert.Contains("automatically_resume_at", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task ResumeReturnsTheSubscriptionToActive()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ActiveSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.ResumeAsync(93491347);

        Assert.Equal("/subscriptions/93491347/resume.json", Assert.Single(handler.Requests).Path);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task AnImmediateCancelDeletesTheSubscriptionAndCarriesTheReason()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.CanceledSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.CancelAsync(93491347, CancellationTiming.Immediate, "Too expensive");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/subscriptions/93491347.json", request.Path);
        Assert.Contains("\"cancellation_message\":\"Too expensive\"", request.Body);

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }

    [Fact]
    public async Task AnEndOfPeriodCancelSchedulesTheCancellationAndReportsTheRefreshedState()
    {
        // The delayed-cancel endpoint answers with a bare message, so the client must re-read the
        // subscription to report its real state rather than inventing one.
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.DelayedCancelAccepted)
            .RespondWithJson(MaxioResponses.PendingCancellationSubscription);

        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.CancelAsync(93491347, CancellationTiming.EndOfBillingPeriod, null);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/subscriptions/93491347/delayed_cancel.json", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);

        // Still active, but now flagged to end at the period boundary.
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 20, 23, 0, TimeSpan.FromHours(5)),
            subscription.DelayedCancelAt);
    }

    [Fact]
    public async Task ReactivateBringsACancelledSubscriptionBack()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.ActiveSubscription);
        var client = BillingClientBuilder.Build(handler);

        var subscription = await client.ReactivateAsync(93491347);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/subscriptions/93491347/reactivate.json", request.Path);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }
}
