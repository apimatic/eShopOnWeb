using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>Drives the three subscription endpoints against a stubbed Maxio (no network).</summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";
    // The in-memory databases are shared by name across factories in one process, so each test that subscribes
    // uses its own plan handle to stay independent of the others.
    private const string TestBaseUrl = "http://maxio.invalid/api-root";

    private sealed class FakeMaxio : HttpMessageHandler
    {
        public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
        private bool _customerExists;
        private bool _subscriptionLanded;

        /// <summary>When set, the subscription create reaches Maxio but the connection drops before the answer.</summary>
        public bool DropSubscriptionAnswer { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            Requests.Enqueue(request);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/products.json"))
            {
                return Json("""[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":2,"handle":"plan-dropped-connection","name":"Other","price_in_cents":100,"interval":1,"interval_unit":"month"}}]""");
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
            {
                return _customerExists ? Json("""{"customer":{"id":11}}""") : Json("{}", HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                await Task.Delay(300, ct);   // widen the race window
                _customerExists = true;
                return Json("""{"customer":{"id":11}}""", HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
            {
                return _subscriptionLanded ? Json(SubscriptionJson) : Json("{}", HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                await Task.Delay(300, ct);
                if (DropSubscriptionAnswer)
                {
                    _subscriptionLanded = true;
                    throw new HttpRequestException("connection reset");
                }
                return Json(SubscriptionJson, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/22.json"))
            {
                return Json(SubscriptionJson);
            }
            return Json("{}", HttpStatusCode.NotFound);
        }

        public int Count(HttpMethod method, string suffix) =>
            Requests.Count(r => r.Method == method && r.RequestUri!.AbsolutePath.EndsWith(suffix));

        private const string SubscriptionJson =
            """{"subscription":{"id":22,"state":"active","next_assessment_at":"2030-01-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (WebApplicationFactory<Program> Factory, FakeMaxio Maxio) CreateFactory()
    {
        var maxio = new FakeMaxio();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:ApiKey", "test-key"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:Subdomain", "test-site"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:ProductFamilyHandle", "test-family"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:BaseUrl", TestBaseUrl),
            }));
            builder.ConfigureServices(services =>
                services.AddHttpClient(MaxioHttpClientName).ConfigurePrimaryHttpMessageHandler(() => maxio));
        });
        return (factory, maxio);
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("api/authenticate", new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD,
        });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var (factory, _) = CreateFactory();
        var client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansFromTheConfiguredFamilyAndHonoursBaseUrlVerbatim()
    {
        var (factory, maxio) = CreateFactory();
        var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual("eshop-pro", body.GetProperty("plans")[0].GetProperty("handle").GetString());
        Assert.IsTrue(body.GetProperty("isComplete").GetBoolean());
        var uri = maxio.Requests.Single().RequestUri!;
        StringAssert.StartsWith(uri.AbsoluteUri, TestBaseUrl + "/product_families/handle");
        StringAssert.Contains(uri.AbsoluteUri, "test-family");
    }

    [TestMethod]
    public async Task RejectsAPlanTheFamilyDoesNotOffer()
    {
        var (factory, maxio) = CreateFactory();
        var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "not-offered" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.Count(HttpMethod.Post, ".json"));
    }

    [TestMethod]
    public async Task ConcurrentSubscribesCreateOneCustomerAndOneSubscription()
    {
        var (factory, maxio) = CreateFactory();
        var client = await AuthenticatedClientAsync(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" })));

        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict));

        // A later retry is idempotent and returns the existing subscription.
        var again = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        var mine = await (await client.GetAsync("api/my-subscriptions")).Content.ReadFromJsonAsync<JsonElement>();
        var subscription = mine.GetProperty("subscriptions").EnumerateArray().Single();
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual(22, subscription.GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(29900, subscription.GetProperty("priceInCents").GetInt64());
    }

    [TestMethod]
    public async Task ADroppedConnectionOnCreateIsSettledByReadingBackNotReportedAsFailure()
    {
        var (factory, maxio) = CreateFactory();
        maxio.DropSubscriptionAnswer = true;
        var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "plan-dropped-connection" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(22, body.GetProperty("subscription").GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(1, maxio.Count(HttpMethod.Post, "/subscriptions.json"));   // never resent
    }

    [TestMethod]
    public void StartupFailsWhenACredentialIsMissing()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:ApiKey", " "),
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:Subdomain", "test-site"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Maxio:ProductFamilyHandle", "test-family"),
            })));

        var ex = Assert.ThrowsException<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }
}
