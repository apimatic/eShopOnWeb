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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionTests
{
    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireBearerToken()
    {
        using var app = new BillingApplication();
        using var client = app.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        Assert.AreEqual(0, app.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task PlansUseConfiguredFamilyAndBaseUrlAndExcludeArchivedProducts()
    {
        using var app = new BillingApplication();
        using var client = await app.ShopperAsync();
        var plans = await client.GetFromJsonAsync<SubscriptionPlan[]>("/api/subscription-plans");
        Assert.AreEqual(1, plans!.Length);
        Assert.AreEqual("fixture-pro", plans[0].Handle);
        Assert.AreEqual(12345L, plans[0].PriceInCents);
        var uri = app.Maxio.Requests.Single();
        Assert.IsTrue(uri.StartsWith("https://billing.invalid/provider-prefix/product_families/handle:fixture-family/products.json"));
        Assert.IsTrue(uri.Contains("include_archived=false"));
        Assert.IsTrue(uri.Contains("per_page=200"));
        Assert.IsTrue(app.Maxio.SawBasicAuth);
    }

    [TestMethod]
    public async Task EnrollmentConfirmsProviderValuesAndIgnoresCallerIdentityFields()
    {
        using var app = new BillingApplication();
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", new
        {
            productHandle = "fixture-pro", firstName = "Test", lastName = "Shopper",
            userId = "victim", customerId = 999, email = "victim@example.invalid", priceInCents = 1
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EnrollmentResult>();
        Assert.AreEqual("confirmed", result!.Status);
        Assert.AreEqual(12345L, result.Subscription!.PriceInCents);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(DateTimeOffset.Parse("2030-02-01T00:00:00Z"), result.Subscription.NextBillingDate);
        Assert.AreEqual("demouser@microsoft.com", app.Maxio.CustomerEmail);
        Assert.AreEqual("remittance", app.Maxio.CollectionMethod);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(result.Subscription.Id, mine!.Subscriptions.Single().Id);
        Assert.AreEqual(0, mine.Pending.Count);
        using var other = await app.ShopperAsync("admin@microsoft.com");
        Assert.AreEqual(0, (await other.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ParallelAndRepeatedEnrollmentCreatesExactlyOneCustomerAndSubscription()
    {
        using var app = new BillingApplication();
        app.Maxio.WriteDelay = true;
        using var client = await app.ShopperAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.PostAsJsonAsync("/api/subscriptions", Request())));
        foreach (var response in responses) Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted);
        var repeat = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        Assert.AreEqual(1, app.Maxio.CustomerWrites);
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(2, await scope.ServiceProvider.GetRequiredService<CatalogContext>().BillingClaims.CountAsync());
    }

    [TestMethod]
    public async Task ClaimStoreCatchesActualInMemoryPrimaryKeyCollisionAcrossContexts()
    {
        using var app = new BillingApplication();
        using var client = app.CreateClient();
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var claim = new BillingClaim { Id = "same-primary-key", Scope = "scope", UserId = "user", Kind = "Customer", Reference = "reference" };
        Assert.IsTrue((await first.ServiceProvider.GetRequiredService<IBillingClaimStore>().AcquireAsync(claim, default)).Acquired);
        var result = await second.ServiceProvider.GetRequiredService<IBillingClaimStore>().AcquireAsync(new BillingClaim { Id = claim.Id }, default);
        Assert.IsFalse(result.Acquired);
        Assert.AreEqual("reference", result.Claim.Reference);
    }

    [TestMethod]
    [DataRow("foreign-plan")]
    [DataRow("archived-plan")]
    public async Task UnofferedHandlesAreRejectedBeforeAnyBillingWrites(string handle)
    {
        using var app = new BillingApplication();
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", Request(handle));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, app.Maxio.CustomerWrites);
        Assert.AreEqual(0, app.Maxio.SubscriptionWrites);
    }

    [TestMethod]
    public async Task MissingNamesAreValidatedBeforeBillingWrites()
    {
        using var app = new BillingApplication();
        using var client = await app.ShopperAsync();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "fixture-pro" })).StatusCode);
        Assert.AreEqual(0, app.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task LostCustomerResponseIsReconciledBeforeReturningAndNeverResent()
    {
        using var app = new BillingApplication();
        app.Maxio.LoseCustomerResponse = true;
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("confirmed", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        Assert.AreEqual(1, app.Maxio.CustomerWrites);
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LostOrMalformedSubscriptionResponseIsReconciledWithoutSecondWrite(bool malformed)
    {
        using var app = new BillingApplication();
        app.Maxio.LoseSubscriptionResponse = !malformed;
        app.Maxio.MalformedSubscriptionSuccess = malformed;
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("confirmed", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnknownOutcomeStaysPendingAndSweepSettlesItWithoutRetryingWrite()
    {
        using var app = new BillingApplication();
        app.Maxio.LoseSubscriptionResponse = true;
        app.Maxio.HideSubscriptions = true;
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual("pending", (await response.Content.ReadFromJsonAsync<EnrollmentResult>())!.Status);
        var duplicate = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
        Assert.AreEqual(1, (await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions"))!.Pending.Count);
        app.Maxio.HideSubscriptions = false;
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SubscriptionService>().ReconcileAsync(default);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(0, mine.Pending.Count);
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
    }

    [TestMethod]
    public async Task DelayedCustomerRecoveryPreservesAndCompletesOriginalEnrollmentIntent()
    {
        using var app = new BillingApplication();
        app.Maxio.LoseCustomerResponse = true;
        app.Maxio.HideCustomers = true;
        using var client = await app.ShopperAsync();
        Assert.AreEqual(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        Assert.AreEqual(1, app.Maxio.CustomerWrites);
        Assert.AreEqual(0, app.Maxio.SubscriptionWrites);
        app.Maxio.HideCustomers = false;
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SubscriptionService>().ReconcileAsync(default);
        var mine = await client.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(0, mine.Pending.Count);
        Assert.AreEqual(1, app.Maxio.CustomerWrites);
        Assert.AreEqual(1, app.Maxio.SubscriptionWrites);
    }

    [TestMethod]
    public async Task UnreadableCustomerLookupDoesNotMeanCustomerIsAbsent()
    {
        using var app = new BillingApplication();
        app.Maxio.MalformedCustomerLookup = true;
        using var client = await app.ShopperAsync();
        Assert.AreEqual(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        Assert.AreEqual(0, app.Maxio.CustomerWrites);
        using var scope = app.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<CatalogContext>().BillingClaims.CountAsync());
    }

    [TestMethod]
    [DataRow("ApiKey")]
    [DataRow("Subdomain")]
    [DataRow("ProductFamilyHandle")]
    [DataRow("BaseUrl")]
    public void InvalidBillingSettingsPreventStartup(string key)
    {
        using var app = new BillingApplication { InvalidSetting = key };
        Assert.ThrowsException<OptionsValidationException>(() => app.CreateClient());
    }

    [TestMethod]
    public async Task ExplicitRejectionReleasesClaimAndProviderDetailsStayPrivate()
    {
        using var app = new BillingApplication();
        app.Maxio.RejectSubscription = true;
        using var client = await app.ShopperAsync();
        var response = await client.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("PRIVATE_PROVIDER_DETAIL"));
        using (var scope = app.Services.CreateScope())
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<CatalogContext>().BillingClaims.CountAsync());
        app.Maxio.RejectSubscription = false;
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        Assert.AreEqual(2, app.Maxio.SubscriptionWrites);
        Assert.AreEqual(1, app.Maxio.CustomerWrites);
    }

    [TestMethod]
    public async Task CatalogCapIsExplicitAndNeverAuthorizesAWriteFromPartialPlans()
    {
        using var app = new BillingApplication();
        app.Maxio.InfiniteCatalog = true;
        using var client = await app.ShopperAsync();
        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "catalog_incomplete");
        Assert.AreEqual(25, app.Maxio.Requests.Count);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        Assert.AreEqual(0, app.Maxio.CustomerWrites);
    }

    [TestMethod]
    public async Task ProviderAuthenticationFailureIsSanitizedAsUpstreamFailure()
    {
        using var app = new BillingApplication();
        app.Maxio.Unauthorized = true;
        using var client = await app.ShopperAsync();
        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(content.Contains("PRIVATE_PROVIDER_DETAIL"));
        Assert.IsFalse(content.Contains("MaxioAdvancedBilling"));
    }

    private static object Request(string handle = "fixture-pro") => new { productHandle = handle, firstName = "Test", lastName = "Shopper" };
}

internal class BillingApplication : WebApplicationFactory<Program>
{
    public MaxioStub Maxio { get; } = new();
    public string? InvalidSetting { get; set; }
    private readonly string _database = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddScoped(_ => new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("Catalog-" + _database).Options);
            services.AddScoped(_ => new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("Identity-" + _database).Options);
            services.Configure<MaxioSettings>(settings =>
            {
                settings.ApiKey = "test-only-not-a-real-key";
                settings.Subdomain = "test-only";
                settings.Environment = "US";
                settings.ProductFamilyHandle = "fixture-family";
                settings.BaseUrl = "https://billing.invalid/provider-prefix";
                switch (InvalidSetting)
                {
                    case "ApiKey": settings.ApiKey = " "; break;
                    case "Subdomain": settings.Subdomain = ""; break;
                    case "ProductFamilyHandle": settings.ProductFamilyHandle = ""; break;
                    case "BaseUrl": settings.BaseUrl = "relative/path"; break;
                }
            });
            // Use the production SDK/options registration, replacing only the network transport.
            services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => Maxio);
        });
    }

    public async Task<HttpClient> ShopperAsync(string username = "demouser@microsoft.com")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/authenticate", new { username, password = "Pass@word1" });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonObject>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!["token"]!.GetValue<string>());
        return client;
    }
}

// Wire fixtures are based solely on the plugin's operation/model contracts, not a replacement HTTP client.
internal class MaxioStub : HttpMessageHandler
{
    public ConcurrentBag<string> Requests { get; } = new();
    private readonly ConcurrentDictionary<string, JsonObject> _customers = new();
    private readonly ConcurrentDictionary<string, JsonObject> _subscriptions = new();
    public int CustomerWrites, SubscriptionWrites;
    public bool LoseCustomerResponse, LoseSubscriptionResponse, MalformedSubscriptionSuccess, HideSubscriptions;
    public bool RejectSubscription, InfiniteCatalog, Unauthorized, WriteDelay, SawBasicAuth, HideCustomers, MalformedCustomerLookup;
    public string? CustomerEmail, CollectionMethod;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        Requests.Add(Uri.UnescapeDataString(uri.ToString()));
        SawBasicAuth = request.Headers.Authorization?.Scheme == "Basic";
        var path = uri.AbsolutePath;
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (Unauthorized) return Reply(request, 401, new { errors = new[] { "PRIVATE_PROVIDER_DETAIL" } });
        if (path.Contains("/product_families/"))
        {
            var products = InfiniteCatalog ? Enumerable.Range(0, 200).Select(_ => new { product = Product("fixture-pro") }).ToArray()
                : new[] { new { product = Product("fixture-pro") }, new { product = Product("archived-plan", true) } };
            return Reply(request, 200, products);
        }
        if (path.EndsWith("/customers/lookup.json"))
        {
            if (MalformedCustomerLookup) return Reply(request, 200, new { customer = "malformed" });
            return !HideCustomers && _customers.TryGetValue(query["reference"].ToString(), out var customer) ? Reply(request, 200, new { customer }) : Reply(request, 404, new { });
        }
        if (path.EndsWith("/subscriptions/lookup.json"))
            return !HideSubscriptions && _subscriptions.TryGetValue(query["reference"].ToString(), out var subscription)
                ? Reply(request, 200, new { subscription }) : Reply(request, 404, new { });
        if (request.Method == HttpMethod.Get && path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            int id = int.Parse(path.Split('/')[^2]);
            return Reply(request, 200, _subscriptions.Values.Where(s => !HideSubscriptions && s["customer"]!["id"]!.GetValue<int>() == id).Select(subscription => new { subscription }).ToArray());
        }
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        if (path.EndsWith("/customers.json"))
        {
            Interlocked.Increment(ref CustomerWrites);
            var customer = (JsonObject)body["customer"]!;
            CustomerEmail = customer["email"]!.GetValue<string>();
            customer["id"] = 100 + CustomerWrites;
            _customers[customer["reference"]!.GetValue<string>()] = customer;
            if (WriteDelay) await Task.Delay(60, ct);
            if (LoseCustomerResponse) throw new HttpRequestException("simulated reset after customer commit");
            return Reply(request, 201, new { customer });
        }
        if (path.EndsWith("/subscriptions.json"))
        {
            Interlocked.Increment(ref SubscriptionWrites);
            if (RejectSubscription) return Reply(request, 422, new { errors = new[] { "PRIVATE_PROVIDER_DETAIL" } });
            var input = body["subscription"]!;
            CollectionMethod = input["payment_collection_method"]?.GetValue<string>();
            var customer = _customers.Values.Single(c => c["id"]!.GetValue<int>() == input["customer_id"]!.GetValue<int>());
            var subscription = new JsonObject
            {
                ["id"] = 500 + SubscriptionWrites, ["reference"] = input["reference"]!.GetValue<string>(),
                ["customer"] = customer.DeepClone(), ["product"] = Product(input["product_handle"]!.GetValue<string>()),
                ["product_price_in_cents"] = 12345, ["currency"] = "USD", ["state"] = "active", ["next_assessment_at"] = "2030-02-01T00:00:00Z"
            };
            _subscriptions[subscription["reference"]!.GetValue<string>()] = subscription;
            if (WriteDelay) await Task.Delay(60, ct);
            if (LoseSubscriptionResponse) throw new HttpRequestException("simulated reset after subscription commit");
            if (MalformedSubscriptionSuccess) return Reply(request, 201, new { subscription = "malformed" });
            return Reply(request, 201, new { subscription });
        }
        throw new InvalidOperationException("Unexpected SDK request in fixture.");
    }

    private static JsonObject Product(string handle, bool archived = false) => new()
    {
        ["id"] = 800, ["handle"] = handle, ["name"] = "Fixture Pro", ["price_in_cents"] = 12345,
        ["interval"] = 1, ["interval_unit"] = "month", ["require_credit_card"] = false,
        ["archived_at"] = archived ? "2020-01-01T00:00:00Z" : null,
        ["product_family"] = new JsonObject { ["handle"] = "fixture-family" }
    };

    private static HttpResponseMessage Reply(HttpRequestMessage request, int status, object body) => new((HttpStatusCode)status)
    {
        RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };
}
