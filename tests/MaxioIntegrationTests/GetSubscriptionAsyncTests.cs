using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class GetSubscriptionAsyncTests
{
    [Fact]
    public async Task MapsAllFieldsIncludingCancelAtEndOfPeriod()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "subscription": { "id": 3001, "state": "canceled", "cancel_at_end_of_period": true,
                "current_period_ends_at": "2026-09-15T00:00:00Z",
                "customer": { "reference": "shopper@example.com" },
                "product": { "id": 7127071, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 } } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.GetSubscriptionAsync(3001);

        Assert.Equal(3001, subscription.Id);
        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.Equal(29.00m, subscription.Price);
        Assert.Equal("shopper@example.com", subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), subscription.CurrentPeriodEndsAt);
    }

    [Fact]
    public async Task ThrowsSubscriptionNotFoundExceptionForAnUnknownId()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.NotFound));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => client.GetSubscriptionAsync(999999));

        Assert.Equal(999999, ex.SubscriptionId);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionOnAnUnrelatedServerError()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.ServiceUnavailable));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(3001));

        Assert.Equal(503, ex.StatusCode);
    }
}
