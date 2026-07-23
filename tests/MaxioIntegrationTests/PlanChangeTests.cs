using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>UC3 — proration preview and the two plan-change timings.</summary>
public class PlanChangeTests
{
    [Fact]
    public async Task AnImmediatePreviewReportsTheProvidersProratedAmountsInCentsAndInCurrency()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.MigrationPreview);

        var preview = await client.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);

        Assert.Equal(-1500L, preview.ProratedAdjustmentInCents);
        Assert.Equal(29900L, preview.ChargeInCents);
        Assert.Equal(28400L, preview.PaymentDueInCents);
        Assert.Equal(1500L, preview.CreditAppliedInCents);

        Assert.Equal(-15.00m, preview.ProratedAdjustment);
        Assert.Equal(299.00m, preview.Charge);
        Assert.Equal(284.00m, preview.PaymentDue);
        Assert.Equal(15.00m, preview.CreditApplied);
    }

    [Fact]
    public async Task AnImmediatePreviewAsksTheProviderAndChangesNothing()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.MigrationPreview);

        await client.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Single(handler.Requests);
        Assert.Contains("preview", handler.LastRequest.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnAtRenewalPreviewChargesNothingNowAndQuotesTheNewPlanPrice()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.BasicPlan);

        var preview = await client.PreviewPlanChangeAsync(90001, "eshop-pro", "basic-plan",
            PlanChangeTiming.AtNextRenewal);

        Assert.Equal(PlanChangeTiming.AtNextRenewal, preview.Timing);

        // Deferred to the next period, so no proration and nothing due today.
        Assert.Equal(0L, preview.ProratedAdjustmentInCents);
        Assert.Equal(0L, preview.CreditAppliedInCents);
        Assert.Equal(0L, preview.PaymentDueInCents);

        // The quoted charge is the new plan's price from the next period: $29.00.
        Assert.Equal(2900L, preview.ChargeInCents);
        Assert.Equal(29.00m, preview.Charge);
    }

    [Fact]
    public async Task AnAtRenewalPreviewForAnUnresolvableTargetIsAConfigurationFailure()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.PreviewPlanChangeAsync(90001, "eshop-pro", "gone", PlanChangeTiming.AtNextRenewal));
    }

    [Fact]
    public async Task TwoPreviewsOfTheSameCommitmentCompareAsEqual()
    {
        var (first, _) = BillingClientFixture.Create(ProviderPayloads.MigrationPreview);
        var (second, _) = BillingClientFixture.Create(ProviderPayloads.MigrationPreview);

        var a = await first.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);
        var b = await second.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);

        Assert.True(a.MatchesCommitmentOf(b));
    }

    [Fact]
    public async Task APreviewWhoseAmountsMovedDoesNotMatchTheOneTheCustomerSaw()
    {
        const string cheaperPreview = """
            {"migration": { "prorated_adjustment_in_cents": -1500, "charge_in_cents": 29900,
              "payment_due_in_cents": 10000, "credit_applied_in_cents": 1500 }}
            """;

        var (first, _) = BillingClientFixture.Create(ProviderPayloads.MigrationPreview);
        var (second, _) = BillingClientFixture.Create(cheaperPreview);

        var shown = await first.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);
        var current = await second.PreviewPlanChangeAsync(90001, "basic-plan", "eshop-pro", PlanChangeTiming.Immediate);

        Assert.False(shown.MatchesCommitmentOf(current));
    }

    [Fact]
    public async Task AnImmediateChangeMigratesTheSubscriptionToTheTargetPlan()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.BasicSubscription);

        var subscription = await client.ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(2900L, subscription.PlanPriceInCents);

        Assert.Contains("\"product_handle\":\"basic-plan\"", handler.LastRequestBody);
        // The immediate path must not ask for a delayed change.
        Assert.DoesNotContain("product_change_delayed", handler.LastRequestBody);
    }

    [Fact]
    public async Task AnAtRenewalChangeIsRequestedAsADelayedProductChange()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.DelayedChangeSubscription);

        var subscription = await client.ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Contains("\"product_change_delayed\":true", handler.LastRequestBody);
        Assert.Contains("\"product_handle\":\"basic-plan\"", handler.LastRequestBody);

        // Until the renewal the subscription is still on the old plan, with the new one scheduled.
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("basic-plan", subscription.NextPlanHandle);
    }

    [Fact]
    public async Task TheTwoTimingsUseDifferentProviderEndpoints()
    {
        var (immediateClient, immediateHandler) = BillingClientFixture.Create(ProviderPayloads.BasicSubscription);
        var (renewalClient, renewalHandler) = BillingClientFixture.Create(ProviderPayloads.DelayedChangeSubscription);

        await immediateClient.ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.Immediate);
        await renewalClient.ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.NotEqual(immediateHandler.LastRequest.RequestUri!.AbsolutePath,
            renewalHandler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AProviderRejectionOfAPlanChangeSurfacesAsATypedException()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ValidationError, HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(90001, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Equal("ChangePlanNow", exception.Operation);
        Assert.Contains("is invalid", exception.ProviderMessage);
    }
}
