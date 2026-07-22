using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC3 — previewing and committing a plan change. Preview amounts arrive from the provider in cents
/// and must reach the customer in dollars.
/// </summary>
public class PlanChangeTests
{
    private static StubBillingServer OnProPlan() => new StubBillingServer()
        .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
            BillingJson.Subscription(1001, planHandle: "eshop-pro", productPriceInCents: 29900)))
        .Get("products/handle", BillingJson.ProductEnvelope(
            BillingJson.Product(7130996, "basic-plan", "Basic Plan", 2900)));

    [Fact]
    public async Task Previews_an_immediate_change_converting_every_amount_from_cents_to_dollars()
    {
        var server = OnProPlan()
            .Post("migrations/preview", BillingJson.MigrationPreview(
                proratedAdjustmentInCents: -13450,
                chargeInCents: 2900,
                paymentDueInCents: 0,
                creditAppliedInCents: 13450));

        var preview = await BillingTestHarness.Build(server)
            .PreviewPlanChangeAsync(1001, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
        Assert.True(preview.IsProrated);

        Assert.Equal(-134.50m, preview.ProratedAdjustment);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(134.50m, preview.CreditApplied);
        Assert.Equal(29.00m, preview.TargetPlanPrice);

        Assert.Contains("\"product_handle\":\"basic-plan\"",
            Assert.Single(server.RequestsFor("migrations/preview")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Previews_an_at_renewal_change_as_unprorated_at_the_next_period_price()
    {
        // The provider prices an at-renewal change at the period boundary and offers no proration
        // preview for it, so nothing may be presented as charged now.
        var server = OnProPlan();

        var preview = await BillingTestHarness.Build(server)
            .PreviewPlanChangeAsync(1001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.False(preview.IsProrated);
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.NotNull(preview.EffectiveAt);

        // No proration preview is requested for a path the provider does not prorate.
        Assert.Empty(server.RequestsFor("migrations/preview"));
    }

    [Fact]
    public async Task The_preview_fingerprint_changes_when_any_amount_changes()
    {
        var basePreview = new PlanChangePreview(1001, "eshop-pro", "basic-plan", PlanChangeTiming.Immediately)
        {
            ProratedAdjustment = -134.50m,
            Charge = 29.00m,
            PaymentDue = 0m,
            CreditApplied = 134.50m,
            TargetPlanPrice = 29.00m
        };

        var identical = basePreview with { };
        var repriced = basePreview with { PaymentDue = 12.34m };
        var retargeted = basePreview with { TargetPlanHandle = "eshop-pro" };

        Assert.Equal(basePreview.Fingerprint, identical.Fingerprint);
        Assert.NotEqual(basePreview.Fingerprint, repriced.Fingerprint);
        Assert.NotEqual(basePreview.Fingerprint, retargeted.Fingerprint);
    }

    [Fact]
    public async Task Commits_an_immediate_change_through_the_prorating_migration_path()
    {
        var server = OnProPlan()
            .Post("/migrations.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, planHandle: "basic-plan", planName: "Basic Plan", productPriceInCents: 2900)));

        var updated = await BillingTestHarness.Build(server)
            .ChangePlanAsync(1001, "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Equal(29.00m, updated.PlanPrice);

        Assert.Contains("\"product_handle\":\"basic-plan\"",
            Assert.Single(server.RequestsFor("/migrations.json")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commits_an_at_renewal_change_as_a_deferred_product_change()
    {
        var server = new StubBillingServer()
            .Put("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, planHandle: "eshop-pro", nextProductHandle: "basic-plan")));

        var updated = await BillingTestHarness.Build(server)
            .ChangePlanAsync(1001, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // The subscription stays on its current plan; the move is scheduled.
        Assert.Equal("eshop-pro", updated.PlanHandle);
        Assert.Equal("basic-plan", updated.ScheduledPlanHandle);

        var body = Assert.Single(server.RequestsFor("/subscriptions/1001.json")).Body;
        Assert.Contains("\"product_change_delayed\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body, StringComparison.Ordinal);

        // The immediate, prorating path must not be used for a deferred change.
        Assert.Empty(server.RequestsFor("/migrations.json"));
    }

    [Fact]
    public async Task Surfaces_a_rejected_plan_change_as_a_typed_billing_exception()
    {
        var server = OnProPlan()
            .Post("/migrations.json",
                BillingJson.Errors("Cannot migrate to the same product."), HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).ChangePlanAsync(1001, "basic-plan", PlanChangeTiming.Immediately));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("same product", exception.ProviderMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Previewing_an_unknown_subscription_reports_it_as_not_found()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/4242.json", BillingJson.NotFound(), HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => BillingTestHarness.Build(server).PreviewPlanChangeAsync(4242, "basic-plan", PlanChangeTiming.Immediately));
    }
}
