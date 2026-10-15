using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    /// <summary>
    /// Hosts the real app on Kestrel (random loopback port) in addition to the TestServer the base class builds:
    /// TestServer 8.x cannot write JSON responses on the .NET 10 runtime this machine rolls forward to.
    /// </summary>
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private IHost? _kestrel;

        public FakeMaxio Maxio { get; } = new();
        public IServiceProvider AppServices => _kestrel!.Services;

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Maxio));

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var testHost = builder.Build();
            builder.ConfigureWebHost(web => web.UseKestrel(o => o.Listen(IPAddress.Loopback, 0)));
            _kestrel = builder.Build();
            _kestrel.Start();
            var address = _kestrel.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            ClientOptions.BaseAddress = new Uri(address);
            testHost.Start();
            return testHost;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _kestrel?.Dispose();
            base.Dispose(disposing);
        }
    }

    private static HttpClient NewClient(Factory factory)
    {
        _ = factory.Server; // forces the host to be built
        return new HttpClient { BaseAddress = factory.ClientOptions.BaseAddress };
    }

    // A fresh shopper per test: the in-memory store is shared across factories in one process.
    private static async Task<(Factory Factory, HttpClient Client)> StartAsync()
    {
        var factory = new Factory();
        _ = factory.Server;
        var userName = $"shopper-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.AppServices.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await users.CreateAsync(new ApplicationUser { UserName = userName, Email = userName }, "Pass@word1");
            Assert.IsTrue(result.Succeeded);
        }
        var client = NewClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        return (factory, client);
    }

    private static Task<HttpResponseMessage> Subscribe(HttpClient client, string plan) =>
        client.PostAsJsonAsync("/api/subscriptions", new { planHandle = plan });

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = new Factory();
        var client = NewClient(factory);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Subscribe(client, "eshop-pro")).StatusCode);
    }

    [TestMethod]
    public async Task ListsOfferedPlansWithoutArchivedOnes()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;
        var resp = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, await resp.Content.ReadAsStringAsync());
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var handles = json.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "eshop-pro", "basic-plan" }, handles);
        Assert.IsTrue(json.GetProperty("isComplete").GetBoolean());
        StringAssert.Contains(factory.Maxio.Requests.First().PathAndQuery, "handle:test-family");
    }

    [TestMethod]
    public async Task RejectsPlanTheApplicationDoesNotOffer()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Subscribe(client, "old-plan")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Subscribe(client, "")).StatusCode);
        Assert.AreEqual(0, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(0, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionOnceAndIsShownInMySubscriptions()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;

        var first = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var sub = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subscription");
        Assert.AreEqual("eshop-pro", sub.GetProperty("planHandle").GetString());
        Assert.AreEqual(29900, sub.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.AreEqual(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), sub.GetProperty("nextBillingDate").GetDateTimeOffset());

        // Second click: same subscription, nothing new created.
        var second = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));

        // The same shopper on another plan reuses the customer.
        Assert.AreEqual(HttpStatusCode.Created, (await Subscribe(client, "basic-plan")).StatusCode);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));

        var mine = await client.GetFromJsonAsync<JsonElement>("/api/my-subscriptions");
        Assert.AreEqual(2, mine.GetProperty("subscriptions").GetArrayLength());

        // The request carries our handle, the stored customer id and a deterministic reference.
        var body = factory.Maxio.Requests.First(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/subscriptions.json")).Body!;
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_id\":");
        StringAssert.Contains(body, "\"reference\":\"sub:SHOPPER-");
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneSubscription()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Subscribe(client, "eshop-pro")));

        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict));
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task ProviderRejectionIsMappedAndTheClaimIsReleased()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;
        factory.Maxio.OnCreateSubscription = _ => FakeMaxio.Json("""{"errors":["No payment method"]}""", HttpStatusCode.UnprocessableEntity);

        var rejected = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        StringAssert.Contains(await rejected.Content.ReadAsStringAsync(), "No payment method");

        factory.Maxio.OnCreateSubscription = null;
        Assert.AreEqual(HttpStatusCode.Created, (await Subscribe(client, "eshop-pro")).StatusCode);
    }

    [TestMethod]
    public async Task ConnectionFailureAfterTheWriteLandedIsSettledByReference()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;
        factory.Maxio.OnCreateSubscription = req =>
        {
            var reference = JsonDocument.Parse(req.Content!.ReadAsStringAsync().Result)
                .RootElement.GetProperty("subscription").GetProperty("reference").GetString()!;
            factory.Maxio.CommitSubscription(reference); // Maxio committed, then the connection died
            throw new HttpRequestException("connection reset");
        };

        var response = await Subscribe(client, "eshop-pro");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"), "the write must not be resent");
    }

    [TestMethod]
    public async Task ConnectionFailureWithNothingFoundReportsUnknownThenRecovers()
    {
        var (factory, client) = await StartAsync();
        using var _ = factory;
        factory.Maxio.OnCreateSubscription = _ => throw new HttpRequestException("connection reset");

        var response = await Subscribe(client, "eshop-pro");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"), "the write must not be resent");

        // The claim is Unknown: the next request settles it by reference (nothing there), clears it, and asks for a retry.
        factory.Maxio.OnCreateSubscription = null;
        Assert.AreEqual(HttpStatusCode.Conflict, (await Subscribe(client, "eshop-pro")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await Subscribe(client, "eshop-pro")).StatusCode);
        Assert.AreEqual(2, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }
}
