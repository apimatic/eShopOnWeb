using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioSubscriptionBillingServiceTests;

public class ListSubscriptions
{
    private static readonly BillingCustomerIdentity Shopper =
        BillingCustomerIdentity.ForUser("demouser@microsoft.com");

    [Fact]
    public async Task ReturnsNothingForAShopperWhoHasNeverSubscribed()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(customerExists: false));

        var subscriptions = await service.ListSubscriptionsAsync(Shopper);

        // No billing customer is an empty list, not a failure — and nothing may be created just by looking.
        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task ReportsPlanPriceStateAndNextBillingDate()
    {
        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            existingSubscriptionsJson: MaxioTestHost.LiveSubscriptionListJson));

        var subscription = Assert.Single(await service.ListSubscriptionsAsync(Shopper));

        Assert.Equal(94208636, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 11, 37, 45, TimeSpan.FromHours(5)), subscription.NextBillingDate);
    }

    [Fact]
    public async Task IncludesTerminatedSubscriptionsSoTheShopperSeesTheirWholeHistory()
    {
        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            existingSubscriptionsJson: MaxioTestHost.CanceledSubscriptionListJson));

        var subscription = Assert.Single(await service.ListSubscriptionsAsync(Shopper));

        Assert.Equal("canceled", subscription.State);
        Assert.Null(subscription.NextBillingDate);
    }

    [Fact]
    public async Task FailsRatherThanReportingAnEmptyListWhenTheLookupIsUnavailable()
    {
        // "I could not read the answer" must never be presented as "you have no subscriptions".
        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            onCustomerLookup: _ => MaxioStubHandler.Json(HttpStatusCode.InternalServerError, "boom")));

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.ListSubscriptionsAsync(Shopper));

        Assert.Equal(BillingFailureKind.Unavailable, exception.Kind);
    }

    [Fact]
    public async Task NeverRendersABlankPlanNameWhenTheProviderOmitsTheNestedProduct()
    {
        const string withoutProduct = """
            [{"subscription":{"id":94208636,"state":"active","product_price_in_cents":29900,"customer":{"id":42}}}]
            """;

        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            existingSubscriptionsJson: withoutProduct));

        var subscription = Assert.Single(await service.ListSubscriptionsAsync(Shopper));

        Assert.False(string.IsNullOrWhiteSpace(subscription.PlanName));
    }
}
