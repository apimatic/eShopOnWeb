using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Drives the subscription endpoints through the real PublicApi host, with Maxio replaced by an
/// in-process fake behind the "Maxio" named HttpClient.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient CreateClient(FakeMaxio maxio, string? token)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-api-key",
                ["Maxio:Subdomain"] = "test-site",
                ["Maxio:ProductFamilyHandle"] = "test-family",
                ["Maxio:BaseUrl"] = "https://maxio.example.test",
                ["Maxio:DefaultPlanHandle"] = "pro",
                ["Maxio:PlanCacheSeconds"] = "0"
            }));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => maxio));
        });
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task EndpointsRequireAuthentication()
    {
        var client = CreateClient(new FakeMaxio(), token: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", Json(new { planHandle = "pro" }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        var client = CreateClient(new FakeMaxio(), ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsTrue(model!.IsComplete);
        Assert.AreEqual("pro", model.DefaultPlanHandle);
        var pro = model.Plans.Single(p => p.Handle == "pro");
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotent_AndShowsInMySubscriptions()
    {
        var maxio = new FakeMaxio();
        var client = CreateClient(maxio, ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", Json(new { planHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("created", created!.Status);
        Assert.AreEqual("active", created.Subscription!.State);
        Assert.AreEqual(299.00m, created.Subscription.Price);
        Assert.IsNotNull(created.Subscription.NextBillingAt);

        var second = await client.PostAsync("api/subscriptions", Json(new { planHandle = "pro" }));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var again = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("already_subscribed", again!.Status);
        Assert.AreEqual(created.Subscription.Id, again.Subscription!.Id);

        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        var mine = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode, await mine.Content.ReadAsStringAsync());
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(created.Subscription.Id, list!.Subscriptions.Single().Id);
        Assert.IsNull(list.PendingPlanHandle);
    }

    [TestMethod]
    public async Task ParallelDoubleSubmit_CreatesOneSubscription()
    {
        var maxio = new FakeMaxio();
        var client = CreateClient(maxio, ApiTokenHelper.GetAdminUserToken());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync("api/subscriptions", Json(new { planHandle = "basic" }))));

        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict),
            string.Join(",", responses.Select(r => (int)r.StatusCode)));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    }

    [TestMethod]
    public async Task UnknownPlan_IsBadRequest_AndNothingIsCreated()
    {
        var maxio = new FakeMaxio();
        var client = CreateClient(maxio, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ProviderUnauthorized_IsServiceUnavailable_NotLeaked()
    {
        var maxio = new FakeMaxio { RejectCredentials = true };
        var client = CreateClient(maxio, ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains("maxio.example.test"), body);
    }

    /// <summary>A minimal stateful stand-in for the Maxio routes the integration uses.</summary>
    private sealed class FakeMaxio : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _customers = new();
        private readonly ConcurrentDictionary<string, string> _subscriptions = new();
        private readonly ConcurrentQueue<(HttpMethod Method, string Path)> _requests = new();
        private int _nextId = 500;

        public bool RejectCredentials { get; init; }

        public int Count(HttpMethod method, string path) => _requests.Count(r => r.Method == method && r.Path.EndsWith(path));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            _requests.Enqueue((request.Method, path));
            if (RejectCredentials)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            var reference = Uri.UnescapeDataString(request.RequestUri.Query).Split("reference=").ElementAtOrDefault(1);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (request.Method == HttpMethod.Get && path.EndsWith("/product_families/handle:test-family/products.json"))
            {
                return Ok("""
                    [{"product":{"id":1,"handle":"pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
                     {"product":{"id":2,"handle":"basic","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]
                    """);
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
            {
                return reference is not null && _customers.TryGetValue(reference, out var id)
                    ? Ok($$$"""{"customer":{"id":{{{id}}},"reference":"{{{reference}}}"}}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                var customer = JsonDocument.Parse(body!).RootElement.GetProperty("customer");
                var customerReference = customer.GetProperty("reference").GetString()!;
                var id = _customers.GetOrAdd(customerReference, _ => Interlocked.Increment(ref _nextId));
                return Ok($$$"""{"customer":{"id":{{{id}}},"reference":"{{{customerReference}}}"}}""", HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
            {
                return reference is not null && _subscriptions.TryGetValue(reference, out var json)
                    ? Ok(json)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                await Task.Delay(50, cancellationToken);   // widen the double-submit window
                var subscription = JsonDocument.Parse(body!).RootElement.GetProperty("subscription");
                var subscriptionReference = subscription.GetProperty("reference").GetString()!;
                var handle = subscription.GetProperty("product_handle").GetString()!;
                var price = handle == "pro" ? 29900 : 2900;
                var json = $$$"""
                    {"subscription":{"id":{{{Interlocked.Increment(ref _nextId)}}},"state":"active","reference":"{{{subscriptionReference}}}",
                     "product_price_in_cents":{{{price}}},"current_period_ends_at":"2026-11-15T12:00:00Z","next_assessment_at":"2026-11-15T12:00:00Z",
                     "activated_at":"2026-10-15T12:00:00Z",
                     "product":{"id":1,"handle":"{{{handle}}}","name":"{{{handle}}}","price_in_cents":{{{price}}},"interval":1,"interval_unit":"month"}
                    }}
                    """;
                _subscriptions[subscriptionReference] = json;
                return Ok(json, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
            {
                return Ok("[" + string.Join(",", _subscriptions.Values) + "]");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
