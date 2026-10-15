using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static readonly object Request = new { productHandle = "pro", firstName = "Test", lastName = "Shopper" };

    [TestMethod]
    public async Task ConcurrentSubscribeCreatesOnceAndAccountShowsProviderState()
    {
        using var fixture = new Fixture();
        using var client = await fixture.ShopperAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync("/api/subscriptions", Request)));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted));
        using var retry = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode);
        Assert.AreEqual(1, fixture.Handler.CustomerWrites);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
        var json = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual("active", json.GetProperty("subscription").GetProperty("state").GetString());
        Assert.AreEqual(29900L, json.GetProperty("subscription").GetProperty("priceInCents").GetInt64());
        Assert.AreEqual("remittance", fixture.Handler.CollectionMethod);
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/my-subscriptions");
        Assert.AreEqual(1, mine.GetArrayLength());
        Assert.AreEqual("USD", mine[0].GetProperty("currency").GetString());
        Assert.IsTrue(mine[0].GetProperty("nextBillingDate").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        using var other = await fixture.ShopperAsync("admin@microsoft.com");
        Assert.AreEqual(0, (await other.GetFromJsonAsync<JsonElement>("/api/my-subscriptions")).GetArrayLength());
    }

    [TestMethod]
    public async Task LostCustomerResponseIsReconciledWithoutResend()
    {
        using var fixture = new Fixture();
        fixture.Handler.DropCustomerResponse = true;
        using var client = await fixture.ShopperAsync();
        using var result = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(1, fixture.Handler.CustomerWrites);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task LostSubscriptionResponseIsReconciledWithoutResend()
    {
        using var fixture = new Fixture();
        fixture.Handler.DropSubscriptionResponse = true;
        using var client = await fixture.ShopperAsync();
        using var result = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnknownOutcomeStaysPendingAndLaterRequestReconciles()
    {
        using var fixture = new Fixture();
        fixture.Handler.DropSubscriptionResponse = true;
        fixture.Handler.HideSubscriptions = true;
        using var client = await fixture.ShopperAsync();
        using var first = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        using var second = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.Accepted, second.StatusCode);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
        fixture.Handler.HideSubscriptions = false;
        using var third = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, third.StatusCode);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task AuthenticationValidationAndFamilyBoundaryPreventWrites()
    {
        using var fixture = new Fixture();
        using var anonymous = fixture.CreateClient();
        foreach (var path in new[] { "/api/subscription-plans", "/api/my-subscriptions" })
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/subscriptions", Request)).StatusCode);
        using var client = await fixture.ShopperAsync();
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/subscription-plans");
        Assert.AreEqual(1, plans.GetProperty("plans").GetArrayLength());
        foreach (var handle in new[] { "outside-family", "archived", "missing" })
        {
            using var result = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = handle, firstName = "A", lastName = "B" });
            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode);
        }
        using var invalid = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro", firstName = " ", lastName = "B" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(0, fixture.Handler.CustomerWrites);
        Assert.AreEqual(0, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task MalformedLookupDoesNotAuthorizeCustomerCreationOrLeakSdkDetails()
    {
        using var fixture = new Fixture();
        fixture.Handler.MalformedCustomerLookup = true;
        using var client = await fixture.ShopperAsync();
        using var result = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.BadGateway, result.StatusCode);
        Assert.AreEqual(0, fixture.Handler.CustomerWrites);
        var body = await result.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains("MaxioAdvancedBilling"));
        Assert.IsFalse(body.Contains("chargify"));
    }

    [TestMethod]
    public async Task MalformedSuccessAfterCreateReconciles()
    {
        using var fixture = new Fixture();
        fixture.Handler.MalformedSubscriptionResponse = true;
        using var client = await fixture.ShopperAsync();
        using var result = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task CatalogWalksPagesAndReportsTruncationBeforeAnyWrite()
    {
        using var fixture = new Fixture();
        fixture.Handler.FullCatalogFirstPage = true;
        using var client = await fixture.ShopperAsync();
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/subscription-plans");
        Assert.AreEqual(1, plans.GetProperty("plans").GetArrayLength());
        Assert.IsFalse(plans.GetProperty("isTruncated").GetBoolean());
        Assert.AreEqual(2, fixture.Handler.CatalogReads);
        fixture.Handler.InfiniteCatalog = true;
        plans = await client.GetFromJsonAsync<JsonElement>("/api/subscription-plans");
        Assert.IsTrue(plans.GetProperty("isTruncated").GetBoolean());
        using var result = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.AreEqual(0, fixture.Handler.CustomerWrites);
    }

    [TestMethod]
    public async Task DefinitiveRefusalReleasesClaimForCorrectedRequest()
    {
        using var fixture = new Fixture();
        fixture.Handler.RejectSubscription = true;
        using var client = await fixture.ShopperAsync();
        using var first = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, first.StatusCode);
        fixture.Handler.RejectSubscription = false;
        using var second = await client.PostAsJsonAsync("/api/subscriptions", Request);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(1, fixture.Handler.CustomerWrites);
        Assert.AreEqual(1, fixture.Handler.SubscriptionWrites);
    }

    [TestMethod]
    public async Task ConfiguredBaseUrlOverridesSubdomainVerbatim()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "unused-test-key", ["Maxio:Subdomain"] = "unused-subdomain",
            ["Maxio:ProductFamilyHandle"] = "test-family", ["Maxio:BaseUrl"] = "https://override.invalid"
        }).Build();
        MaxioSettings.Register(services, configuration);
        using var handler = new BillingHandler();
        services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var plans = await scope.ServiceProvider.GetRequiredService<MaxioGateway>().PlansAsync(default);
        Assert.AreEqual(1, plans.Plans.Count);
        Assert.AreEqual("https://override.invalid/products.json", handler.LastUri!.GetLeftPart(UriPartial.Path));
    }

    [TestMethod]
    public void MissingCredentialsFailBeforeHostStarts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Maxio:ApiKey"] = " ", ["Maxio:Subdomain"] = "test", ["Maxio:ProductFamilyHandle"] = "test" }).Build();
        var failure = Assert.ThrowsException<InvalidOperationException>(() => MaxioSettings.Register(new ServiceCollection(), configuration));
        Assert.AreEqual("Maxio:ApiKey is required.", failure.Message);
    }

    private sealed class Fixture : WebApplicationFactory<Program>
    {
        public BillingHandler Handler { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var root = new InMemoryDatabaseRoot();
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
                services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase("BillingTests", root));
                services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase("BillingIdentityTests", root));
                services.RemoveAll<MaxioAdvancedBillingClient>();
                var options = new MaxioAdvancedBillingClientOptions
                {
                    Retry = RetryOptions.Disabled(),
                    Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
                };
                options.Server.Production.Us.BaseUrl = "https://billing-test.invalid";
                services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(Handler), options));
            });
        }

        public async Task<HttpClient> ShopperAsync(string username = "demouser@microsoft.com")
        {
            var client = CreateClient();
            using var auth = await client.PostAsJsonAsync("/api/authenticate", new { username, password = "Pass@word1" });
            var json = await auth.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
            return client;
        }
    }

    private sealed class BillingHandler : HttpMessageHandler
    {
        public int CustomerWrites;
        public int SubscriptionWrites;
        public int CatalogReads;
        public bool FullCatalogFirstPage, InfiniteCatalog, RejectSubscription;
        public Uri? LastUri;
        public bool DropCustomerResponse, DropSubscriptionResponse, HideSubscriptions, MalformedCustomerLookup, MalformedSubscriptionResponse;
        public string? CollectionMethod;
        private readonly ConcurrentDictionary<string, object> _customers = new();
        private readonly ConcurrentDictionary<string, object> _subscriptions = new();
        private readonly ConcurrentDictionary<string, int> _customerIds = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            LastUri = request.RequestUri;
            var reference = Uri.UnescapeDataString(request.RequestUri.Query.Replace("?reference=", ""));
            if (request.Method == HttpMethod.Get)
            {
                if (path == "/products.json")
                {
                    Interlocked.Increment(ref CatalogReads);
                    if (InfiniteCatalog || (FullCatalogFirstPage && request.RequestUri.Query.Contains("page=1&")))
                        return Json(request, Enumerable.Range(0, 200).Select(i => new { product = Product("other-" + i, "another-family") }).ToArray());
                    return Json(request, new[]
                {
                    new { product = Product("pro", "test-family") },
                    new { product = Product("outside-family", "another-family") },
                    new { product = Product("archived", "test-family", true) }
                });
                }
                if (path == "/customers/lookup.json")
                {
                    if (MalformedCustomerLookup) return Raw(request, "{}");
                    return _customers.TryGetValue(reference, out var c) ? Json(request, new { customer = c }) : Json(request, new { }, HttpStatusCode.NotFound);
                }
                if (path == "/subscriptions/lookup.json") return !HideSubscriptions && _subscriptions.TryGetValue(reference, out var s)
                    ? Json(request, new { subscription = s }) : Json(request, new { }, HttpStatusCode.NotFound);
                if (path.Contains("/subscriptions.json"))
                {
                    var id = int.Parse(path.Split('/')[2]);
                    return Json(request, _subscriptions.Where(x => _customerIds[x.Key] == id).Select(x => new { subscription = x.Value }).ToArray());
                }
            }
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (path == "/customers.json")
            {
                var customer = body.RootElement.GetProperty("customer");
                reference = customer.GetProperty("reference").GetString()!;
                var c = new { id = Interlocked.Increment(ref CustomerWrites), reference };
                _customers[reference] = c;
                await Task.Delay(40, ct);
                if (DropCustomerResponse) throw new HttpRequestException("response lost after committing customer");
                return Json(request, new { customer = c });
            }
            if (path == "/subscriptions.json")
            {
                if (RejectSubscription) return Json(request, new { errors = new[] { "Validation rejected" } }, HttpStatusCode.UnprocessableEntity);
                var subscription = body.RootElement.GetProperty("subscription");
                reference = subscription.GetProperty("reference").GetString()!;
                var customerId = subscription.GetProperty("customer_id").GetInt32();
                CollectionMethod = subscription.GetProperty("payment_collection_method").GetString();
                var s = new { id = Interlocked.Increment(ref SubscriptionWrites), reference,
                    customer = new { id = customerId }, product = Product("pro", "test-family"),
                    state = "active", product_price_in_cents = 29900, currency = "USD", next_assessment_at = DateTimeOffset.UtcNow.AddMonths(1) };
                _subscriptions[reference] = s;
                _customerIds[reference] = customerId;
                if (DropSubscriptionResponse) throw new HttpRequestException("response lost after committing subscription");
                if (MalformedSubscriptionResponse) return Raw(request, "{}");
                return Json(request, new { subscription = s });
            }
            throw new InvalidOperationException("Unexpected SDK request: " + path);
        }

        private static object Product(string handle, string family, bool archived = false) => new
        {
            handle, name = "Pro", price_in_cents = 29900, interval = 1, interval_unit = "month",
            product_family = new { handle = family }, require_credit_card = false,
            archived_at = archived ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
        };
        private static HttpResponseMessage Json(HttpRequestMessage request, object body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Raw(HttpRequestMessage request, string body)
            => new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
