using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task EndpointsRequireBearerToken()
    {
        using var factory = CreateFactory(new MaxioStubHandler());
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("api/subscription-plans");
        var subscriptions = await client.GetAsync("api/my-subscriptions");
        var create = await client.PostAsync(
            "api/subscriptions",
            JsonContent(new { productHandle = "eshop-pro" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode, await plans.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode, await subscriptions.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode, await create.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task ConcurrentDuplicateSubscribeCreatesOneCustomerAndOneSubscription()
    {
        var handler = new MaxioStubHandler();
        using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode, await plansResponse.Content.ReadAsStringAsync());

        var first = client.PostAsync(
            "api/subscriptions",
            JsonContent(new { productHandle = "eshop-pro" }));
        var second = client.PostAsync(
            "api/subscriptions",
            JsonContent(new { productHandle = "eshop-pro" }));
        var responses = await Task.WhenAll(first, second);

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, handler.CustomerCreateCount);
        Assert.AreEqual(1, handler.SubscriptionCreateCount);
        Assert.IsTrue(handler.RequestBodies.Any(body =>
            body.Contains("\"product_handle\":\"eshop-pro\"", StringComparison.Ordinal)));
        Assert.IsTrue(handler.RequestBodies.Any(body =>
            body.Contains("\"payment_collection_method\":\"remittance\"", StringComparison.Ordinal)));
        Assert.IsFalse(handler.RequestBodies.Any(body =>
            body.Contains("product_id", StringComparison.Ordinal)));

        var accountResponse = await client.GetAsync("api/my-subscriptions");
        accountResponse.EnsureSuccessStatusCode();
        var accountJson = await accountResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(accountJson);
        var subscriptions = document.RootElement.EnumerateArray().ToArray();

        Assert.AreEqual(1, subscriptions.Length);
        Assert.AreEqual("eshop-pro", subscriptions[0].GetProperty("planHandle").GetString());
        Assert.AreEqual(29900L, subscriptions[0].GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("active", subscriptions[0].GetProperty("state").GetString());
        Assert.AreEqual(
            "2026-10-03T12:00:00+00:00",
            subscriptions[0].GetProperty("nextBillingAt").GetDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    private static WebApplicationFactory<Program> CreateFactory(MaxioStubHandler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<MaxioAdvancedBillingClient>();
                services.AddSingleton(CreateClient(handler));
            });
        });

    private static MaxioAdvancedBillingClient CreateClient(HttpMessageHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(2) }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private int _customerExists;
        private int _subscriptionExists;
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;
        public ConcurrentBag<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;

            if (request.Method == HttpMethod.Get && path.Contains("/product_families/", StringComparison.Ordinal))
            {
                response = Json(HttpStatusCode.OK, PlanListJson);
            }
            else if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                response = Volatile.Read(ref _customerExists) == 1
                    ? Json(HttpStatusCode.OK, CustomerJson)
                    : Json(HttpStatusCode.NotFound, "{}");
            }
            else if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                Interlocked.Exchange(ref _customerExists, 1);
                Interlocked.Increment(ref _customerCreateCount);
                response = Json(HttpStatusCode.Created, CustomerJson);
            }
            else if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                response = Volatile.Read(ref _subscriptionExists) == 1
                    ? Json(HttpStatusCode.OK, SubscriptionJson)
                    : Json(HttpStatusCode.NotFound, "{}");
            }
            else if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                Interlocked.Exchange(ref _subscriptionExists, 1);
                Interlocked.Increment(ref _subscriptionCreateCount);
                response = Json(HttpStatusCode.Created, SubscriptionJson);
            }
            else if (request.Method == HttpMethod.Get && path == "/customers/42/subscriptions.json")
            {
                response = Json(
                    HttpStatusCode.OK,
                    Volatile.Read(ref _subscriptionExists) == 1 ? $"[{SubscriptionJson}]" : "[]");
            }
            else
            {
                response = Json(HttpStatusCode.NotFound, "{}");
            }

            response.RequestMessage = request;
            return response;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private const string PlanListJson = """
            [{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","description":"Pro subscription","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"default_product_price_point_id":1,"product_price_point_id":1,"product_price_point_handle":"default"}}]
            """;

        private const string CustomerJson = """
            {"customer":{"id":42,"first_name":"demo","last_name":"Customer","email":"demouser@microsoft.com","reference":"test-customer"}}
            """;

        private const string SubscriptionJson = """
            {"subscription":{"id":84,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-10-03T12:00:00Z","next_assessment_at":"2026-10-03T12:00:00Z","reference":"test-subscription","currency":"USD","product_price_point_id":1,"customer":{"id":42,"reference":"test-customer"},"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}}
            """;
    }
}
