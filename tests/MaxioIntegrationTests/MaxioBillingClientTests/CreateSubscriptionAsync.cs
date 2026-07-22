using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class CreateSubscriptionAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    private static BillingCustomer Customer() =>
        new(5551212, "demouser@microsoft.com", "demouser@microsoft.com", "demouser", "microsoft");

    [Fact]
    public async Task ReturnsAnActiveSubscriptionCarryingThePlanAndTheNextBillingDate()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        var subscription = await BillingClientFixture.Create(_handler)
            .CreateSubscriptionAsync(Customer(), "eshop-pro");

        Assert.Equal(90210, subscription.ProviderSubscriptionId);
        Assert.Equal(5551212, subscription.ProviderCustomerId);
        Assert.Equal("demouser@microsoft.com", subscription.CustomerReference);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        Assert.Equal(299.00m, subscription.Plan.Price);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)),
            subscription.CurrentPeriodEndsAt);
        Assert.Equal(subscription.CurrentPeriodEndsAt, subscription.NextBillingAt);
    }

    [Fact]
    public async Task IdentifiesThePlanByHandleAndTheCustomerByIdOnTheWire()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(ProviderPayloads.Subscription()));

        await BillingClientFixture.Create(_handler).CreateSubscriptionAsync(Customer(), "eshop-pro");

        var request = _handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
        Assert.Contains("\"customer_id\":5551212", request.Body);
        // The demo plans need no card, so the subscription is invoiced rather than auto-charged.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", request.Body);
    }

    [Fact]
    public async Task MapsTrialingOntoALiveSubscription()
    {
        _handler.RespondWithJson(ProviderPayloads.SubscriptionResponse(
            ProviderPayloads.Subscription(state: "trialing")));

        var subscription = await BillingClientFixture.Create(_handler)
            .CreateSubscriptionAsync(Customer(), "eshop-pro");

        Assert.Equal(SubscriptionState.Trialing, subscription.State);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWithItsOwnValidationMessages()
    {
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity, ProviderPayloads.ValidationErrors);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).CreateSubscriptionAsync(Customer(), "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product handle is invalid.", exception.ProviderMessage);
        Assert.Contains("Coupon not found.", exception.ProviderMessage);
    }

    [Fact]
    public async Task RefusesAnEmptyPlanHandleBeforeCallingTheProvider()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => BillingClientFixture.Create(_handler).CreateSubscriptionAsync(Customer(), ""));

        Assert.Empty(_handler.Requests);
    }
}
