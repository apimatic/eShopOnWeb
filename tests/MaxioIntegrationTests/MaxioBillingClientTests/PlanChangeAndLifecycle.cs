using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class PlanChangeAndLifecycle
{
    [Fact]
    public async Task PreviewsAnImmediatePlanChangeAndReadsTheProratedAmounts()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("migrations/preview.json",
            MaxioJson.MigrationPreviewResponse(-1500, 27000, 25500, 1500));

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(101, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.Equal(-1500, preview.ProratedAdjustmentInCents);
        Assert.Equal(27000, preview.ChargeInCents);
        Assert.Equal(25500, preview.PaymentDueInCents);
        Assert.Equal(1500, preview.CreditAppliedInCents);

        // Cents to dollars, including the negative adjustment.
        Assert.Equal(-15.00m, preview.ProratedAdjustment);
        Assert.Equal(270.00m, preview.Charge);
        Assert.Equal(255.00m, preview.PaymentDue);
        Assert.Equal(15.00m, preview.CreditApplied);
    }

    [Fact]
    public async Task PreservesTheBillingPeriodSoAnImmediateChangeIsProratedRatherThanRebilled()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("migrations/preview.json",
            MaxioJson.MigrationPreviewResponse(0, 0, 0, 0));

        await builder.Build().PreviewPlanChangeAsync(101, "basic-plan", PlanChangeTiming.Immediately);

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        var migration = body.RootElement.GetProperty("migration");
        Assert.Equal("basic-plan", migration.GetProperty("product_handle").GetString());
        Assert.True(migration.GetProperty("preserve_period").GetBoolean());
    }

    [Fact]
    public async Task QuotesNothingDueForAChangeDeferredToTheNextRenewalWithoutCallingTheProvider()
    {
        var builder = new MaxioClientBuilder();

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(101, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(PlanChangeTiming.AtNextRenewal, preview.Timing);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(0, preview.ProratedAdjustmentInCents);
        // Nothing is prorated, so there is no migration to ask the provider about.
        Assert.Empty(builder.Handler.Requests);
    }

    [Fact]
    public async Task CommitsAnImmediatePlanChangeAsAMigration()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/migrations.json",
            MaxioJson.SubscriptionResponse(101, "active", "basic-plan", 2900));

        var subscription = await builder.Build()
            .ChangePlanAsync(101, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("basic-plan", subscription.Plan.Handle);
        Assert.Equal(29.00m, subscription.Plan.Price);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("subscriptions/101/migrations.json", request.Uri.ToString());

        using var body = JsonDocument.Parse(request.Body!);
        Assert.True(body.RootElement.GetProperty("migration").GetProperty("preserve_period").GetBoolean());
    }

    [Fact]
    public async Task SchedulesADeferredPlanChangeAsADelayedProductUpdate()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101.json",
            MaxioJson.SubscriptionResponse(101, "active"));

        await builder.Build().ChangePlanAsync(101, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("subscriptions/101.json", request.Uri.ToString());

        using var body = JsonDocument.Parse(request.Body!);
        var payload = body.RootElement.GetProperty("subscription");
        Assert.Equal("basic-plan", payload.GetProperty("product_handle").GetString());
        Assert.True(payload.GetProperty("product_change_delayed").GetBoolean());
    }

    [Fact]
    public async Task PausesASubscriptionByPuttingItOnHold()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/hold.json",
            MaxioJson.SubscriptionResponse(101, "on_hold"));

        var subscription = await builder.Build().PauseAsync(101, null);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.Equal(HttpMethod.Post, builder.Handler.LastRequest.Method);
        // With no resumption date Maxio expects no body at all.
        Assert.Null(builder.Handler.LastRequest.Body);
    }

    [Fact]
    public async Task SendsTheAutomaticResumptionDateWhenOneIsRequested()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/hold.json",
            MaxioJson.SubscriptionResponse(101, "on_hold"));

        var resumeAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        await builder.Build().PauseAsync(101, resumeAt);

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        var value = body.RootElement.GetProperty("hold").GetProperty("automatically_resume_at").GetString();
        Assert.StartsWith("2026-09-01T12:00:00", value);
    }

    [Fact]
    public async Task ResumesASubscriptionOffHold()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/resume.json",
            MaxioJson.SubscriptionResponse(101, "active"));

        var subscription = await builder.Build().ResumeAsync(101);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(HttpMethod.Post, builder.Handler.LastRequest.Method);
        Assert.Contains("subscriptions/101/resume.json", builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task CancelsImmediatelyByDeletingTheSubscription()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101.json",
            $$"""{ "subscription": {{MaxioJson.Subscription(101, "canceled", canceledAt: "2026-07-22T11:00:00-05:00")}} }""");

        var subscription = await builder.Build()
            .CancelAsync(101, CancellationTiming.Immediate, "no longer needed");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.NotNull(subscription.CanceledAt);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Delete, request.Method);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("no longer needed",
            body.RootElement.GetProperty("subscription").GetProperty("cancellation_message").GetString());
    }

    [Fact]
    public async Task CancelsAtPeriodEndAndRereadsTheSubscriptionBecauseMaxioOnlyAcknowledges()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler
            .RespondWithOk("delayed_cancel.json", MaxioJson.DelayedCancelMessage)
            .RespondWithOk("subscriptions/101.json",
                $$"""{ "subscription": {{MaxioJson.Subscription(101, "active", cancelAtEndOfPeriod: true)}} }""");

        var subscription = await builder.Build()
            .CancelAsync(101, CancellationTiming.EndOfPeriod, null);

        // The delayed-cancel call returns only {"message": ...}, so the state must be re-read.
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(SubscriptionState.Active, subscription.State);

        Assert.Equal(2, builder.Handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, builder.Handler.Requests[0].Method);
        Assert.Contains("delayed_cancel.json", builder.Handler.Requests[0].Uri.ToString());
        Assert.Equal(HttpMethod.Get, builder.Handler.Requests[1].Method);
    }

    [Fact]
    public async Task ReactivatesASubscriptionWithAnUnwrappedBody()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101/reactivate.json",
            MaxioJson.SubscriptionResponse(101, "active"));

        var subscription = await builder.Build().ReactivateAsync(101);

        Assert.Equal(SubscriptionState.Active, subscription.State);

        var request = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Put, request.Method);

        using var body = JsonDocument.Parse(request.Body!);
        // Reactivate is one of the few Maxio operations with no wrapper object.
        Assert.False(body.RootElement.TryGetProperty("subscription", out _));
    }
}
