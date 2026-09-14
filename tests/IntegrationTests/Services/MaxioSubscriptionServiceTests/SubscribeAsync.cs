using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private static readonly SubscribingCustomer Shopper = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "Customer"
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNeitherExists()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.NotFound, ""),                                          // ReadCustomerByReference: no customer yet
            (HttpStatusCode.OK, """{"customer": {"id": 55, "reference": "user-1"}}"""), // CreateCustomer
            (HttpStatusCode.NotFound, ""),                                          // FindSubscription: no existing subscription
            (HttpStatusCode.OK, """
                {"subscription": {"id": 900, "state": "active", "next_assessment_at": "2026-10-05T00:00:00Z",
                 "current_period_ends_at": "2026-11-05T00:00:00Z",
                 "product": {"handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900}}}
                """));                                                              // CreateSubscription

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(900, result.MaxioSubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task DoubleSubscribeReturnsExistingSubscriptionWithoutCreatingADuplicate()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.OK, """{"customer": {"id": 55, "reference": "user-1"}}"""),   // customer already exists
            (HttpStatusCode.OK, """
                {"subscription": {"id": 900, "state": "active", "product": {"handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900}}}
                """));                                                                     // FindSubscription finds the earlier one

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(900, result.MaxioSubscriptionId);
        // Only the two lookups happened - no CreateSubscription call, so no duplicate was created.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RecoversFromACustomerReferenceRaceOn422()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.NotFound, ""),                                            // ReadCustomerByReference: not found yet
            (HttpStatusCode.UnprocessableEntity, "{}"),                               // CreateCustomer: lost the race (reference now taken)
            (HttpStatusCode.OK, """{"customer": {"id": 55, "reference": "user-1"}}"""),// re-lookup finds the winner
            (HttpStatusCode.NotFound, ""),                                            // FindSubscription: none yet
            (HttpStatusCode.OK, """
                {"subscription": {"id": 900, "state": "active", "product": {"handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900}}}
                """));

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(900, result.MaxioSubscriptionId);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task InvalidPlanHandleSurfacesAsACallerFacingError()
    {
        var (service, _) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.OK, """{"customer": {"id": 55, "reference": "user-1"}}"""),
            (HttpStatusCode.NotFound, ""),
            (HttpStatusCode.UnprocessableEntity, """{"errors": ["Product handle is not valid"]}"""));

        var ex = await Assert.ThrowsAsync<MaxioIntegrationException>(
            () => service.SubscribeAsync(Shopper, "not-a-real-plan"));

        Assert.Contains("Product handle is not valid", ex.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.ProviderStatusCode);
    }
}
