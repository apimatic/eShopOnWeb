using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Plan change (UC3). Two things must hold: proration amounts convert from Maxio's cents, and the two
/// timings take genuinely different provider routes — an immediate change prorates, a deferred one must
/// not touch the migration path at all.
/// </summary>
public class MaxioBillingClientPlanChangeTests
{
    [Fact]
    public async Task PreviewPlanChangeAsync_Immediate_ConvertsProrationFromCents()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription(productHandle: "basic-plan", productPriceInCents: 2900));
        context.Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(MaxioPayloads.Products);
        context.Handler.Enqueue(MaxioPayloads.MigrationPreview(chargeInCents: 15_000, creditAppliedInCents: 1_450));

        var preview = await context.Client.PreviewPlanChangeAsync(
            MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediately);

        // 15000 cents charged, 1450 credited => $150.00 and $14.50, net $135.50.
        Assert.Equal(150.00m, preview.ProrationCharge);
        Assert.Equal(14.50m, preview.ProrationCredit);
        Assert.Equal(135.50m, preview.NetAmount);
        Assert.Equal(299.00m, preview.NewPlanPrice);
        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
        Assert.Equal(PlanChangeTiming.Immediately, preview.Timing);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_Immediate_ProducesACreditForADowngrade()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());
        context.Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(MaxioPayloads.Products);
        context.Handler.Enqueue(MaxioPayloads.MigrationPreview(chargeInCents: 1_450, creditAppliedInCents: 15_000));

        var preview = await context.Client.PreviewPlanChangeAsync(
            MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.Immediately);

        // A downgrade leaves the customer in credit, so the net is negative.
        Assert.Equal(-135.50m, preview.NetAmount);
        Assert.Equal(29.00m, preview.NewPlanPrice);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_AtNextRenewal_ProratesNothingAndSkipsTheMigrationCall()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());
        context.Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(MaxioPayloads.Products);

        var preview = await context.Client.PreviewPlanChangeAsync(
            MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(0m, preview.ProrationCharge);
        Assert.Equal(0m, preview.ProrationCredit);
        Assert.Equal(0m, preview.NetAmount);
        Assert.Equal(29.00m, preview.NewPlanPrice);

        // Effective at the period boundary, and no provider preview was requested.
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), preview.EffectiveAt);
        Assert.Equal(3, context.Handler.Requests.Count);
        Assert.DoesNotContain(context.Handler.Requests, request => request.Path.Contains("migration"));
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_ThrowsBillingConfiguration_WhenTheTargetPlanDoesNotResolve()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());
        context.Handler.Enqueue(MaxioPayloads.ProductFamilies).Enqueue(MaxioPayloads.Products);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => context.Client.PreviewPlanChangeAsync(
                MaxioPayloads.SubscriptionId, "ghost-plan", PlanChangeTiming.Immediately));

        Assert.Contains("ghost-plan", exception.Message);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_ThrowsBillingProvider_WhenTheSubscriptionDoesNotExist()
    {
        using var context = new BillingTestContext();
        context.Handler.EnqueueStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.PreviewPlanChangeAsync(4242, "eshop-pro", PlanChangeTiming.Immediately));

        Assert.Contains("4242", exception.Message);
    }

    [Fact]
    public async Task ChangePlanAsync_Immediate_UsesTheMigrationRoute()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription(productHandle: "eshop-pro"));

        var updated = await context.Client.ChangePlanAsync(
            MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediately);

        var request = Assert.Single(context.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("migration", request.Path);
        Assert.DoesNotContain("preview", request.Path);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body!);

        Assert.Equal("eshop-pro", updated.PlanHandle);
    }

    [Fact]
    public async Task ChangePlanAsync_AtNextRenewal_DefersInsteadOfMigrating()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription(productHandle: "eshop-pro"));

        await context.Client.ChangePlanAsync(
            MaxioPayloads.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        var request = Assert.Single(context.Handler.Requests);

        // A deferred change is an update to the subscription, never a migration — a migration would
        // prorate immediately and charge the customer now.
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.DoesNotContain("migration", request.Path);
        Assert.Contains("\"product_change_delayed\":true", request.Body!);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body!);
    }

    [Fact]
    public async Task ChangePlanAsync_RejectsABlankTargetHandle_WithoutCallingMaxio()
    {
        using var context = new BillingTestContext();

        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Client.ChangePlanAsync(MaxioPayloads.SubscriptionId, "  ", PlanChangeTiming.Immediately));

        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task ChangePlanAsync_SurfacesProviderRejection()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.ErrorList, HttpStatusCode.UnprocessableEntity);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ChangePlanAsync(
                MaxioPayloads.SubscriptionId, "eshop-pro", PlanChangeTiming.Immediately));
    }
}
