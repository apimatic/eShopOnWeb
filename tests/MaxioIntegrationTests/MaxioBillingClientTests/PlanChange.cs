using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class PlanChange
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly StubHttpMessageHandler _handler = new();

    private StubHttpMessageHandler WithActiveProSubscription() =>
        _handler
            .RespondOk(HttpMethod.Get, "/subscriptions/42.json",
                MaxioJson.SubscriptionResponse(42, "active", 33, UserReference))
            .RespondOk(HttpMethod.Get, "/products/handle/basic-plan",
                MaxioJson.ProductResponse(MaxioJson.BasicPlanId, "basic-plan", "Basic Plan", MaxioJson.BasicPlanPriceInCents));

    [Fact]
    public async Task PreviewsAnImmediateChangeWithAmountsInWholeCurrencyUnits()
    {
        WithActiveProSubscription()
            .RespondOk(HttpMethod.Post, "/migrations/preview.json",
                MaxioJson.MigrationPreview(proratedAdjustmentInCents: -24_150,
                    chargeInCents: 2_900,
                    paymentDueInCents: 0,
                    creditAppliedInCents: 24_150));
        var client = BillingClientBuilder.Build(_handler);

        var preview = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);

        // Every amount arrives as integer cents and must be shown in dollars.
        Assert.Equal(-241.50m, preview.ProratedAdjustment);
        Assert.Equal(29.00m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(241.50m, preview.CreditApplied);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.Equal("eshop-pro", preview.CurrentPlanHandle);
        Assert.Equal("basic-plan", preview.TargetPlanHandle);
    }

    [Fact]
    public async Task PreviewsAChangeAtNextRenewalAsUnproratedWithoutCallingTheProvider()
    {
        // A deferred change costs nothing now — the customer simply pays the new price from the
        // next period, so there is nothing for the provider to price.
        WithActiveProSubscription();
        var client = BillingClientBuilder.Build(_handler);

        var preview = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), preview.EffectiveAt);
        Assert.Empty(_handler.RequestsFor("/migrations/preview.json"));
    }

    [Fact]
    public async Task PreviewSignatureIsStableForTheSamePricedFacts()
    {
        WithActiveProSubscription()
            .RespondOk(HttpMethod.Post, "/migrations/preview.json",
                MaxioJson.MigrationPreview(-24_150, 2_900, 0, 24_150));
        var client = BillingClientBuilder.Build(_handler);

        var first = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);
        var second = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(first.Signature, second.Signature);
        Assert.True(first.Matches(second));
    }

    [Fact]
    public async Task PreviewSignatureChangesWhenTheAmountChanges()
    {
        // This is what makes a stale preview detectable at commit time: if the basis moved, the
        // signature the customer confirmed no longer matches.
        WithActiveProSubscription()
            .RespondInSequence(HttpMethod.Post, "/migrations/preview.json",
                MaxioJson.MigrationPreview(-24_150, 2_900, 0, 24_150),
                MaxioJson.MigrationPreview(-10_000, 2_900, 1_000, 10_000));
        var client = BillingClientBuilder.Build(_handler);

        var first = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);
        var second = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);

        Assert.NotEqual(first.Signature, second.Signature);
        Assert.False(first.Matches(second));
    }

    [Fact]
    public async Task PreviewSignatureDistinguishesTiming()
    {
        WithActiveProSubscription()
            .RespondOk(HttpMethod.Post, "/migrations/preview.json",
                MaxioJson.MigrationPreview(0, 0, 0, 0));
        var client = BillingClientBuilder.Build(_handler);

        var now = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate);
        var atRenewal = await client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.NotEqual(now.Signature, atRenewal.Signature);
    }

    [Fact]
    public async Task RejectsAPreviewForAPlanHandleThatDoesNotResolve()
    {
        _handler
            .RespondOk(HttpMethod.Get, "/subscriptions/42.json",
                MaxioJson.SubscriptionResponse(42, "active", 33, UserReference))
            .Respond(HttpMethod.Get, "/products/handle/nonexistent", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.PreviewPlanChangeAsync(42, "nonexistent", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task CommitsAnImmediateChangeAsAMigration()
    {
        _handler.RespondOk(HttpMethod.Post, "/migrations.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference, "basic-plan",
                MaxioJson.BasicPlanId, "Basic Plan", MaxioJson.BasicPlanPriceInCents));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Equal(29.00m, updated.PlanPrice);

        var posted = _handler.RequestsFor("/migrations.json").Single();
        Assert.Contains("\"product_handle\":\"basic-plan\"", posted.Body);
    }

    [Fact]
    public async Task CommitsAChangeAtNextRenewalAsADelayedProductChange()
    {
        _handler.RespondOk(HttpMethod.Put, "/subscriptions/42.json",
            MaxioJson.SubscriptionResponse(42, "active", 33, UserReference,
                nextProductHandle: "basic-plan", nextProductId: MaxioJson.BasicPlanId));
        var client = BillingClientBuilder.Build(_handler);

        var updated = await client.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // The subscription stays on its current plan; the change is queued for the next period.
        Assert.Equal("eshop-pro", updated.PlanHandle);
        Assert.True(updated.HasPendingPlanChange);
        Assert.Equal("basic-plan", updated.PendingPlanHandle);

        var body = _handler.LastRequest.Body;
        Assert.Contains("\"product_change_delayed\":true", body);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfADelayedPlanChange()
    {
        _handler.Respond(HttpMethod.Put, "/subscriptions/42.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Product: does not exist."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(42, "ghost", PlanChangeTiming.AtNextRenewal));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("does not exist", exception.ProviderMessage);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfAProrationPreview()
    {
        WithActiveProSubscription()
            .Respond(HttpMethod.Post, "/migrations/preview.json", HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Migration cannot be previewed for this subscription."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PreviewPlanChangeAsync(42, "basic-plan", PlanChangeTiming.Immediate));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderDuringAPlanChange()
    {
        _handler.Unreachable(HttpMethod.Post, "/migrations.json");
        var client = BillingClientBuilder.Build(_handler);

        Assert.True((await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(42, "basic-plan", PlanChangeTiming.Immediate))).IsTransport);
    }

    [Fact]
    public async Task RejectsAPreviewForAnUnknownSubscription()
    {
        _handler.Respond(HttpMethod.Get, "/subscriptions/999.json", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PreviewPlanChangeAsync(999, "basic-plan", PlanChangeTiming.Immediate));

        Assert.True(exception.IsNotFound);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfAPlanChangeWithItsOwnMessage()
    {
        _handler.Respond(HttpMethod.Post, "/migrations.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Cannot migrate to the same product."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ChangePlanAsync(42, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Cannot migrate to the same product.", exception.ProviderMessage);
    }
}
