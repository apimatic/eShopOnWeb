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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingTests
{
    [TestMethod]
    public async Task RoutesRequireJwtAndRejectUnofferedPlansBeforeWriting()
    {
        using var host = new BillingHost();
        using var anonymous = host.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" })).StatusCode);
        using var client = await host.ShopperAsync();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "other-family-plan" })).StatusCode);
        Assert.AreEqual(0, host.Provider.CustomerWrites);
        Assert.AreEqual(0, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task ConcurrentEnrollmentHasOneCustomerAndSubscriptionAndAccountIsIsolated()
    {
        using var host = new BillingHost();
        using var client = await host.ShopperAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" })));
        Assert.IsTrue(results.All(x => x.IsSuccessStatusCode));
        var repeat = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan", userId = "someone-else", customerId = 123 });
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
        var mine = await client.GetFromJsonAsync<AccountSubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Items.Count);
        Assert.AreEqual(29900L, mine.Subscriptions.Items[0].PriceInCents);
        Assert.AreEqual("active", mine.Subscriptions.Items[0].State);
        Assert.IsNotNull(mine.Subscriptions.Items[0].NextBillingDate);
        using var other = await host.ShopperAsync("admin@microsoft.com");
        Assert.AreEqual(0, (await other.GetFromJsonAsync<AccountSubscriptions>("/api/my-subscriptions"))!.Subscriptions.Items.Count);
    }

    [TestMethod]
    public async Task LostCustomerResponseReconcilesWithoutAnotherPost()
    {
        using var host = new BillingHost();
        host.Provider.LoseCustomerResponse = true;
        using var client = await host.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.IsTrue(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("confirmed", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        Assert.AreEqual(1, host.Provider.CustomerWrites);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task LostSubscriptionResponseReconcilesWithoutAnotherPost()
    {
        using var host = new BillingHost();
        host.Provider.LoseSubscriptionResponse = true;
        using var client = await host.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.IsTrue(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("confirmed", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnknownWriteRemainsPendingAndWorkerSettlesItWithoutResending()
    {
        using var host = new BillingHost();
        host.Provider.LoseSubscriptionResponse = true;
        host.Provider.HideSubscriptions = true;
        using var client = await host.ShopperAsync();
        var first = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" })).StatusCode);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var unknown = await db.BillingClaims.SingleAsync(x => x.Kind == "Subscription");
            Assert.AreEqual("Unknown", unknown.State);
            unknown.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        host.Provider.HideSubscriptions = false;
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISubscriptionBilling>().ReconcileAsync(default);
        using (var scope = host.Services.CreateScope())
            Assert.AreEqual("Complete", (await scope.ServiceProvider.GetRequiredService<CatalogContext>().BillingClaims.SingleAsync(x => x.Kind == "Subscription")).State);
        Assert.AreEqual(1, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task MalformedCustomerLookupDoesNotTurnIntoCustomerCreation()
    {
        using var host = new BillingHost();
        host.Provider.MalformedCustomerLookup = true;
        using var client = await host.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("MaxioAdvancedBilling"));
        Assert.AreEqual(0, host.Provider.CustomerWrites);
    }

    [TestMethod]
    public async Task MalformedSubscriptionSuccessReconcilesAndValidationRejectionReleasesClaim()
    {
        using var host = new BillingHost();
        using var client = await host.ShopperAsync();
        host.Provider.RejectSubscription = true;
        var rejected = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        host.Provider.RejectSubscription = false;
        host.Provider.MalformedSubscriptionSuccess = true;
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" });
        Assert.AreEqual("confirmed", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        Assert.AreEqual(2, host.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task ProviderAuthenticationAndMalformedErrorsAreSanitized()
    {
        using var host = new BillingHost();
        using var client = await host.ShopperAsync();
        host.Provider.CatalogStatus = HttpStatusCode.Unauthorized;
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        host.Provider.CatalogStatus = HttpStatusCode.TooManyRequests;
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        host.Provider.CatalogStatus = HttpStatusCode.OK;
        host.Provider.MalformedCatalog = true;
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.GetAsync("/api/subscription-plans")).StatusCode);
    }

    [TestMethod]
    public async Task CappedCatalogIsMarkedPartialAndCannotAuthorizeEnrollment()
    {
        using var host = new BillingHost();
        host.Provider.EndlessCatalog = true;
        using var client = await host.ShopperAsync();
        var plans = await client.GetFromJsonAsync<BillingPage<SubscriptionPlan>>("/api/subscription-plans");
        Assert.IsTrue(plans!.IsPartial);
        Assert.AreEqual(101, plans.NextPage);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" })).StatusCode);
        Assert.AreEqual(0, host.Provider.CustomerWrites);
    }

    [TestMethod]
    public void MissingCredentialsFailBeforeAnyNetworkCall()
    {
        Assert.ThrowsException<InvalidOperationException>(() => new ServiceCollection().AddSubscriptionBilling(new ConfigurationBuilder().Build()));
    }

    [TestMethod]
    public async Task RegisteredClientUsesConfiguredBaseUrlVerbatim()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key", ["Maxio:Subdomain"] = "unused-site",
            ["Maxio:ProductFamilyHandle"] = "test-family", ["Maxio:BaseUrl"] = "https://billing.test/prefix"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSubscriptionBilling(config);
        using var fake = new FakeProvider();
        services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => fake);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var products = await scope.ServiceProvider.GetRequiredService<MaxioGateway>().ProductsAsync("test-family", 1, default);
        Assert.AreEqual(1, products.Count); // FakeProvider asserts the configured path prefix on the actual SDK request.
    }

    [TestMethod]
    public async Task AccountPaginationIsExplicitAndForeignCustomerResultsAreRejected()
    {
        using var host = new BillingHost();
        using var client = await host.ShopperAsync();
        Assert.IsTrue((await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "test-plan" })).IsSuccessStatusCode);
        host.Provider.EndlessSubscriptions = true;
        var mine = await client.GetFromJsonAsync<AccountSubscriptions>("/api/my-subscriptions");
        Assert.IsTrue(mine!.Subscriptions.IsPartial);
        Assert.AreEqual(101, mine.Subscriptions.NextPage);
        host.Provider.EndlessSubscriptions = false;
        host.Provider.ForeignCustomer = true;
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
    }

    private sealed class BillingHost : WebApplicationFactory<Program>
    {
        public FakeProvider Provider { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Maxio:ApiKey", "test-key");
            builder.UseSetting("Maxio:Subdomain", "test-site");
            builder.UseSetting("Maxio:ProductFamilyHandle", "test-family");
            builder.ConfigureServices(services =>
            {
                var database = Guid.NewGuid().ToString();
                services.RemoveAll<DbContextOptions<CatalogContext>>();
                services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
                services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(database));
                services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(database + "-identity"));
                services.RemoveAll<MaxioOptions>();
                services.AddSingleton(new MaxioOptions { ApiKey = "test-key", Subdomain = "test-site", ProductFamilyHandle = "test-family", BaseUrl = "https://billing.test/prefix" });
                services.RemoveAll<MaxioAdvancedBillingClient>();
                var options = new MaxioAdvancedBillingClientOptions
                {
                    Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(2) },
                    Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
                };
                options.Server.Production.Us.BaseUrl = "https://billing.test/prefix";
                services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(Provider), options));
            });
        }

        public async Task<HttpClient> ShopperAsync(string username = "demouser@microsoft.com")
        {
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/authenticate", new { username, password = "Pass@word1" });
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString());
            return client;
        }
    }

    private sealed class FakeProvider : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> _customers = new();
        private readonly ConcurrentDictionary<string, string> _subscriptions = new();
        public int CustomerWrites;
        public int SubscriptionWrites;
        public bool LoseCustomerResponse, LoseSubscriptionResponse, HideSubscriptions, MalformedCustomerLookup,
            RejectSubscription, MalformedSubscriptionSuccess, MalformedCatalog, EndlessCatalog, EndlessSubscriptions, ForeignCustomer;
        public HttpStatusCode CatalogStatus = HttpStatusCode.OK;
        private static object Product => new { id = 42, handle = "test-plan", name = "Test Plan", price_in_cents = 29900, interval = 1, interval_unit = "month", require_credit_card = false, product_family = new { handle = "test-family" } };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.IsTrue(request.RequestUri!.AbsolutePath.StartsWith("/prefix/"), "BaseUrl path must be preserved.");
            var path = request.RequestUri.AbsolutePath[8..];
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            var reference = query.TryGetValue("reference", out var value) ? value.ToString() : "";
            if (path.Contains("product_families/"))
            {
                if (CatalogStatus != HttpStatusCode.OK) return Json(request, "sensitive provider detail", CatalogStatus);
                if (MalformedCatalog) return Json(request, "[{}]");
                return Json(request, JsonSerializer.Serialize(Enumerable.Range(0, EndlessCatalog ? 200 : 1).Select(_ => new { product = Product })));
            }
            if (path == "customers/lookup.json")
            {
                if (MalformedCustomerLookup) return Json(request, "{}");
                return _customers.TryGetValue(reference, out var customer) ? Json(request, customer) : Json(request, "{}", HttpStatusCode.NotFound);
            }
            if (path == "customers.json")
            {
                Interlocked.Increment(ref CustomerWrites);
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                reference = body.RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
                var customer = JsonSerializer.Serialize(new { customer = new { id = 11, reference } });
                _customers[reference] = customer;
                await Task.Delay(20, ct); // Make the first claim visible while other callers compete.
                if (LoseCustomerResponse) throw new HttpRequestException("Connection reset after accepting customer");
                return Json(request, customer, HttpStatusCode.Created);
            }
            if (path == "subscriptions/lookup.json")
                return !HideSubscriptions && _subscriptions.TryGetValue(reference, out var subscription)
                    ? Json(request, subscription) : Json(request, "", HttpStatusCode.NotFound);
            if (path == "subscriptions.json" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref SubscriptionWrites);
                if (RejectSubscription) return Json(request, "{\"errors\":[\"sensitive validation message\"]}", HttpStatusCode.UnprocessableEntity);
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = body.RootElement.GetProperty("subscription");
                Assert.AreEqual("test-plan", input.GetProperty("product_handle").GetString());
                Assert.AreEqual(11, input.GetProperty("customer_id").GetInt32());
                Assert.AreEqual("remittance", input.GetProperty("payment_collection_method").GetString());
                reference = input.GetProperty("reference").GetString()!;
                var subscription = JsonSerializer.Serialize(new { subscription = new { id = 22, reference, customer = new { id = 11 }, product = Product, product_price_in_cents = 29900, currency = "USD", state = "active", next_assessment_at = "2026-11-15T00:00:00Z" } });
                _subscriptions[reference] = subscription;
                if (LoseSubscriptionResponse) throw new HttpRequestException("Connection reset after accepting subscription");
                return Json(request, MalformedSubscriptionSuccess ? "{\"subscription\":\"invalid\"}" : subscription, HttpStatusCode.Created);
            }
            if (path == "subscriptions.json")
            {
                Assert.AreEqual("11", query["customer_id"].ToString());
                if (ForeignCustomer) return Json(request, "[{\"subscription\":{\"customer\":{\"id\":12}}}]");
                var items = EndlessSubscriptions ? Enumerable.Repeat(_subscriptions.Values.First(), 200) : _subscriptions.Values;
                return Json(request, "[" + string.Join(",", items) + "]");
            }
            throw new InvalidOperationException("Unexpected SDK operation in test: " + path);
        }

        private static HttpResponseMessage Json(HttpRequestMessage request, string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { RequestMessage = request, Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
