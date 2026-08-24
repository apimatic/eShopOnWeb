using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string CustomerReference = "demouser@microsoft.com";

    [Fact]
    public async Task SubscribeReturnsExistingLiveSubscriptionWithoutCreating()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", ProductsJson)
            .Respond(HttpMethod.Get, "/customers/lookup.json?reference=demouser%40microsoft.com", CustomerJson(id: 7))
            .Respond(HttpMethod.Get, "/customers/7/subscriptions.json", SubscriptionsJson(id: 42, handle: "eshop-pro", state: "active"));

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(42, result.Subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", ProductsJson)
            .Respond(HttpMethod.Get, "/customers/lookup.json?reference=demouser%40microsoft.com", "{}", HttpStatusCode.NotFound)
            .Respond(HttpMethod.Post, "/customers.json", CustomerJson(id: 9), HttpStatusCode.Created)
            .Respond(HttpMethod.Get, "/customers/9/subscriptions.json", "[]")
            .Respond(HttpMethod.Post, "/subscriptions.json", SubscriptionJson(id: 55, handle: "eshop-pro", state: "active"), HttpStatusCode.Created);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);

        var createCustomer = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/customers.json");
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", createCustomer.Body);

        var createSubscription = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json");
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createSubscription.Body);
        Assert.Contains("\"customer_id\":9", createSubscription.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createSubscription.Body);
    }

    [Fact]
    public async Task SubscribeResubscribesWhenExistingSubscriptionIsTerminated()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", ProductsJson)
            .Respond(HttpMethod.Get, "/customers/lookup.json?reference=demouser%40microsoft.com", CustomerJson(id: 7))
            .Respond(HttpMethod.Get, "/customers/7/subscriptions.json", SubscriptionsJson(id: 42, handle: "eshop-pro", state: "canceled"))
            .Respond(HttpMethod.Post, "/subscriptions.json", SubscriptionJson(id: 56, handle: "eshop-pro", state: "active"), HttpStatusCode.Created);

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(CustomerReference, CustomerReference, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(56, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeThrowsForUnknownPlan()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", ProductsJson);

        var service = CreateService(handler);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(CustomerReference, CustomerReference, "no-such-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenCustomerMissing()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/customers/lookup.json?reference=nobody%40microsoft.com", "{}", HttpStatusCode.NotFound);

        var service = CreateService(handler);

        var result = await service.ListSubscriptionsAsync("nobody@microsoft.com");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListPlansMapsProductsFromConfiguredFamily()
    {
        var handler = new StubHttpMessageHandler()
            .Respond(HttpMethod.Get, "/product_families/handle:eshop-subscribe/products.json", ProductsJson);

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(2900, plans[0].PriceInCents);
        Assert.Equal("eshop-pro", plans[1].Handle);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new System.Uri("https://tests.chargify.com/") };
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "tests",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionBillingService(
            new MaxioApiClient(httpClient),
            settings,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private const string ProductsJson = """
        [
          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "description": null, "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "description": null, "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "archived_at": null } }
        ]
        """;

    private static string CustomerJson(long id) => $$"""
        { "customer": { "id": {{id}}, "first_name": "demouser", "last_name": "User", "email": "demouser@microsoft.com", "reference": "demouser@microsoft.com" } }
        """;

    private static string SubscriptionJson(long id, string handle, string state) => $$"""
        { "subscription": { "id": {{id}}, "state": "{{state}}", "created_at": "2026-08-24T12:00:00Z", "activated_at": "2026-08-24T12:00:00Z", "current_period_ends_at": "2026-09-24T12:00:00Z", "product": { "id": 1, "name": "Pro Plan", "handle": "{{handle}}", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } }
        """;

    private static string SubscriptionsJson(long id, string handle, string state) =>
        "[" + SubscriptionJson(id, handle, state) + "]";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpMethod Method, string Path, string Body, HttpStatusCode Status)> _responses = new();

        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

        public StubHttpMessageHandler Respond(HttpMethod method, string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses.Enqueue((method, path, body, status));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, requestBody));

            Assert.True(_responses.Count > 0, $"Unexpected request: {request.Method} {request.RequestUri}");
            var next = _responses.Dequeue();
            Assert.Equal(next.Method, request.Method);
            Assert.Equal(next.Path, request.RequestUri!.PathAndQuery);

            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
