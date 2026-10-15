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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Drives the subscription endpoints through the real PublicApi host (JWT auth, endpoint routing, error middleware),
/// with Maxio replaced at the HttpClient seam. Success payloads are asserted in SubscriptionEndpointHandlerTest.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        using var app = new SubscriptionApp();
        var client = app.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("api/subscriptions", PlanJson("eshop-pro"))).StatusCode);
        Assert.AreEqual(0, app.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task UnknownPlanIsABadRequestAndNothingIsWritten()
    {
        using var app = new SubscriptionApp();
        var client = app.CreateUserClient();

        var response = await client.PostAsync("api/subscriptions", PlanJson("not-a-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(app.Maxio.Requests.Any(r => r.StartsWith("POST")));
    }

    [TestMethod]
    public async Task ProviderOutageIsA503WithoutProviderDetails()
    {
        using var app = new SubscriptionApp();
        app.Maxio.FailEverything = true;
        var client = app.CreateUserClient();

        var response = await client.GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsFalse(body.Contains("maxio.test"), body);
    }

    private static StringContent PlanJson(string planHandle) =>
        new(JsonSerializer.Serialize(new { planHandle }), Encoding.UTF8, "application/json");

    private sealed class SubscriptionApp : WebApplicationFactory<Program>
    {
        public StubMaxio Maxio { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("UseOnlyInMemoryDatabase", "true");
            builder.UseSetting("Maxio:ApiKey", "test-placeholder");
            builder.UseSetting("Maxio:ProductFamilyHandle", "test-family");
            builder.UseSetting("Maxio:BaseUrl", StubMaxio.BaseUrl);
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Maxio));
        }

        public HttpClient CreateUserClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
            return client;
        }
    }

    /// <summary>Scripted Maxio: a new customer, two plans, and subscription creates that succeed.</summary>
    private sealed class StubMaxio : HttpMessageHandler
    {
        public const string BaseUrl = "https://maxio.test";

        public ConcurrentQueue<string> Requests { get; } = new();
        public object[] SubscriptionsOfCustomer { get; set; } = Array.Empty<object>();
        public bool FailEverything { get; set; }
        private int _customerCreated;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var route = $"{request.Method.Method} {Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)}";
            Requests.Enqueue(route);
            if (FailEverything)
            {
                throw new HttpRequestException("connection refused");
            }

            switch (route)
            {
                case "GET /product_families/handle:test-family/products.json":
                    return Json(HttpStatusCode.OK, new[]
                    {
                        new { product = Product("basic-plan", 2900) },
                        new { product = Product("eshop-pro", 29900) }
                    });
                case "GET /customers/lookup.json":
                    return Volatile.Read(ref _customerCreated) == 1
                        ? Json(HttpStatusCode.OK, new { customer = new { id = 10 } })
                        : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
                case "POST /customers.json":
                    Interlocked.Exchange(ref _customerCreated, 1);
                    return Json(HttpStatusCode.Created, new { customer = new { id = 10 } });
                case "GET /customers/10/subscriptions.json":
                    return Json(HttpStatusCode.OK, SubscriptionsOfCustomer);
                case "POST /subscriptions.json":
                    await Task.Yield();
                    return Json(HttpStatusCode.Created, Subscription(500, "eshop-pro"));
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
            }
        }

        private static object Product(string handle, long priceInCents) =>
            new { id = 1, handle, name = handle, price_in_cents = priceInCents, interval = 1, interval_unit = "month" };

        public static object Subscription(int id, string handle) => new
        {
            subscription = new
            {
                id,
                state = "active",
                currency = "USD",
                product_price_in_cents = 29900,
                current_period_ends_at = "2026-11-15T12:00:00Z",
                created_at = "2026-10-15T12:00:00Z",
                product = new { id = 1, handle, name = handle }
            }
        };

        private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }
}
