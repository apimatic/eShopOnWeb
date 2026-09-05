using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private static readonly MaxioCustomerProfile Profile = new(
        Reference: "demouser@microsoft.com",
        Email: "demouser@microsoft.com",
        FirstName: "Demo",
        LastName: "User");

    private const string ProductJson = """
    { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
    """;

    private const string SubscriptionJson = """
    {
      "subscription": {
        "state": "active",
        "next_assessment_at": "2026-10-05T00:00:00Z",
        "product_price_in_cents": 29900,
        "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" },
        "customer": { "id": 555 }
      }
    }
    """;

    [Fact]
    public async Task CreatesCustomerAndSubscription_WhenNeitherExistsYet()
    {
        const string customerJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";

        var (service, handler) = MaxioTestClientFactory.Create(MaxioTestClientFactory.Sequenced(
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, ProductJson),                 // ReadProductByHandle
            MaxioTestClientFactory.Respond(HttpStatusCode.NotFound, "\"not found\""),        // ReadCustomerByReference (miss)
            MaxioTestClientFactory.Respond(HttpStatusCode.Created, customerJson),            // CreateCustomer
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, "[]"),                         // ListCustomerSubscriptions
            MaxioTestClientFactory.Respond(HttpStatusCode.Created, SubscriptionJson)));       // CreateSubscription

        var result = await service.SubscribeAsync(Profile, "eshop-pro");

        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task ReusesExistingCustomer_WhenReferenceAlreadyExists()
    {
        const string customerJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";

        var (service, handler) = MaxioTestClientFactory.Create(MaxioTestClientFactory.Sequenced(
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, ProductJson),           // ReadProductByHandle
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, customerJson),          // ReadCustomerByReference (hit)
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, "[]"),                  // ListCustomerSubscriptions
            MaxioTestClientFactory.Respond(HttpStatusCode.Created, SubscriptionJson))); // CreateSubscription

        var result = await service.SubscribeAsync(Profile, "eshop-pro");

        Assert.Equal("active", result.State);
        // No POST to create a customer: only the one POST for CreateSubscription.
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ReturnsExistingSubscription_WhenAlreadyLiveOnThatPlan_WithoutCreatingASecondOne()
    {
        const string customerJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";
        const string existingSubscriptionsJson = $"[{SubscriptionJson}]";

        var (service, handler) = MaxioTestClientFactory.Create(MaxioTestClientFactory.Sequenced(
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, ProductJson),
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, customerJson),
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, existingSubscriptionsJson)));

        var result = await service.SubscribeAsync(Profile, "eshop-pro");

        Assert.Equal("active", result.State);
        // The double-click never reaches CreateSubscription at all.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ReconcilesExistingCustomer_WhenCreateCustomerRacesADuplicateReference()
    {
        const string customerJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";

        var (service, handler) = MaxioTestClientFactory.Create(MaxioTestClientFactory.Sequenced(
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, ProductJson),                          // ReadProductByHandle
            MaxioTestClientFactory.Respond(HttpStatusCode.NotFound, "\"not found\""),                 // ReadCustomerByReference (miss)
            MaxioTestClientFactory.Respond(HttpStatusCode.UnprocessableEntity, """{ "errors": {} }"""), // CreateCustomer races a duplicate
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, customerJson),                          // reconciliation re-read (hit)
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, "[]"),                                  // ListCustomerSubscriptions
            MaxioTestClientFactory.Respond(HttpStatusCode.Created, SubscriptionJson)));                // CreateSubscription

        var result = await service.SubscribeAsync(Profile, "eshop-pro");

        Assert.Equal("active", result.State);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.ToString().Contains("customer", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WrapsAConnectionFailure_InsteadOfLeakingTheTransportException()
    {
        var (service, _) = MaxioTestClientFactory.Create(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<MaxioProviderException>(() => service.SubscribeAsync(Profile, "eshop-pro"));
    }

    [Fact]
    public async Task ThrowsPlanNotFound_WhenTheHandleIsUnknown()
    {
        var (service, _) = MaxioTestClientFactory.Create(_ => MaxioTestClientFactory.JsonResponse(HttpStatusCode.NotFound, "\"no such product\""));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Profile, "no-such-plan"));
    }
}
