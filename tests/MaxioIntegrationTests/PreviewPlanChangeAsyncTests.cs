using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class PreviewPlanChangeAsyncTests
{
    private const string ActiveProSubscriptionJson = """
        { "subscription": { "id": 4001, "state": "active", "current_period_ends_at": "2026-08-01T00:00:00Z",
            "product": { "id": 7127070, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } }
        """;

    [Fact]
    public async Task ApplyNowConvertsProratedAmountsFromCentsToDollars()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, ActiveProSubscriptionJson),
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "migration": { "prorated_adjustment_in_cents": -13500, "charge_in_cents": 1600, "payment_due_in_cents": 1600, "credit_applied_in_cents": 13500 } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(4001, "basic-plan", applyNow: true);

        Assert.Equal("eshop-pro", preview.FromPlanHandle);
        Assert.Equal("basic-plan", preview.ToPlanHandle);
        Assert.True(preview.ApplyNow);
        Assert.Equal(-135.00m, preview.ProratedAmount);
        Assert.Equal(16.00m, preview.PaymentDueAmount);
        Assert.Equal(135.00m, preview.CreditAppliedAmount);
        Assert.Contains("/migrations/preview", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ApplyLaterComputesADeterministicPreviewWithoutCallingTheMigrationEndpoint()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, ActiveProSubscriptionJson),
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            [{ "product": { "id": 7127071, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }]
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(4001, "basic-plan", applyNow: false);

        Assert.False(preview.ApplyNow);
        Assert.Equal(0m, preview.ProratedAmount);
        Assert.Equal(0m, preview.CreditAppliedAmount);
        Assert.Equal(29.00m, preview.PaymentDueAmount);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), preview.EffectiveDate);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("migrations"));
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenTheProviderRejectsThePreview()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, ActiveProSubscriptionJson),
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Cannot migrate to the current product"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PreviewPlanChangeAsync(4001, "eshop-pro", applyNow: true));

        Assert.Equal(422, ex.StatusCode);
    }
}
