using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Plan change with proration (UC3) and the four lifecycle transitions (UC4).
/// </summary>
public class MaxioBillingClientLifecycleTests
{
    [Fact]
    public async Task PreviewPlanChangeAsync_MapsEveryProrationAmountFromCents()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(productHandle: "basic-plan"))
            .RespondWithJson(MaxioResponses.MigrationPreview(
                proratedAdjustmentInCents: -13500,
                chargeInCents: 29900,
                creditAppliedInCents: 13500,
                paymentDueInCents: 16400));

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(90001, "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);

        Assert.Equal(-13500L, preview.ProratedAdjustmentInCents);
        Assert.Equal(29900L, preview.ChargeInCents);
        Assert.Equal(13500L, preview.CreditAppliedInCents);
        Assert.Equal(16400L, preview.PaymentDueInCents);

        // $164.00 due now, not $16,400 and not $1.64.
        Assert.Equal(164.00m, preview.PaymentDue);
        Assert.Equal(299.00m, preview.Charge);
        Assert.Equal(135.00m, preview.CreditApplied);
        Assert.Equal(-135.00m, preview.ProratedAdjustment);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_ChargesNothingNowForAChangeDeferredToRenewal()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(productHandle: "eshop-pro"))
            .RespondWithJson(MaxioResponses.Product(handle: "basic-plan", priceInCents: 2900));

        var preview = await builder.Build()
            .PreviewPlanChangeAsync(90001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // A deferred change is not prorated: nothing is due now, and the new price starts next period.
        Assert.Equal(0L, preview.PaymentDueInCents);
        Assert.Equal(0L, preview.ProratedAdjustmentInCents);
        Assert.Equal(0L, preview.CreditAppliedInCents);
        Assert.Equal(2900L, preview.ChargeInCents);
        Assert.Equal(29.00m, preview.Charge);

        // Only the subscription read and the plan read — no proration call is made.
        Assert.Equal(2, builder.Handler.Requests.Count);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_FailsWhenTheSubscriptionDoesNotExist()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"not found"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().PreviewPlanChangeAsync(404404, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Equal((int)HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_FailsWithAConfigurationErrorWhenTheDeferredTargetIsUnknown()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(productHandle: "eshop-pro"))
            .Respond(HttpStatusCode.NotFound, """{"error":"not found"}""");

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => builder.Build().PreviewPlanChangeAsync(90001, "ghost-plan", PlanChangeTiming.AtNextRenewal));
    }

    [Fact]
    public async Task ChangePlanAsync_MigratesImmediatelyAndReturnsTheNewPlan()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(productHandle: "eshop-pro", productPriceInCents: 29900));

        var subscription = await builder.Build()
            .ChangePlanAsync(90001, "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);

        var request = Assert.Single(builder.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
    }

    [Fact]
    public async Task ChangePlanAsync_SchedulesADeferredChangeInsteadOfProratingNow()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(productHandle: "eshop-pro", nextProductHandle: "basic-plan"));

        var subscription = await builder.Build()
            .ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal("basic-plan", subscription.NextPlanHandle);

        var request = Assert.Single(builder.Handler.Requests);
        Assert.Contains("\"product_change_delayed\":true", request.Body);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
    }

    [Fact]
    public async Task ChangePlanAsync_SurfacesAProviderRejection()
    {
        var builder = new BillingClientBuilder()
            .Respond(
                HttpStatusCode.UnprocessableEntity,
                MaxioResponses.ErrorList("Product: cannot migrate a canceled subscription."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ChangePlanAsync(90001, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("cannot migrate a canceled subscription", exception.DisplayMessage);
    }

    [Fact]
    public async Task PauseSubscriptionAsync_ReturnsTheHeldSubscription()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(state: "on_hold"));

        var subscription = await builder.Build().PauseSubscriptionAsync(90001);

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.False(subscription.IsLive);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_ReturnsTheLiveSubscription()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(state: "active"));

        var subscription = await builder.Build().ResumeSubscriptionAsync(90001);

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_CancelsImmediatelyAndPassesTheReasonThrough()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(state: "canceled"));

        var subscription = await builder.Build()
            .CancelSubscriptionAsync(90001, CancellationTiming.Immediate, "Too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);

        var request = Assert.Single(builder.Handler.Requests);
        Assert.Contains("Too expensive", request.Body);

        // An immediate cancel must not smuggle in an end-of-period schedule.
        Assert.DoesNotContain("\"cancel_at_end_of_period\":true", request.Body);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_DefersToThePeriodBoundaryAndRereadsTheProvidersView()
    {
        var builder = new BillingClientBuilder()
            // The delayed-cancel endpoint answers with a message, so the subscription is re-read.
            .RespondWithJson(MaxioResponses.DelayedCancellation())
            .RespondWithJson(MaxioResponses.Subscription(state: "active", cancelAtEndOfPeriod: true));

        var subscription = await builder.Build()
            .CancelSubscriptionAsync(90001, CancellationTiming.EndOfPeriod, "Switching plans");

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.CancelAtEndOfPeriod);

        Assert.Equal(2, builder.Handler.Requests.Count);
        Assert.Contains("\"cancel_at_end_of_period\":true", builder.Handler.Requests[0].Body);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_ReturnsTheRevivedSubscription()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(state: "active"));

        var subscription = await builder.Build().ReactivateSubscriptionAsync(90001);

        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task LifecycleCalls_SurfaceAProviderRejectionAsATypedException()
    {
        var builder = new BillingClientBuilder()
            .Respond(
                HttpStatusCode.UnprocessableEntity,
                MaxioResponses.ErrorList("Subscription cannot be held within 24 hours of renewal."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().PauseSubscriptionAsync(90001));

        Assert.Contains("within 24 hours of renewal", exception.DisplayMessage);
    }
}
