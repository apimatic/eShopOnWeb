using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.MaxioSubscriptionBillingServiceTests;

public class ListSubscriptionsAsync
{
    private static Subscriber Shopper() => new Subscriber("shopper@example.com", "shopper@example.com");

    [Fact]
    public async Task ReturnsAnEmptyListWhenTheShopperHasNoBillingCustomerYet()
    {
        var transport = new StubTransport(_ => StubTransport.Json(HttpStatusCode.NotFound, "{}"));

        var subscriptions = await MaxioTestHarness.CreateService(transport).ListSubscriptionsAsync(Shopper());

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReportsPlanPriceStateAndNextBillingDate()
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json"))
            {
                return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            }

            return StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                MaxioTestHarness.SubscriptionJson(9001, "active", "pro-plan", 29900)));
        });

        var subscription = Assert.Single(await MaxioTestHarness.CreateService(transport).ListSubscriptionsAsync(Shopper()));

        Assert.Equal(9001, subscription.Id);
        Assert.Equal("pro-plan", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsLive);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 16, 47, 12, TimeSpan.FromHours(5)), subscription.NextBillingDate);
        Assert.Equal("eshoponweb-shopper@example.com", subscription.CustomerReference);
    }

    [Fact]
    public async Task MarksAnEndedSubscriptionAsNotLive()
    {
        var transport = new StubTransport(request =>
        {
            if (request.Matches(HttpMethod.Get, "/customers/lookup.json"))
            {
                return StubTransport.Ok(MaxioTestHarness.CustomerJson(500, "eshoponweb-shopper@example.com"));
            }

            return StubTransport.Ok(MaxioTestHarness.SubscriptionsJson(
                MaxioTestHarness.SubscriptionJson(9001, "canceled", "pro-plan", 29900)));
        });

        var subscription = Assert.Single(await MaxioTestHarness.CreateService(transport).ListSubscriptionsAsync(Shopper()));

        Assert.False(subscription.IsLive);
    }

    [Fact]
    public async Task DoesNotTreatAnUnreadableLookupResponseAsAMissingCustomer()
    {
        // "I could not read the answer" is not "the provider said no": mapping it onto an absence
        // would turn a corrupt response into an empty account page - or, on the subscribe path,
        // into a duplicate customer.
        var transport = new StubTransport(_ => StubTransport.Ok("{ not json at all"));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).ListSubscriptionsAsync(Shopper()));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task ReportsAProviderOutageAsServiceUnavailable()
    {
        var transport = new StubTransport(_ => StubTransport.Json(HttpStatusCode.InternalServerError, "{}"));

        var exception = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => MaxioTestHarness.CreateService(transport).ListSubscriptionsAsync(Shopper()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, exception.ProviderStatusCode);
    }
}
