using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class CreateSubscription
{
    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task EnrolsTheCustomerInThePlanAndReturnsTheActiveSubscription()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created,
            MaxioPayloads.Subscription());

        var subscription = await _builder.Build().CreateSubscriptionAsync(88001, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);

        var request = Assert.Single(_builder.Handler.Requests);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
        Assert.Contains("\"customer_id\":88001", request.Body);
    }

    [Fact]
    public async Task SendsTheConfiguredCollectionMethodSoNoCardIsRequired()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created,
            MaxioPayloads.Subscription());

        await _builder.Build().CreateSubscriptionAsync(88001, "eshop-pro");

        // The demo plans capture no card. Relying on the provider's "automatic" default makes it
        // refuse the enrollment with "No payment method was on file for the $299.00 balance".
        Assert.Contains("\"payment_collection_method\":\"remittance\"",
            Assert.Single(_builder.Handler.Requests).Body);
    }

    [Fact]
    public async Task OmitsTheCollectionMethodWhenItIsConfiguredEmpty()
    {
        _builder.Settings.PaymentCollectionMethod = string.Empty;
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created,
            MaxioPayloads.Subscription());

        await _builder.Build().CreateSubscriptionAsync(88001, "eshop-pro");

        // Configuring it away must fall back to the provider's own default, not send an empty value.
        Assert.DoesNotContain("payment_collection_method", Assert.Single(_builder.Handler.Requests).Body);
    }

    [Fact]
    public async Task SurfacesTheProvidersRejectionAsATypedException()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions.json",
            HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorList);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("CreateSubscriptionAsync", exception.Operation);
        Assert.Contains("Product: could not be found.", exception.Errors);
        Assert.Contains("Subscription must be active", exception.Errors);
        Assert.Contains("Product: could not be found.", exception.Message);
    }

    [Fact]
    public async Task ThrowsWhenTheProviderAcceptsTheCallButReturnsNoSubscription()
    {
        _builder.Handler.RespondWith(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, "{}");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Contains("no subscription", exception.Message);
    }
}
