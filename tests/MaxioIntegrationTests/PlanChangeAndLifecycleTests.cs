using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Moving between plans and driving the lifecycle transitions.</summary>
public class PlanChangeAndLifecycleTests
{
    [Fact]
    public async Task AProrationQuoteIsReportedInMajorUnitsIncludingTheNegativeAdjustment()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/migrations/preview.json", BillingPayloads.MigrationPreview);
        var (client, _) = BillingClientFixture.Create(provider);

        var quote = await client.PreviewPlanChangeAsync(15236915, "basic-plan");

        Assert.Equal(-40.00m, quote.ProratedAdjustment);
        Assert.Equal(100.00m, quote.Charge);
        Assert.Equal(60.00m, quote.PaymentDue);
        Assert.Equal(40.00m, quote.CreditApplied);
    }

    [Fact]
    public async Task APreviewNeverCommitsAnything()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/migrations/preview.json", BillingPayloads.MigrationPreview);
        var (client, _) = BillingClientFixture.Create(provider);

        await client.PreviewPlanChangeAsync(15236915, "basic-plan");

        Assert.Equal(1, provider.CallsTo("/migrations/preview.json"));
        Assert.Equal(0, provider.Requests.Count(request =>
            request.Uri.PathAndQuery.EndsWith("/migrations.json", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task APaymentDueTheProviderOmitsIsTheChargeLessTheCredit()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/migrations/preview.json",
                BillingPayloads.MigrationPreviewWithoutPaymentDue);
        var (client, _) = BillingClientFixture.Create(provider);

        var quote = await client.PreviewPlanChangeAsync(15236915, "basic-plan");

        Assert.Equal(60.00m, quote.PaymentDue);
    }

    [Fact]
    public async Task CommittingAPlanChangeMigratesTheSubscriptionAndReturnsTheNewPlan()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/migrations.json",
                BillingPayloads.MigratedSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.MigratePlanAsync(15236915, "basic-plan");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"product_handle\":\"basic-plan\"", sent.Body);
        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.PlanPrice);
        Assert.Equal(60.00m, subscription.Balance);
    }

    [Fact]
    public async Task ADeferredPlanChangeIsQueuedRatherThanApplied()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Put, "/subscriptions/15236915.json",
                """
                {"subscription":{"id":15236915,"state":"active","next_product_handle":"basic-plan",
                  "current_period_ends_at":"2026-08-01T00:00:00-04:00",
                  "product":{"id":7126957,"handle":"eshop-pro","price_in_cents":29900}}}
                """);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.SchedulePlanChangeAsync(15236915, "basic-plan");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"product_change_delayed\":true", sent.Body);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("basic-plan", subscription.NextPlanHandle);
        Assert.Equal(0, provider.CallsTo("/migrations.json"));
    }

    [Fact]
    public async Task PausingPlacesTheSubscriptionOnHold()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/hold.json", BillingPayloads.PausedSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.PauseSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.OnHold, subscription.State);
        Assert.True(subscription.IsPaused);
        Assert.False(subscription.AllowsPlanChange);
    }

    [Fact]
    public async Task ResumingReturnsTheSubscriptionToActive()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/resume.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.ResumeSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.False(subscription.IsPaused);
    }

    [Fact]
    public async Task CancellingImmediatelyCarriesTheReasonAndDoesNotDeferToThePeriodEnd()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Delete, "/subscriptions/15236915.json", BillingPayloads.CanceledSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.CancelSubscriptionAsync(15236915, "too expensive");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"cancellation_message\":\"too expensive\"", sent.Body);
        Assert.DoesNotContain("\"cancel_at_end_of_period\":true", sent.Body);

        Assert.Equal(BillingSubscriptionState.Canceled, subscription.State);
        Assert.True(subscription.IsTerminated);
        Assert.False(subscription.IsLive);
    }

    [Fact]
    public async Task AnEndOfPeriodCancellationIsConfirmedByReReadingTheSubscription()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/delayed_cancel.json",
                BillingPayloads.DelayedCancellationAccepted)
            .Respond(HttpMethod.Get, "/subscriptions/15236915.json",
                BillingPayloads.PendingCancellationSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.ScheduleCancellationAsync(15236915, "switching plans");

        // The provider acknowledges a delayed cancel with a message only, so the state has to be
        // read back rather than assumed.
        Assert.Equal(1, provider.CallsTo("/delayed_cancel.json"));
        Assert.Equal(1, provider.CallsTo("/subscriptions/15236915.json"));
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)),
            subscription.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task ReactivatingBringsACancelledSubscriptionBack()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Put, "/subscriptions/15236915/reactivate.json",
                BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.ReactivateSubscriptionAsync(15236915);

        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task ASubscriptionTheProviderNeverReturnsIsAProviderFaultNotAnEmptyResult()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/hold.json", "{}", HttpStatusCode.OK);
        var (client, _) = BillingClientFixture.Create(provider);

        await Assert.ThrowsAsync<ApplicationCore.Exceptions.BillingProviderUnavailableException>(
            () => client.PauseSubscriptionAsync(15236915));
    }
}
