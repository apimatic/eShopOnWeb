using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class PreviewPlanChangeAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    private static Subscription OnBasicPlan()
    {
        var plan = new BillingPlan(7131000, "basic-plan", "Basic Plan", null, 29.00m, 1, "month", false);
        return new Subscription(90210, 5551212, "demouser@microsoft.com", plan,
            SubscriptionState.Active, DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow.AddDays(10),
            false, null);
    }

    [Fact]
    public async Task ConvertsEveryProrationAmountFromCentsIntoWholeCurrencyUnits()
    {
        _handler.RespondWithJson(ProviderPayloads.MigrationPreview);

        var preview = await BillingClientFixture.Create(_handler)
            .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        Assert.Equal(247.50m, preview.ProratedAdjustment);
        Assert.Equal(270.00m, preview.Charge);
        Assert.Equal(247.50m, preview.PaymentDue);
        Assert.Equal(22.50m, preview.CreditApplied);
        Assert.Equal("basic-plan", preview.CurrentPlanHandle);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
    }

    [Fact]
    public async Task AsksTheProviderToPriceAnImmediateChangeAgainstTheTargetPlan()
    {
        _handler.RespondWithJson(ProviderPayloads.MigrationPreview);

        await BillingClientFixture.Create(_handler)
            .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        var request = _handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions/90210/migrations/preview.json", request.Uri.AbsolutePath);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
    }

    [Fact]
    public async Task PricesADeferredChangeAtTheTargetPlansPriceWithNoProration()
    {
        // A renewal-time change is never prorated, so the provider's proration preview is not used.
        _handler.RespondWithJson(ProviderPayloads.ProductResponse(ProviderPayloads.ProPlanProduct));

        var preview = await BillingClientFixture.Create(_handler)
            .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.AtNextRenewal);

        Assert.Equal(PlanChangeTiming.AtNextRenewal, preview.Timing);
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Equal(299.00m, preview.Charge);
        Assert.DoesNotContain("migrations", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task PointsBackAtTheSandboxSeedWhenTheDeferredTargetPlanDoesNotResolve()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(_handler)
                .PreviewPlanChangeAsync(OnBasicPlan(), "ghost-plan", PlanChangeTiming.AtNextRenewal));
    }

    [Fact]
    public async Task ProducesADifferentFingerprintWhenTheProvidersNumbersMove()
    {
        _handler.RespondWithJson(ProviderPayloads.MigrationPreview);
        var first = await BillingClientFixture.Create(_handler)
            .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        var moved = new StubHttpMessageHandler();
        moved.RespondWithJson("""
            {"migration": {"prorated_adjustment_in_cents": 30000, "charge_in_cents": 30000,
                           "payment_due_in_cents": 30000, "credit_applied_in_cents": 0}}
            """);
        var second = await BillingClientFixture.Create(moved)
            .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        // The fingerprint is what stops a stale preview being committed at a different price.
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionAsATypedBillingFailure()
    {
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity, ProviderPayloads.ValidationErrors);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler)
                .PreviewPlanChangeAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately));

        Assert.Equal(422, exception.StatusCode);
    }
}
