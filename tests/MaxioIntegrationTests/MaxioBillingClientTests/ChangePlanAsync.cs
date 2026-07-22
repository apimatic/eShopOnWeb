using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ChangePlanAsync
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
    public async Task MigratesImmediatelyAndReturnsTheSubscriptionOnItsNewPlan()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        var updated = await BillingClientFixture.Create(_handler)
            .ChangePlanAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        Assert.Equal("eshop-pro", updated.Plan.Handle);
        Assert.Equal(299.00m, updated.Plan.Price);

        var request = _handler.LastRequest;
        Assert.Contains("/subscriptions/90210/migrations.json", request.Uri.AbsolutePath);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
    }

    [Fact]
    public async Task SendsTheSameOptionsItPreviewedWithSoTheChargeMatchesThePreview()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        await BillingClientFixture.Create(_handler)
            .ChangePlanAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.Immediately);

        var body = _handler.LastRequest.Body!;
        Assert.Contains("\"preserve_period\":false", body);
        Assert.Contains("\"include_coupons\":true", body);
        Assert.Contains("\"include_trial\":false", body);
        Assert.Contains("\"include_initial_charge\":false", body);
    }

    [Fact]
    public async Task DefersToTheNextRenewalWithoutProratingWhenAskedTo()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(product: ProviderPayloads.BasicPlanProduct,
                nextProductHandle: "eshop-pro")));

        var updated = await BillingClientFixture.Create(_handler)
            .ChangePlanAsync(OnBasicPlan(), "eshop-pro", PlanChangeTiming.AtNextRenewal);

        // The current period is untouched; the new plan is merely scheduled.
        Assert.Equal("basic-plan", updated.Plan.Handle);
        Assert.Equal("eshop-pro", updated.ScheduledPlanHandle);

        var request = _handler.LastRequest;
        Assert.DoesNotContain("migrations", request.Uri.AbsolutePath);
        Assert.Contains("\"product_change_delayed\":true", request.Body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
    }

    [Fact]
    public async Task RefusesAnEmptyTargetPlanBeforeCallingTheProvider()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => BillingClientFixture.Create(_handler)
                .ChangePlanAsync(OnBasicPlan(), "", PlanChangeTiming.Immediately));

        Assert.Empty(_handler.Requests);
    }
}
