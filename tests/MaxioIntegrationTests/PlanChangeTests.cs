using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class PlanChangeTests
{
    [Fact]
    public async Task PreviewPlanChange_Immediate_ConvertsCentsToDollars()
    {
        const string json = """
        { "migration": { "prorated_adjustment_in_cents": -1500, "charge_in_cents": 5000, "payment_due_in_cents": 3500, "credit_applied_in_cents": 0 } }
        """;
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var preview = await client.PreviewPlanChangeAsync(100, "eshop-pro", applyImmediately: true);

        Assert.True(preview.ApplyImmediately);
        Assert.Equal(-15.00m, preview.ProratedAdjustment);
        Assert.Equal(50.00m, preview.ChargeAmount);
        Assert.Equal(35.00m, preview.PaymentDue);
        Assert.Equal(0m, preview.CreditApplied);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions/100/migrations/preview.json", request.PathAndQuery);
        Assert.Contains("\"preserve_period\":true", request.Body);
    }

    [Fact]
    public async Task PreviewPlanChange_AtRenewal_UsesTargetPlanPrice_NoProration()
    {
        // At-renewal preview reads the target plan's price from the family product list (no migration preview call).
        const string plansJson = """
        [
          { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
          { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
        ]
        """;
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, plansJson);

        var preview = await client.PreviewPlanChangeAsync(100, "basic-plan", applyImmediately: false);

        Assert.False(preview.ApplyImmediately);
        Assert.Equal(0m, preview.ProratedAdjustment);
        Assert.Equal(29.00m, preview.ChargeAmount);   // basic-plan recurring price
        Assert.Equal(0m, preview.PaymentDue);
        Assert.Contains("/product_families/", handler.Requests[0].PathAndQuery);   // no migrations/preview call
    }

    [Fact]
    public async Task ChangePlan_Immediate_PostsMigrationWithPreservePeriod()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "active", "eshop-pro", 29900));

        var updated = await client.ChangePlanAsync(100, "eshop-pro", applyImmediately: true);

        Assert.Equal("eshop-pro", updated.ProductHandle);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions/100/migrations.json", request.PathAndQuery);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
        Assert.Contains("\"preserve_period\":true", request.Body);
    }

    [Fact]
    public async Task ChangePlan_AtRenewal_PutsDelayedProductChange()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "active", "eshop-pro", 29900));

        await client.ChangePlanAsync(100, "basic-plan", applyImmediately: false);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("/subscriptions/100.json", request.PathAndQuery);
        Assert.Contains("\"product_handle\":\"basic-plan\"", request.Body);
        Assert.Contains("\"product_change_delayed\":true", request.Body);
    }
}
