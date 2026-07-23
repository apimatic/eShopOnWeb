using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC3 — previewing and committing a plan change, and the fingerprint that makes a stale quote
/// detectable.
/// </summary>
public class MaxioBillingClientPlanChangeTests
{
    private const string Target = BillingClientFixture.AlternatePlanHandle;

    [Fact]
    public async Task PreviewPlanChangeAsync_Immediate_ConvertsProrationCentsToDollars()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription())),
            StubResponse.Ok(MaxioJson.ProductEnvelope(MaxioJson.Product(
                handle: Target, name: "Basic Plan", priceInCents: MaxioJson.BasicPlanCents))),
            StubResponse.Ok(MaxioJson.MigrationPreview(
                proratedAdjustmentInCents: 24_000,
                chargeInCents: 27_000,
                creditAppliedInCents: 3_000,
                paymentDueInCents: 24_000)));

        var preview = await BillingClientFixture.Create(handler)
            .PreviewPlanChangeAsync(900001, Target, PlanChangeTiming.Immediate);

        Assert.Equal(240.00m, preview.ProratedAdjustment);
        Assert.Equal(270.00m, preview.Charge);
        Assert.Equal(30.00m, preview.CreditApplied);
        Assert.Equal(240.00m, preview.PaymentDue);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal(Target, preview.TargetPlanHandle);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_AtNextRenewal_QuotesZeroProrationWithoutAskingTheProvider()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription())),
            StubResponse.Ok(MaxioJson.ProductEnvelope(MaxioJson.Product(
                handle: Target, name: "Basic Plan", priceInCents: MaxioJson.BasicPlanCents))));

        var preview = await BillingClientFixture.Create(handler)
            .PreviewPlanChangeAsync(900001, Target, PlanChangeTiming.AtNextRenewal);

        // A deferred change is not prorated: nothing is charged now.
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.Charge);
        Assert.Equal(0m, preview.CreditApplied);
        Assert.Equal(0m, preview.PaymentDue);

        // The customer still sees what they will pay from the next period.
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.NotNull(preview.EffectiveAt);

        // Two reads only; there is no migration preview to request.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Throws_WhenTheSubscriptionDoesNotExist()
    {
        var handler = StubHttpMessageHandler.Sequence(StubResponse.NotFound());

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).PreviewPlanChangeAsync(999999, Target, PlanChangeTiming.Immediate));

        Assert.True(ex.IsNotFound);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Throws_WhenTheTargetPlanDoesNotResolve()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.SubscriptionEnvelope(MaxioJson.Subscription())),
            StubResponse.NotFound());

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(handler)
                .PreviewPlanChangeAsync(900001, "no-such-plan", PlanChangeTiming.Immediate));

        Assert.Contains("no-such-plan", ex.Message);
    }

    [Fact]
    public async Task ChangePlanAsync_Immediate_MigratesAndReturnsTheNewPlan()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.SubscriptionEnvelope(
            MaxioJson.Subscription(planHandle: Target, planName: "Basic Plan", planPriceInCents: MaxioJson.BasicPlanCents)));

        var updated = await BillingClientFixture.Create(handler)
            .ChangePlanAsync(900001, Target, PlanChangeTiming.Immediate);

        Assert.Equal(Target, updated.PlanHandle);
        Assert.Equal(29.00m, updated.PlanPrice);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("migration", request.Path);
        Assert.Contains($"\"product_handle\":\"{Target}\"", request.Body!.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task ChangePlanAsync_AtNextRenewal_SchedulesADelayedProductChange()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.SubscriptionEnvelope(
            MaxioJson.Subscription(nextProductHandle: Target)));

        var updated = await BillingClientFixture.Create(handler)
            .ChangePlanAsync(900001, Target, PlanChangeTiming.AtNextRenewal);

        // The current plan is untouched; the change is queued for the next renewal.
        Assert.Equal("eshop-pro", updated.PlanHandle);
        Assert.Equal(Target, updated.NextPlanHandle);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Put, request.Method);

        var body = request.Body!.Replace(" ", string.Empty);
        Assert.Contains("\"product_change_delayed\":true", body);
        Assert.Contains($"\"product_handle\":\"{Target}\"", body);
    }

    [Fact]
    public async Task ChangePlanAsync_SurfacesAProviderRejection()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(
            MaxioJson.Errors("Subscription cannot be migrated."), (System.Net.HttpStatusCode)422);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ChangePlanAsync(900001, Target, PlanChangeTiming.Immediate));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Subscription cannot be migrated.", ex.Message);
    }

    // -------------------------------------------------------------------------------------------
    // Fingerprint: the mechanism that makes a stale quote detectable
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Fingerprint_IsStable_ForTheSameQuote()
    {
        var first = Quote(paymentDue: 240m);
        var second = Quote(paymentDue: 240m);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData(240.01)]
    [InlineData(239.99)]
    [InlineData(0)]
    public void Fingerprint_Changes_WhenTheAmountDueMoves(decimal movedPaymentDue)
    {
        Assert.NotEqual(Quote(paymentDue: 240m).Fingerprint, Quote(paymentDue: movedPaymentDue).Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenTheTargetPlanPriceMoves()
    {
        var original = Quote(paymentDue: 240m);
        var repriced = Quote(paymentDue: 240m, targetPlanPrice: 39m);

        Assert.NotEqual(original.Fingerprint, repriced.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenTheTimingChanges()
    {
        var now = Quote(paymentDue: 240m);
        var atRenewal = Quote(paymentDue: 240m, timing: PlanChangeTiming.AtNextRenewal);

        Assert.NotEqual(now.Fingerprint, atRenewal.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenTheTargetPlanChanges()
    {
        var toBasic = Quote(paymentDue: 240m);
        var toPro = Quote(paymentDue: 240m, targetPlanHandle: "eshop-pro");

        Assert.NotEqual(toBasic.Fingerprint, toPro.Fingerprint);
    }

    private static PlanChangePreview Quote(
        decimal paymentDue,
        decimal targetPlanPrice = 29m,
        PlanChangeTiming timing = PlanChangeTiming.Immediate,
        string targetPlanHandle = Target) => new()
        {
            SubscriptionId = 900001,
            CurrentPlanHandle = "eshop-pro",
            TargetPlanHandle = targetPlanHandle,
            Timing = timing,
            ProratedAdjustment = 240m,
            Charge = 270m,
            CreditApplied = 30m,
            PaymentDue = paymentDue,
            TargetPlanPrice = targetPlanPrice
        };
}
