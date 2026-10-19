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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("POST", "api/subscriptions")]
    [DataRow("GET", "api/my-subscriptions")]
    public async Task RequiresBearerToken(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeFlow_ListsPlans_Subscribes_IsIdempotent_AndShowsInMySubscriptions()
    {
        var maxio = new StubMaxio();
        using var factory = new TestWebApplicationFactory(maxio);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        var plansJson = await plansResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, plansResponse.StatusCode, plansJson);
        var plans = plansJson.FromJson<ListSubscriptionPlansResponse>();
        Assert.AreEqual("pro", plans!.Plans.Single().Handle);
        Assert.AreEqual(299.00m, plans.Plans.Single().Price);
        Assert.IsFalse(plans.IsTruncated);

        var created = await client.PostAsync("api/subscriptions", Json(new { planHandle = "pro" }));
        var createdJson = await created.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, createdJson);
        var createdBody = createdJson.FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("Created", createdBody!.Outcome);
        Assert.AreEqual("active", createdBody.Subscription!.State);
        Assert.AreEqual(299.00m, createdBody.Subscription.Price);
        Assert.IsNotNull(createdBody.Subscription.NextBillingAt);

        var repeat = await client.PostAsync("api/subscriptions", Json(new { }));
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        Assert.AreEqual("AlreadySubscribed", (await repeat.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!.Outcome);
        Assert.AreEqual(1, maxio.SubscriptionCreates, "a repeat must not create a second subscription");

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineBody = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(createdBody.Subscription.Id, mineBody!.Subscriptions.Single().Id);

        // The customer is keyed by the caller from the token, never by anything in the request body.
        Assert.IsTrue(maxio.CustomerReferences.All(r => r == "eshop-demouser@microsoft.com"));
    }

    [TestMethod]
    public async Task UnknownPlan_Returns400_WithCallerSafeMessage()
    {
        using var factory = new TestWebApplicationFactory(new StubMaxio());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "not an available subscription plan");
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// <summary>Minimal in-process Maxio: one product family with one plan, no existing customer.</summary>
    private sealed class StubMaxio : HttpMessageHandler
    {
        private readonly ConcurrentBag<object> _subscriptions = new();
        private int _subscriptionCreates;
        public int SubscriptionCreates => _subscriptionCreates;
        public ConcurrentBag<string> CustomerReferences { get; } = new();
        private volatile bool _customerExists;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (path == "/product_families/handle:test-family/products.json")
            {
                return Ok(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"] == "1"
                    ? "[{\"product\":{\"id\":1,\"handle\":\"pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}]"
                    : "[]");
            }

            if (path == "/customers/lookup.json")
            {
                CustomerReferences.Add(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["reference"]!);
                return _customerExists ? Ok(Customer()) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path == "/customers.json" && request.Method == HttpMethod.Post)
            {
                CustomerReferences.Add(JsonDocument.Parse(body!).RootElement.GetProperty("customer").GetProperty("reference").GetString()!);
                _customerExists = true;
                return Ok(Customer());
            }

            if (path == "/customers/77/subscriptions.json")
            {
                return Ok(JsonSerializer.Serialize(_subscriptions.Select(s => new { subscription = s })));
            }

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref _subscriptionCreates);
                var reference = JsonDocument.Parse(body!).RootElement.GetProperty("subscription").GetProperty("reference").GetString();
                var subscription = new
                {
                    id = 5001,
                    state = "active",
                    reference,
                    currency = "USD",
                    product_price_in_cents = 29900,
                    current_period_ends_at = "2026-11-19T00:00:00Z",
                    created_at = "2026-10-19T00:00:00Z",
                    product = new { id = 1, handle = "pro", name = "Pro Plan" }
                };
                _subscriptions.Add(subscription);
                return Ok(JsonSerializer.Serialize(new { subscription }));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string Customer() =>
            "{\"customer\":{\"id\":77,\"reference\":\"eshop-demouser@microsoft.com\",\"email\":\"demouser@microsoft.com\"}}";

        private static HttpResponseMessage Ok(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
