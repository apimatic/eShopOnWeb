using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>UC3 — previewing and committing a plan change, immediately or at next renewal.</summary>
public class MaxioBillingClientPlanChangeTests
{
    private static string SubscriptionPath => $"/subscriptions/{MaxioPayloads.SubscriptionId}.json";
    private static string PreviewPath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/migrations/preview.json";
    private static string MigratePath => $"/subscriptions/{MaxioPayloads.SubscriptionId}/migrations.json";

    [Fact]
    public async Task PreviewsTheProratedCostOfAnImmediateChangeInMajorUnits()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.Subscription(productHandle: "basic-plan", productPriceInCents: 2_900))
            .Map(HttpMethod.Post, PreviewPath, MaxioPayloads.MigrationPreview));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(239.00m, preview.PaymentDue);
        Assert.Equal(249.00m, preview.Charge);
        Assert.Equal(10.00m, preview.CreditApplied);
        Assert.Equal(239.00m, preview.ProratedAdjustment);
        Assert.Equal(299.00m, preview.NewPlanPrice);
        Assert.NotEmpty(preview.Token);

        // A preview must never commit anything.
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, MigratePath));
    }

    [Fact]
    public async Task PreviewsADeferredChangeAsCostingNothingNowAndStartingNextPeriod()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.Subscription(productHandle: "basic-plan", productPriceInCents: 2_900)));

        var preview = await harness.Client.PreviewPlanChangeAsync(
            MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(0m, preview.Charge);
        Assert.Equal(0m, preview.CreditApplied);
        Assert.Equal(299.00m, preview.NewPlanPrice);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 4, 0, 0, TimeSpan.Zero), preview.EffectiveAt);

        // Deferred changes are not prorated, so the provider's preview endpoint is not consulted.
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, PreviewPath));
    }

    [Fact]
    public async Task CommitsAnImmediateChangeThroughTheMigrationEndpoint()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Post, MigratePath, MaxioPayloads.Subscription(productHandle: "eshop-pro")));

        var subscription = await harness.Client.ChangePlanAsync(
            MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate);

        Assert.Equal("eshop-pro", subscription.PlanHandle);

        var body = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, MigratePath)).Body;
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"preserve_period\":true", body);
    }

    [Fact]
    public async Task DefersAChangeToTheNextRenewalThroughTheDelayedProductChangeFlag()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Put, SubscriptionPath, MaxioPayloads.Subscription(productHandle: "basic-plan", productPriceInCents: 2_900)));

        await harness.Client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.AtNextRenewal);

        var body = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Put, SubscriptionPath)).Body;
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"product_change_delayed\":true", body);

        // A deferred change must not go through the immediate, prorating migration endpoint.
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, MigratePath));
    }

    [Fact]
    public async Task PreviewAndCommitUseTheSameProrationBasis()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.Subscription(productHandle: "basic-plan", productPriceInCents: 2_900))
            .Map(HttpMethod.Post, PreviewPath, MaxioPayloads.MigrationPreview)
            .Map(HttpMethod.Post, MigratePath, MaxioPayloads.Subscription(productHandle: "eshop-pro")));

        await harness.Client.PreviewPlanChangeAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate);
        await harness.Client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate);

        var previewBody = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, PreviewPath)).Body;
        var commitBody = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, MigratePath)).Body;

        // Identical migration options: what was previewed is what is applied.
        Assert.Equal(previewBody, commitBody);
    }

    [Fact]
    public async Task FailsToPreviewAgainstAnUnknownSubscription()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.PreviewPlanChangeAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public async Task FailsToPreviewAgainstAnUnresolvableTargetPlan()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Get, SubscriptionPath, MaxioPayloads.Subscription(productHandle: "basic-plan")));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.PreviewPlanChangeAsync(MaxioPayloads.SubscriptionId, "ghost-plan", PlanChangeTiming.Immediate));

        Assert.Contains("ghost-plan", exception.Message);
    }

    [Fact]
    public async Task SurfacesARejectedCommitWithTheProvidersMessages()
    {
        using var harness = MaxioBillingClientHarness.WithSeededCatalog(handler => handler
            .Map(HttpMethod.Post, MigratePath, """{"errors":["Cannot migrate a canceled subscription"]}""", HttpStatusCode.UnprocessableEntity));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediate));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Cannot migrate a canceled subscription", exception.Message);
    }
}
