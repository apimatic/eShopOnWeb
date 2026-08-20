using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingGatewayTests
{
    [Fact]
    public async Task ListsOnlyActiveProductsFromConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/products.json?page=1&per_page=200", request.RequestUri!.PathAndQuery);
            return Json(HttpStatusCode.OK, """
                [
                  { "product": { "id": 1, "handle": "basic", "name": "Basic", "description": "Starter", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "archived_at": null, "product_price_point_name": "Default", "product_family": { "handle": "eshop" } } },
                  { "product": { "id": 2, "handle": "archived", "name": "Old", "description": "", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2024-01-01T00:00:00Z", "product_price_point_name": "Default", "product_family": { "handle": "eshop" } } },
                  { "product": { "id": 3, "handle": "other", "name": "Other", "description": "", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": null, "product_price_point_name": "Default", "product_family": { "handle": "another-family" } } }
                ]
                """);
        });
        var gateway = CreateGateway(handler);

        var plans = await gateway.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("basic", plan.Handle);
        Assert.Equal(2900, plan.PriceInCents);
    }

    [Fact]
    public async Task RepeatedCustomerAndSubscriptionEnrollmentPostsOnlyOnce()
    {
        var customerExists = false;
        var subscriptionExists = false;
        var customerPosts = 0;
        var subscriptionPosts = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return customerExists
                    ? Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "reference": "customer-ref" } }""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                customerPosts++;
                customerExists = true;
                AssertJsonProperty(request, "customer", "reference", "customer-ref");
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "reference": "customer-ref" } }""");
            }

            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                return subscriptionExists
                    ? SubscriptionResponse()
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                subscriptionPosts++;
                subscriptionExists = true;
                AssertJsonProperty(request, "subscription", "product_handle", "basic");
                AssertJsonNumber(request, "subscription", "customer_id", 42);
                AssertJsonProperty(request, "subscription", "reference", "subscription-ref");
                AssertJsonProperty(request, "subscription", "payment_collection_method", "remittance");
                return SubscriptionResponse(HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var gateway = CreateGateway(handler);
        var user = new BillingUser("user-id", "shopper@example.com");

        var customer1 = await gateway.EnsureCustomerAsync(user, "customer-ref", CancellationToken.None);
        var customer2 = await gateway.EnsureCustomerAsync(user, "customer-ref", CancellationToken.None);
        var subscription1 = await gateway.EnsureSubscriptionAsync(
            "basic", 42, "subscription-ref", CancellationToken.None);
        var subscription2 = await gateway.EnsureSubscriptionAsync(
            "basic", 42, "subscription-ref", CancellationToken.None);

        Assert.Equal(customer1.Id, customer2.Id);
        Assert.Equal(subscription1.Id, subscription2.Id);
        Assert.Equal(1, customerPosts);
        Assert.Equal(1, subscriptionPosts);
        Assert.Equal("active", subscription2.State);
        Assert.Equal(2900, subscription2.PriceInCents);
        Assert.NotNull(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "not-a-real-api-key",
            Subdomain = "unused",
            ProductFamilyHandle = "eshop",
            BaseUrl = "https://maxio.test"
        });
        return new MaxioBillingGateway(new HttpClient(handler), options);
    }

    private static HttpResponseMessage SubscriptionResponse(HttpStatusCode status = HttpStatusCode.OK) =>
        Json(status, """
            {
              "subscription": {
                "id": 99,
                "reference": "subscription-ref",
                "state": "active",
                "product_price_in_cents": 2900,
                "next_assessment_at": "2026-09-20T12:00:00Z",
                "current_period_ends_at": "2026-09-20T12:00:00Z",
                "product": {
                  "id": 1,
                  "handle": "basic",
                  "name": "Basic",
                  "description": "Starter",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month",
                  "archived_at": null,
                  "product_price_point_name": "Default",
                  "product_family": { "handle": "eshop" }
                }
              }
            }
            """);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = JsonContent.Create(JsonSerializer.Deserialize<JsonElement>(body))
    };

    private static void AssertJsonProperty(
        HttpRequestMessage request,
        string container,
        string property,
        string expected)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expected, document.RootElement.GetProperty(container).GetProperty(property).GetString());
    }

    private static void AssertJsonNumber(
        HttpRequestMessage request,
        string container,
        string property,
        long expected)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expected, document.RootElement.GetProperty(container).GetProperty(property).GetInt64());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }
}
