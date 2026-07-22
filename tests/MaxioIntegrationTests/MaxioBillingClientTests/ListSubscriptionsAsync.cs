using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class ListSubscriptionsAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    private static BillingCustomer Customer() =>
        new(5551212, "demouser@microsoft.com", "demouser@microsoft.com", "demouser", "microsoft");

    [Fact]
    public async Task ReturnsEverySubscriptionTheCustomerHolds()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionList(
            ProviderPayloads.Subscription(),
            ProviderPayloads.Subscription(state: "canceled", product: ProviderPayloads.BasicPlanProduct)));

        var subscriptions = await BillingClientFixture.Create(_handler).ListSubscriptionsAsync(Customer());

        Assert.Equal(2, subscriptions.Count);
        Assert.Single(subscriptions, s => s.IsActive);
        Assert.Contains(subscriptions, s => s.State == SubscriptionState.Canceled);
        Assert.Contains("/customers/5551212/subscriptions.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionForACustomerWhoHasNeverSubscribed()
    {
        _handler.RespondWithJson("[]");

        var subscriptions = await BillingClientFixture.Create(_handler).ListSubscriptionsAsync(Customer());

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task SurfacesAProviderFailureAsATypedBillingFailure()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.InternalServerError, "\"boom\"");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).ListSubscriptionsAsync(Customer()));

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task ReadsASingleSubscriptionById()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        var subscription = await BillingClientFixture.Create(_handler).GetSubscriptionAsync(90210);

        Assert.Equal(90210, subscription.ProviderSubscriptionId);
        Assert.Contains("/subscriptions/90210.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ReportsAnUnknownSubscriptionIdRatherThanReturningNothing()
    {
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).GetSubscriptionAsync(404404));

        Assert.Equal(404, exception.StatusCode);
    }
}
