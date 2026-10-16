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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionTests
{
    [TestMethod]
    [DataRow("Maxio:ApiKey")]
    [DataRow("Maxio:Subdomain")]
    [DataRow("Maxio:ProductFamilyHandle")]
    public void BlankRequiredSettingPreventsStartup(string key)
    {
        using var factory = new BillingFactory { BlankSetting = key };
        var failure = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(failure.Message, key);
    }

    [TestMethod]
    public void SqlMigrationCreatesDurablePrimaryKeyClaimsWithoutConnecting()
    {
        using var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=unused.invalid;Database=MigrationScriptOnly;Integrated Security=true").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20211231093753_FixShipToAddress", "20261016000000_AddBillingClaims");
        StringAssert.Contains(sql, "CREATE TABLE [BillingClaims]");
        StringAssert.Contains(sql, "PRIMARY KEY ([Id])");
    }

    [TestMethod]
    public async Task CatalogCapFailsClosedWithoutCreatingAnything()
    {
        using var factory = new BillingFactory();
        factory.Provider.RepeatFullCatalogPage = true;
        using var client = await factory.LoginAsync();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(50, factory.Provider.CatalogReads);
        Assert.AreEqual(0, factory.Provider.CustomerWrites);
    }

    [TestMethod]
    public async Task MalformedErrorBodyRetainsPendingClaimRatherThanResending()
    {
        using var factory = new BillingFactory();
        factory.Provider.MalformedSubscriptionError = true;
        using var client = await factory.LoginAsync();
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
        Assert.IsTrue((await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.HasPendingOperations);
    }

    [TestMethod]
    public async Task RequiresBearerTokenOnEveryRoute()
    {
        using var factory = new BillingFactory();
        using var client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(0, factory.Provider.CustomerWrites);
    }

    [TestMethod]
    public async Task ListsOnlyOfferedPlansAndRejectsOtherHandlesBeforeAnyWrites()
    {
        using var factory = new BillingFactory();
        using var client = await factory.LoginAsync();
        var plans = await client.GetFromJsonAsync<SubscriptionPlan[]>("/api/subscription-plans");
        Assert.AreEqual(1, plans!.Length);
        Assert.AreEqual("pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("USD", plans[0].Currency);
        foreach (var handle in new[] { "foreign", "archived", "card-required", "missing", "" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = handle })).StatusCode);
        Assert.AreEqual(0, factory.Provider.CustomerWrites);
        Assert.AreEqual(0, factory.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task SubscribeRepeatAndAccountReadUseProviderState()
    {
        using var factory = new BillingFactory();
        using var client = await factory.LoginAsync();
        var first = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro", userId = "ignored", customerId = 999 });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());
        var created = await first.Content.ReadFromJsonAsync<SubscribeResult>();
        Assert.AreEqual("active", created!.Subscription!.State);
        Assert.IsNotNull(created.Subscription.NextBillingDate);
        Assert.AreEqual(29900L, created.Subscription.PriceInCents);
        var second = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var existing = await second.Content.ReadFromJsonAsync<SubscribeResult>();
        Assert.AreEqual(created.Subscription.Id, existing!.Subscription!.Id);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.IsFalse(mine.HasPendingOperations);
        Assert.AreEqual(1, factory.Provider.CustomerWrites);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
        using var otherUser = await factory.LoginAsync("admin@microsoft.com");
        var other = await otherUser.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(0, other!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickSendsOneCustomerAndOneSubscription()
    {
        using var factory = new BillingFactory();
        factory.Provider.WriteDelay = TimeSpan.FromMilliseconds(120);
        using var client = await factory.LoginAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })));
        foreach (var response in responses)
            Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, factory.Provider.CustomerWrites);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
        Assert.AreEqual(1, (await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task LostCustomerResponseIsReconciledWithoutResending()
    {
        using var factory = new BillingFactory();
        factory.Provider.LoseCustomerResponse = true;
        using var client = await factory.LoginAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, factory.Provider.CustomerWrites);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task LostSubscriptionResponseIsReconciledWithoutResending()
    {
        using var factory = new BillingFactory();
        factory.Provider.LoseSubscriptionResponse = true;
        using var client = await factory.LoginAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("active", (await response.Content.ReadFromJsonAsync<SubscribeResult>())!.Subscription!.State);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnknownOutcomeStaysPendingAndBackgroundReconciliationSettlesIt()
    {
        using var factory = new BillingFactory();
        factory.Provider.LoseSubscriptionResponse = true;
        factory.Provider.HideSubscriptions = true;
        using var client = await factory.LoginAsync();
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.IsTrue((await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.HasPendingOperations);
        factory.Provider.HideSubscriptions = false;
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SubscriptionService>().ReconcilePendingAsync(CancellationToken.None);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.IsFalse(mine!.HasPendingOperations);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(1, factory.Provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task ProviderRejectionReleasesClaimAndDoesNotEchoPrivateErrors()
    {
        using var factory = new BillingFactory();
        factory.Provider.RejectSubscription = true;
        using var client = await factory.LoginAsync();
        var rejected = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.IsFalse((await rejected.Content.ReadAsStringAsync()).Contains("private-provider-detail"));
        factory.Provider.RejectSubscription = false;
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(2, factory.Provider.SubscriptionWrites);
        Assert.AreEqual(1, factory.Provider.CustomerWrites);
    }

    [TestMethod]
    public async Task MalformedSuccessIsReconciledAndCatalogFailureIsSanitized()
    {
        using var factory = new BillingFactory();
        factory.Provider.MalformedSubscriptionResponse = true;
        using var client = await factory.LoginAsync();
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        factory.Provider.BreakCatalog = true;
        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("private-provider-detail"));
    }

    [TestMethod]
    public async Task DevelopmentRefusesLiveSiteBeforeWriting()
    {
        using var factory = new BillingFactory();
        factory.Provider.IsTestSite = false;
        using var client = await factory.LoginAsync();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "pro" })).StatusCode);
        Assert.AreEqual(0, factory.Provider.CustomerWrites);
    }
}

internal sealed class BillingFactory : WebApplicationFactory<Program>
{
    public BillingProvider Provider { get; } = new();
    public string? BlankSetting { get; set; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-only-key", ["Maxio:Subdomain"] = "test-only-site",
                ["Maxio:ProductFamilyHandle"] = "family", ["Maxio:BaseUrl"] = "https://billing.invalid",
                ["UseOnlyInMemoryDatabase"] = "true"
            };
            if (BlankSetting != null) values[BlankSetting] = " ";
            config.AddInMemoryCollection(values);
        });
        var database = Guid.NewGuid().ToString();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CatalogContext>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddScoped(_ => new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("billing-catalog-" + database).Options));
            services.AddScoped(_ => new AppIdentityDbContext(new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("billing-identity-" + database).Options));
            services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => Provider);
        });
    }

    public async Task<HttpClient> LoginAsync(string username = "demouser@microsoft.com")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/authenticate", new { username, password = AuthorizationConstants.DEFAULT_PASSWORD });
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());
        return client;
    }
}

internal sealed class BillingProvider : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, object> _customers = new();
    private readonly ConcurrentDictionary<string, JsonElement> _subscriptions = new();
    public int CustomerWrites;
    public int SubscriptionWrites;
    public int CatalogReads;
    public TimeSpan WriteDelay;
    public bool LoseCustomerResponse, LoseSubscriptionResponse, HideSubscriptions, RejectSubscription, MalformedSubscriptionResponse, BreakCatalog;
    public bool IsTestSite = true;
    public bool RepeatFullCatalogPage, MalformedSubscriptionError;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Assert.AreEqual("billing.invalid", request.RequestUri.Host, "The SDK must use the configured BaseUrl override.");
        if (request.Method == HttpMethod.Get)
        {
            if (path == "/site.json") return Json(new { site = new { test = IsTestSite, currency = "USD", relationship_invoicing_enabled = true } });
            if (path == "/products.json")
            {
                Interlocked.Increment(ref CatalogReads);
                if (BreakCatalog) return Raw("private-provider-detail", HttpStatusCode.ServiceUnavailable);
                object Plan(string handle, string family = "family", bool card = false, string? archived = null) => new
                {
                    product = new { id = 987, handle, name = handle, price_in_cents = 29900, interval = 1,
                        interval_unit = "month", require_credit_card = card, archived_at = archived, product_family = new { handle = family } }
                };
                if (RepeatFullCatalogPage) return Json(Enumerable.Range(0, 200).Select(index => Plan("pro-" + index)).ToArray());
                return Json(new[] { Plan("pro"), Plan("foreign", "other"), Plan("archived", archived: "2025-01-01T00:00:00Z"), Plan("card-required", card: true) });
            }
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            if (path == "/customers/lookup.json")
                return _customers.TryGetValue(query["reference"].ToString(), out var customer) ? Json(new { customer }) : Raw("{}", HttpStatusCode.NotFound);
            if (path == "/subscriptions/lookup.json")
                return !HideSubscriptions && _subscriptions.TryGetValue(query["reference"].ToString(), out var subscription) ? Json(new { subscription }) : Raw("{}", HttpStatusCode.NotFound);
            if (path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
            {
                int customerId = int.Parse(path.Split('/')[2]);
                return Json(HideSubscriptions ? Array.Empty<object>() : _subscriptions.Values
                    .Where(x => x.GetProperty("customer").GetProperty("id").GetInt32() == customerId).Select(x => (object)new { subscription = x }).ToArray());
            }
        }
        if (request.Method == HttpMethod.Post)
        {
            // Buffer before SDK disposes content. Assert real SDK request envelopes.
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            if (WriteDelay > TimeSpan.Zero) await Task.Delay(WriteDelay, ct);
            if (path == "/customers.json")
            {
                var count = Interlocked.Increment(ref CustomerWrites);
                var input = body.GetProperty("customer");
                Assert.IsTrue(input.TryGetProperty("email", out _));
                var reference = input.GetProperty("reference").GetString()!;
                var customer = new { id = count, reference };
                _customers[reference] = customer;
                if (LoseCustomerResponse) throw new HttpRequestException("Simulated reset after provider committed customer");
                return Json(new { customer });
            }
            if (path == "/subscriptions.json")
            {
                var count = Interlocked.Increment(ref SubscriptionWrites);
                if (MalformedSubscriptionError) return Raw("{\"unexpected\":true}", HttpStatusCode.UnprocessableEntity);
                if (RejectSubscription) return Raw("{\"errors\":[\"private-provider-detail\"]}", HttpStatusCode.UnprocessableEntity);
                var input = body.GetProperty("subscription");
                Assert.AreEqual("remittance", input.GetProperty("payment_collection_method").GetString());
                var reference = input.GetProperty("reference").GetString()!;
                var subscription = JsonSerializer.SerializeToElement(new
                {
                    id = count, reference, state = "active", product_price_in_cents = 29900, currency = "USD",
                    next_assessment_at = "2026-11-16T00:00:00Z", customer = new { id = input.GetProperty("customer_id").GetInt32() },
                    product = new { handle = input.GetProperty("product_handle").GetString(), name = "Pro", product_family = new { handle = "family" } }
                });
                _subscriptions[reference] = subscription;
                if (LoseSubscriptionResponse) throw new HttpRequestException("Simulated reset after provider committed subscription");
                if (MalformedSubscriptionResponse) return Raw("{\"subscription\":\"broken\"}");
                return Json(new { subscription });
            }
        }
        throw new InvalidOperationException($"Unexpected SDK request: {request.Method} {path}");
    }

    private static HttpResponseMessage Json(object value) => Raw(JsonSerializer.Serialize(value));
    private static HttpResponseMessage Raw(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}
