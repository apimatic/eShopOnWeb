using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class PlanChanges
{
    private readonly RecordingHttpMessageHandler _handler = new();

    private static string PreviewPath => $"/subscriptions/{MaxioResponses.SubscriptionId}/migrations/preview.json";

    private static string MigrationPath => $"/subscriptions/{MaxioResponses.SubscriptionId}/migrations.json";

    private static string SubscriptionPath => $"/subscriptions/{MaxioResponses.SubscriptionId}.json";

    [Fact]
    public async Task PreviewsAnImmediateChangeWithTheProvidersProratedFigures()
    {
        _handler.RespondJson(HttpMethod.Post, PreviewPath, MaxioResponses.MigrationPreview);

        var preview = await TestBillingClientFactory.Create(_handler).PreviewPlanChangeAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        // Credits are negative and charges positive, exactly as the provider reports them.
        Assert.Equal(-29900, preview.ProratedAdjustmentInCents);
        Assert.Equal(2905, preview.ChargeInCents);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(-26995, preview.CreditAppliedInCents);
        Assert.Equal(PlanChangeTiming.Immediate, preview.Timing);
        Assert.Equal("basic-plan", preview.TargetProductHandle);
    }

    [Fact]
    public async Task ExposesThePreviewedAmountsInMajorUnitsToo()
    {
        _handler.RespondJson(HttpMethod.Post, PreviewPath, MaxioResponses.MigrationPreview);

        var preview = await TestBillingClientFactory.Create(_handler).PreviewPlanChangeAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal(-299.00m, preview.ProratedAdjustment);
        Assert.Equal(29.05m, preview.Charge);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(-269.95m, preview.CreditApplied);
    }

    [Fact]
    public async Task SendsTheTargetPlanHandleWhenPreviewingAnImmediateChange()
    {
        _handler.RespondJson(HttpMethod.Post, PreviewPath, MaxioResponses.MigrationPreview);

        await TestBillingClientFactory.Create(_handler).PreviewPlanChangeAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Contains("\"product_handle\":\"basic-plan\"", Assert.Single(_handler.Requests).Body!);
    }

    /// <summary>
    /// Deferring to the next renewal prorates nothing, so the preview is the new plan's price with
    /// nothing due now — and no migration preview is requested from the provider.
    /// </summary>
    [Fact]
    public async Task PreviewsADeferredChangeAsTheNewPlanPriceWithNothingDueNow()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var preview = await TestBillingClientFactory.Create(_handler).PreviewPlanChangeAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(0, preview.ProratedAdjustmentInCents);
        Assert.Equal(2900, preview.ChargeInCents);
        Assert.Equal(0, preview.PaymentDueInCents);
        Assert.Equal(0, preview.CreditAppliedInCents);
        Assert.Equal(PlanChangeTiming.AtNextRenewal, preview.Timing);
        Assert.Empty(_handler.RequestsFor(HttpMethod.Post, PreviewPath));
    }

    [Fact]
    public async Task FailsWithAConfigurationErrorWhenTheDeferredTargetPlanDoesNotResolve()
    {
        _handler.RespondJson(HttpMethod.Get, MaxioResponses.FamilyPath, MaxioResponses.ProductFamilies)
                .RespondJson(HttpMethod.Get, MaxioResponses.ProductsPath, MaxioResponses.Products);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            client.PreviewPlanChangeAsync(MaxioResponses.SubscriptionId, "no-such-plan", PlanChangeTiming.AtNextRenewal));

        Assert.Contains("no-such-plan", exception.Message);
    }

    [Fact]
    public async Task CommitsAnImmediateChangeAsAMigration()
    {
        _handler.RespondJson(HttpMethod.Post, MigrationPath,
            MaxioResponses.Subscription(productHandle: "basic-plan", productName: "Basic Plan", productPriceInCents: 2900));

        var updated = await TestBillingClientFactory.Create(_handler).ChangePlanAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.Immediate);

        Assert.Equal("basic-plan", updated.ProductHandle);
        Assert.Equal(2900, updated.ProductPriceInCents);
        Assert.Equal(29.00m, updated.ProductPrice);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body!);
    }

    /// <summary>
    /// A deferred change is an update flagged for the next renewal, not a migration — a migration
    /// would move the plan immediately and prorate it.
    /// </summary>
    [Fact]
    public async Task CommitsADeferredChangeAsADelayedProductChange()
    {
        _handler.RespondJson(HttpMethod.Put, SubscriptionPath, MaxioResponses.Subscription(nextProductHandle: "basic-plan"));

        var updated = await TestBillingClientFactory.Create(_handler).ChangePlanAsync(
            MaxioResponses.SubscriptionId, "basic-plan", PlanChangeTiming.AtNextRenewal);

        // The plan itself has not moved yet; only the next one is scheduled.
        Assert.Equal("eshop-pro", updated.ProductHandle);
        Assert.Equal("basic-plan", updated.NextProductHandle);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("\"product_change_delayed\":true", request.Body!);
        Assert.Empty(_handler.RequestsFor(HttpMethod.Post, MigrationPath));
    }

    /// <summary>
    /// The fingerprint is what makes a stale preview detectable, so it must change whenever any
    /// previewed figure changes and stay stable when nothing does.
    /// </summary>
    [Fact]
    public void FingerprintsIdenticalPreviewsIdentically()
    {
        var first = new PlanChangePreview("basic-plan", PlanChangeTiming.Immediate, -29900, 2905, 0, -26995);
        var second = new PlanChangePreview("basic-plan", PlanChangeTiming.Immediate, -29900, 2905, 0, -26995);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("eshop-pro", PlanChangeTiming.Immediate, -29900, 2905, 0, -26995)]
    [InlineData("basic-plan", PlanChangeTiming.AtNextRenewal, -29900, 2905, 0, -26995)]
    [InlineData("basic-plan", PlanChangeTiming.Immediate, -29901, 2905, 0, -26995)]
    [InlineData("basic-plan", PlanChangeTiming.Immediate, -29900, 2906, 0, -26995)]
    [InlineData("basic-plan", PlanChangeTiming.Immediate, -29900, 2905, 1, -26995)]
    [InlineData("basic-plan", PlanChangeTiming.Immediate, -29900, 2905, 0, -26996)]
    public void FingerprintsDifferWhenAnyPreviewedFigureChanges(string handle,
        PlanChangeTiming timing,
        int prorated,
        int charge,
        int due,
        int credit)
    {
        var baseline = new PlanChangePreview("basic-plan", PlanChangeTiming.Immediate, -29900, 2905, 0, -26995);
        var changed = new PlanChangePreview(handle, timing, prorated, charge, due, credit);

        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }
}
