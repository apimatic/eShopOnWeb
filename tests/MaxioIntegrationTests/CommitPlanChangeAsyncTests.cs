using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class CommitPlanChangeAsyncTests
{
    [Fact]
    public async Task ApplyNowCallsTheImmediateMigrationEndpointAndPreservesThePeriod()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "subscription": { "id": 4001, "state": "active",
                "product": { "id": 7127071, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 } } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var updated = await client.CommitPlanChangeAsync(4001, "basic-plan", applyNow: true);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("/migrations", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.DoesNotContain("/preview", handler.Requests[0].RequestUri!.AbsolutePath);

        Assert.Contains("\"preserve_period\":true", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ApplyLaterCallsUpdateSubscriptionWithProductChangeDelayed()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "subscription": { "id": 4001, "state": "active",
                "product": { "id": 7127071, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 } } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var updated = await client.CommitPlanChangeAsync(4001, "basic-plan", applyNow: false);

        Assert.Equal("basic-plan", updated.PlanHandle);
        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.DoesNotContain("migrations", handler.Requests[0].RequestUri!.AbsolutePath);

        Assert.Contains("\"product_change_delayed\":true", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenTheImmediateMigrationIsRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Cannot migrate to the current product"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CommitPlanChangeAsync(4001, "eshop-pro", applyNow: true));

        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenTheDelayedChangeIsRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product must exist"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CommitPlanChangeAsync(4001, "unknown", applyNow: false));

        Assert.Equal(422, ex.StatusCode);
    }
}
