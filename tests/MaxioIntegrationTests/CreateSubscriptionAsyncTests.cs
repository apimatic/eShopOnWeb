using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class CreateSubscriptionAsyncTests
{
    [Fact]
    public async Task CreatesSubscriptionByCustomerReferenceAndMapsTheResult()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "product": { "id": 7127070, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "require_credit_card": false } }"""),
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "subscription": { "id": 2001, "state": "active", "cancel_at_end_of_period": false,
                "product": { "id": 7127070, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscription = await client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro");

        Assert.Equal(2001, subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("shopper@example.com", subscription.CustomerReference);

        var createBody = handler.RequestBodies[1];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_reference\":\"shopper@example.com\"", createBody);
        // Confirmed live against the sandbox: without this, Maxio declines signup with "No
        // payment method was on file" even though the product does not require a card.
        Assert.Contains("\"payment_collection_method\":\"invoice\"", createBody);
        Assert.DoesNotContain("payment_profile", createBody);
        Assert.DoesNotContain("credit_card", createBody);
    }

    [Fact]
    public async Task ThrowsBillingConfigurationExceptionWithoutEnrollingWhenPlanRequiresPaymentMethod()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "product": { "id": 7127070, "handle": "eshop-pro", "require_credit_card": true } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro"));

        Assert.Contains("eshop-pro", ex.Message);
        // Only the product read happened — enrollment must never be attempted in this case.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ThrowsBillingConfigurationExceptionWhenPlanHandleDoesNotResolve()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.NotFound));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.CreateSubscriptionAsync("shopper@example.com", "unknown-plan"));

        Assert.Contains("unknown-plan", ex.Message);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWithJoinedMessagesWhenEnrollmentIsRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "product": { "id": 7127070, "handle": "eshop-pro", "require_credit_card": false } }"""),
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Customer can't be blank", "Product must be active"] }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Customer can't be blank", ex.Message);
        Assert.Contains("Product must be active", ex.Message);
    }
}
