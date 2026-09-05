using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static readonly MaxioCustomerIdentity Shopper =
        new(Reference: "demouser@microsoft.com", Email: "demouser@microsoft.com", FirstName: "demouser", LastName: "Shopper");

    private const string CustomerFoundJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com" } }""";
    private const string CustomerCreatedJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com" } }""";
    private const string NoSubscriptionsJson = "[]";

    private const string ActiveProSubscriptionJson = """
        [
          {
            "subscription": {
              "id": 999,
              "state": "active",
              "next_assessment_at": "2026-10-05T00:00:00Z",
              "current_billing_amount_in_cents": 29900,
              "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
            }
          }
        ]
        """;

    private const string CreatedSubscriptionJson = """
        {
          "subscription": {
            "id": 1001,
            "state": "active",
            "next_assessment_at": "2026-10-05T00:00:00Z",
            "current_billing_amount_in_cents": 29900,
            "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
          }
        }
        """;

    private const string PlansJson = """
        [
          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    private static (MaxioSubscriptionService Service, QueueHandler Handler) BuildService(
        params (HttpStatusCode Status, string Json)[] responses)
    {
        var handler = new QueueHandler(responses);
        var httpClient = new System.Net.Http.HttpClient(handler);
        var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return (new MaxioSubscriptionService(client, options), handler);
    }

    [Fact]
    public async Task GetAvailablePlansAsync_ReturnsMappedPlans()
    {
        var (service, _) = BuildService((HttpStatusCode.OK, PlansJson));

        var plans = await service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerAndPlanAreNew_CreatesCustomerAndSubscription()
    {
        var (service, handler) = BuildService(
            (HttpStatusCode.NotFound, ""),                 // ReadCustomerByReference: not found
            (HttpStatusCode.OK, CustomerCreatedJson),       // CreateCustomer
            (HttpStatusCode.OK, NoSubscriptionsJson),       // ListCustomerSubscriptions: none yet
            (HttpStatusCode.OK, CreatedSubscriptionJson));  // CreateSubscription

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(1001, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task SubscribeAsync_WhenActiveSubscriptionToSamePlanExists_ReturnsExistingWithoutCreatingDuplicate()
    {
        var (service, handler) = BuildService(
            (HttpStatusCode.OK, CustomerFoundJson),           // ReadCustomerByReference: found
            (HttpStatusCode.OK, ActiveProSubscriptionJson));  // ListCustomerSubscriptions: already subscribed

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(999, result.SubscriptionId);
        // Exactly the 2 read calls above - no POST to create a second subscription.
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Method == System.Net.Http.HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCreateCustomerRaces422_RetriesLookupInsteadOfFailing()
    {
        var (service, handler) = BuildService(
            (HttpStatusCode.NotFound, ""),                                     // ReadCustomerByReference: not found
            (HttpStatusCode.UnprocessableEntity, """{ "errors": {} }"""),      // CreateCustomer: 422 (concurrent duplicate)
            (HttpStatusCode.OK, CustomerFoundJson),                            // Re-check: the concurrent create won
            (HttpStatusCode.OK, NoSubscriptionsJson),                          // ListCustomerSubscriptions
            (HttpStatusCode.OK, CreatedSubscriptionJson));                     // CreateSubscription

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(1001, result.SubscriptionId);
        Assert.Equal(5, handler.Requests.Count);
    }

    private sealed class QueueHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json)> _responses;
        public List<System.Net.Http.HttpRequestMessage> Requests { get; } = new();

        public QueueHandler(IEnumerable<(HttpStatusCode Status, string Json)> responses) => _responses = new(responses);

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, json) = _responses.Dequeue();
            var response = new System.Net.Http.HttpResponseMessage(status);
            if (!string.IsNullOrEmpty(json))
            {
                response.Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }
}
