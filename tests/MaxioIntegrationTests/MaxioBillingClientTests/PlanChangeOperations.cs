using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Plan change with proration (UC3). Maxio reports every preview amount in cents, so the
/// conversion here is what stands between the customer and a 100x wrong figure.
/// </summary>
public class PlanChangeOperations
{
    private static BillingPlan BasicPlan() =>
        new(MaxioJson.BasicPlanId, "basic-plan", "Basic Plan", 29.00m, 1, "month");

    private static Subscription OnProPlan() =>
        new(MaxioJson.SubscriptionId,
            MaxioJson.UserReference,
            MaxioJson.CustomerId,
            new BillingPlan(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 299.00m, 1, "month"),
            SubscriptionState.Active,
            "active")
        {
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4))
        };

    [Fact]
    public async Task ConvertsTheProratedPreviewAmountsFromCents()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.OK,
            MaxioJson.MigrationPreview(chargeInCents: 2_500, creditAppliedInCents: 1_000));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        Assert.Equal(25.00m, preview.ProratedCharge);
        Assert.Equal(10.00m, preview.ProratedCredit);
        Assert.Equal(15.00m, preview.NetAmount);
    }

    [Fact]
    public async Task ReportsANetCreditWhenTheCreditExceedsTheCharge()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.OK,
            MaxioJson.MigrationPreview(chargeInCents: 500, creditAppliedInCents: 20_000));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        // Downgrading mid-period credits the customer; the sign must survive the conversion.
        Assert.Equal(-195.00m, preview.NetAmount);
    }

    [Fact]
    public async Task TreatsAProviderSignedNegativeCreditAsACreditRatherThanAnExtraCharge()
    {
        using var harness = MaxioTestHarness.Create();

        // This is what Maxio actually returns for a mid-period downgrade: the credit is signed
        // negative. Passing that sign straight through flips the subtraction and turns a $240.90
        // credit into a $299.00 charge.
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.OK,
            MaxioJson.MigrationPreview(chargeInCents: 2_905, creditAppliedInCents: -26_995));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        Assert.Equal(29.05m, preview.ProratedCharge);
        Assert.Equal(269.95m, preview.ProratedCredit);
        Assert.Equal(-240.90m, preview.NetAmount);
    }

    [Fact]
    public async Task ReportsWhatMaxioWillActuallyBillOnConfirmation()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.OK,
            MaxioJson.MigrationPreview(chargeInCents: 2_905, creditAppliedInCents: -26_995,
                paymentDueInCents: 0));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        // A downgrade nets to an account credit, not a refund, so nothing is billed today. This is
        // taken verbatim from the provider rather than re-derived from charge minus credit, which
        // would wrongly promise a refund.
        Assert.Equal(0m, preview.AmountDueNow);
        Assert.Equal(-240.90m, preview.NetAmount);
    }

    [Fact]
    public async Task ReportsTheAmountDueForAnUpgradeThatIsCharged()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.OK,
            MaxioJson.MigrationPreview(chargeInCents: 26_995, creditAppliedInCents: -2_905,
                paymentDueInCents: 24_090));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        Assert.Equal(240.90m, preview.AmountDueNow);
        Assert.Equal(240.90m, preview.NetAmount);
    }


    [Fact]
    public async Task PreviewsAtNextRenewalWithoutCallingTheProviderOrProratingAnything()
    {
        using var harness = MaxioTestHarness.Create();

        var preview = await harness.Client.PreviewPlanChangeAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.AtNextRenewal);

        // Nothing is prorated when the change waits for the boundary, and asking Maxio would return
        // an immediate-proration figure that is not what the customer would be charged.
        Assert.Empty(harness.Handler.Requests);
        Assert.Equal(0m, preview.ProratedCharge);
        Assert.Equal(0m, preview.ProratedCredit);
        Assert.Equal(0m, preview.NetAmount);
        Assert.Equal(OnProPlan().CurrentPeriodEndsAt, preview.EffectiveAt);
    }

    [Fact]
    public void GivesTheSameFingerprintForTheSameQuotedCost()
    {
        var first = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, null);
        var second = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, DateTimeOffset.UtcNow);

        // The fingerprint must depend only on what the customer was shown, so an unrelated field
        // does not spuriously invalidate a preview they already agreed to.
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void SurvivesTheFewCentsOfProrationDriftThatEveryPassingSecondCauses()
    {
        var quoted = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            29.05m, 269.95m, 0m, null);
        var momentsLater = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            29.05m, 269.92m, 0m, null);

        // Proration is a function of how much of the period remains, so the amounts move constantly.
        // If that invalidated the quote, no plan change could ever be committed.
        Assert.Equal(quoted.Fingerprint, momentsLater.Fingerprint);
    }

    [Fact]
    public void GivesADifferentFingerprintWhenTheCurrentPlanIsRepriced()
    {
        var quoted = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, null);
        var repriced = new PlanChangePreview(1,
            new BillingPlan(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 349.00m, 1, "month"),
            BasicPlan(), PlanChangeTiming.Immediate, 25.00m, 10.00m, 15.00m, null);

        // A repriced plan changes what the customer is agreeing to and must invalidate the quote.
        Assert.NotEqual(quoted.Fingerprint, repriced.Fingerprint);
    }

    [Fact]
    public void GivesADifferentFingerprintForADifferentTargetPlan()
    {
        var toBasic = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, null);
        var toSomethingElse = new PlanChangePreview(1, OnProPlan().Plan,
            new BillingPlan(99, "enterprise", "Enterprise", 29.00m, 1, "month"),
            PlanChangeTiming.Immediate, 25.00m, 10.00m, 15.00m, null);

        // Same price, different plan: the customer did not agree to this one.
        Assert.NotEqual(toBasic.Fingerprint, toSomethingElse.Fingerprint);
    }

    [Fact]
    public void GivesADifferentFingerprintForADifferentSubscription()
    {
        var one = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, null);
        var another = new PlanChangePreview(2, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate,
            25.00m, 10.00m, 15.00m, null);

        // A quote for one subscription must not authorize a change to a different one.
        Assert.NotEqual(one.Fingerprint, another.Fingerprint);
    }

    [Fact]
    public void GivesADifferentFingerprintWhenThePlanPriceChangesBeneathAnIdenticalProration()
    {
        var current = OnProPlan().Plan;
        var quoted = new PlanChangePreview(1, current, BasicPlan(), PlanChangeTiming.Immediate, 25m, 10m, 15m, null);
        var repricedTarget = new PlanChangePreview(1, current,
            new BillingPlan(MaxioJson.BasicPlanId, "basic-plan", "Basic Plan", 39.00m, 1, "month"),
            PlanChangeTiming.Immediate, 25m, 10m, 15m, null);

        // The plan price is part of what the customer decided on, even when the proration matches.
        Assert.NotEqual(quoted.Fingerprint, repricedTarget.Fingerprint);
    }

    [Fact]
    public void GivesADifferentFingerprintForADifferentTiming()
    {
        var now = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.Immediate, 0m, 0m, 0m, null);
        var later = new PlanChangePreview(1, OnProPlan().Plan, BasicPlan(), PlanChangeTiming.AtNextRenewal, 0m, 0m, 0m, null);

        Assert.NotEqual(now.Fingerprint, later.Fingerprint);
    }

    [Fact]
    public async Task MigratesImmediatelyWhenTheChangeIsToApplyNow()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations.json", HttpStatusCode.OK,
            MaxioJson.Subscription(product: MaxioJson.BasicPlan()));

        var updated = await harness.Client.ChangePlanAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", updated.Plan.Handle);
        Assert.Equal(29.00m, updated.Plan.Price);

        var request = harness.Handler.Requests.Single();
        Assert.Contains("/migrations.json", request.Uri.AbsolutePath);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task SchedulesADelayedChangeWhenTheChangeIsToApplyAtRenewal()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Put, $"/subscriptions/{MaxioJson.SubscriptionId}.json",
            HttpStatusCode.OK, MaxioJson.Subscription(nextProductHandle: "basic-plan"));

        var updated = await harness.Client.ChangePlanAsync(
            OnProPlan(), BasicPlan(), PlanChangeTiming.AtNextRenewal);

        // The subscription stays on its current plan; the move is scheduled, not applied.
        Assert.Equal("eshop-pro", updated.Plan.Handle);
        Assert.Equal("basic-plan", updated.PendingPlanHandle);

        var body = harness.Handler.Requests.Single().Body.Replace(" ", string.Empty);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
        Assert.Contains("\"product_change_delayed\":true", body);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenThePreviewIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Product not found"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.PreviewPlanChangeAsync(OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product not found", exception.ProviderMessages);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenTheMigrationIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/migrations.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Subscription is canceled"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.ChangePlanAsync(OnProPlan(), BasicPlan(), PlanChangeTiming.Immediate));

        Assert.Contains("Subscription is canceled", exception.ProviderMessages);
    }
}
