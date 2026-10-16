using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionBillingTests
{
    // Deliberately synthetic catalog; no sandbox catalog values or credentials are fixtures.
    private const string Handle = "fixture-monthly";
    private static readonly ApplicationUser Shopper = new() { Id = "fixture-user", Email = "shopper@example.invalid" };

    private sealed class Provider : HttpMessageHandler
    {
        public string? CustomerReference;
        public string? SubscriptionReference;
        public int CustomerPosts;
        public int SubscriptionPosts;
        public bool LoseCustomerResponse;
        public bool LoseSubscriptionResponse;
        public bool MalformedSubscriptionResponse;
        public bool RejectSubscription;
        public bool MalformedRejection;
        public bool FullCatalogPages;
        public int CatalogPages;
        public bool RelationshipInvoicing = true;
        public string? PostedCollectionMethod;
        public bool HideSubscription;
        public bool CorruptCustomerLookup;
        public bool HideCustomer;
        public TaskCompletionSource<bool>? CustomerEntered;
        public TaskCompletionSource<bool>? ReleaseCustomer;
        public TaskCompletionSource<bool>? SubscriptionEntered;
        public TaskCompletionSource<bool>? ReleaseSubscription;
        public string Product => JsonSerializer.Serialize(new { id = 41, handle = Handle, name = "Fixture", price_in_cents = 1250, interval = 1, interval_unit = "month" });
        private string Customer => JsonSerializer.Serialize(new { customer = new { id = 51, reference = CustomerReference } });
        private string Subscription => "{\"subscription\":{\"id\":61,\"reference\":\"" + SubscriptionReference + "\",\"customer\":{\"id\":51},\"product\":" + Product + ",\"product_price_in_cents\":1250,\"state\":\"active\",\"next_assessment_at\":\"2027-01-01T00:00:00Z\",\"currency\":\"USD\"}}";
        private static HttpResponseMessage Reply(HttpRequestMessage request, string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/site.json") return Reply(request, "{\"site\":{\"relationship_invoicing_enabled\":" + (RelationshipInvoicing ? "true" : "false") + "}}");
            if (path.Contains("product_families"))
            {
                Interlocked.Increment(ref CatalogPages);
                return Reply(request, "[" + string.Join(",", Enumerable.Repeat("{\"product\":" + Product + "}", FullCatalogPages ? 200 : 1)) + "]");
            }
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (path == "/customers.json")
                {
                    Interlocked.Increment(ref CustomerPosts);
                    CustomerReference = body.RootElement.GetProperty("customer").GetProperty("reference").GetString();
                    CustomerEntered?.TrySetResult(true);
                    if (ReleaseCustomer != null) await ReleaseCustomer.Task.WaitAsync(ct);
                    if (LoseCustomerResponse) throw new HttpRequestException("fixture reset after commit");
                    return Reply(request, Customer);
                }
                Interlocked.Increment(ref SubscriptionPosts);
                PostedCollectionMethod = body.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString();
                if (MalformedRejection) return Reply(request, "{\"errors\":42}", HttpStatusCode.UnprocessableEntity);
                if (RejectSubscription) return Reply(request, "{\"errors\":[\"fixture rejection\"]}", HttpStatusCode.UnprocessableEntity);
                SubscriptionReference = body.RootElement.GetProperty("subscription").GetProperty("reference").GetString();
                SubscriptionEntered?.TrySetResult(true);
                if (ReleaseSubscription != null) await ReleaseSubscription.Task.WaitAsync(ct);
                if (LoseSubscriptionResponse) throw new HttpRequestException("fixture reset after commit");
                if (MalformedSubscriptionResponse) return Reply(request, "{\"subscription\":\"invalid\"}");
                return Reply(request, Subscription);
            }
            if (path == "/customers/lookup.json")
            {
                if (CorruptCustomerLookup) return Reply(request, "{\"customer\":\"invalid\"}");
                return CustomerReference == null || HideCustomer ? Reply(request, "{}", HttpStatusCode.NotFound) : Reply(request, Customer);
            }
            if (path == "/subscriptions/lookup.json") return SubscriptionReference == null || HideSubscription ? Reply(request, "", HttpStatusCode.NotFound) : Reply(request, Subscription);
            if (path == "/customers/51/subscriptions.json") return Reply(request, "[" + Subscription + "]");
            throw new InvalidOperationException("Unexpected fixture route: " + path);
        }
    }

    private sealed class Harness : IDisposable
    {
        public Provider Provider { get; } = new();
        private readonly InMemoryDatabaseRoot root = new();
        private readonly string database = Guid.NewGuid().ToString();
        private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient client;
        public MaxioAdvancedBilling.MaxioAdvancedBillingClient Client => client;
        public Harness()
        {
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions { Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled(), Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions { LoggerFactory = NullLoggerFactory.Instance } };
            options.Server.Production.Us.BaseUrl = "https://fixture.invalid";
            client = new(new HttpClient(Provider), options);
        }
        public CatalogContext Context() => new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(database, root).Options);
        public SubscriptionService Service(CatalogContext db) => new(client, db, new MaxioSettings { Subdomain = "fixture", ProductFamilyHandle = "fixture-family" }, NullLogger<SubscriptionService>.Instance);
        public async Task<ShopperSubscription> Enroll(string handle = Handle)
        {
            using var db = Context();
            return await Service(db).Enroll(Shopper, handle, CancellationToken.None);
        }
        public void Dispose() => Provider.Dispose();
    }

    [TestMethod]
    public async Task RepeatedEnrollmentReturnsSameLiveSubscription()
    {
        using var h = new Harness();
        Assert.AreEqual(61, (await h.Enroll()).Id);
        var again = await h.Enroll();
        Assert.AreEqual(61, again.Id);
        Assert.AreEqual("active", again.State);
        Assert.IsNotNull(again.NextBillingDate);
        Assert.AreEqual(1, h.Provider.CustomerPosts);
        Assert.AreEqual(1, h.Provider.SubscriptionPosts);
        Assert.AreEqual("remittance", h.Provider.PostedCollectionMethod);
    }

    [TestMethod]
    public async Task LegacySiteUsesSupportedInvoiceCollection()
    {
        using var h = new Harness(); h.Provider.RelationshipInvoicing = false;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual("invoice", h.Provider.PostedCollectionMethod);
    }

    [TestMethod]
    public async Task LostCustomerResponseReconcilesWithoutResend()
    {
        using var h = new Harness(); h.Provider.LoseCustomerResponse = true;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual(1, h.Provider.CustomerPosts);
        using var db = h.Context();
        Assert.IsTrue((await db.BillingClaims.ToListAsync()).All(x => x.State == "complete"));
    }

    [TestMethod]
    public async Task LostSubscriptionResponseReconcilesWithoutResend()
    {
        using var h = new Harness(); h.Provider.LoseSubscriptionResponse = true;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual(1, h.Provider.SubscriptionPosts);
    }

    [TestMethod]
    public async Task UnknownCustomerClaimSettlesOnLaterEnrollmentWithoutResend()
    {
        using var h = new Harness(); h.Provider.LoseCustomerResponse = true; h.Provider.HideCustomer = true;
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
        Assert.AreEqual("billing-outcome-unknown", error.Code);
        await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
        h.Provider.HideCustomer = false;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual(1, h.Provider.CustomerPosts);
    }

    [TestMethod]
    public async Task CatalogCapReturnsExplicitIncompleteError()
    {
        using var h = new Harness(); h.Provider.FullCatalogPages = true;
        using var db = h.Context();
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Service(db).Plans(CancellationToken.None));
        Assert.AreEqual("catalog-incomplete", error.Code);
        Assert.AreEqual(100, h.Provider.CatalogPages);
    }

    [TestMethod]
    public async Task MalformedSuccessReconcilesWithoutResend()
    {
        using var h = new Harness(); h.Provider.MalformedSubscriptionResponse = true;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual(1, h.Provider.SubscriptionPosts);
    }

    [TestMethod]
    public async Task UnknownClaimNeverResendsAndMineSettlesLater()
    {
        using var h = new Harness(); h.Provider.LoseSubscriptionResponse = true; h.Provider.HideSubscription = true;
        var unknown = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
        Assert.AreEqual("billing-outcome-unknown", unknown.Code);
        await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
        Assert.AreEqual(1, h.Provider.SubscriptionPosts);
        h.Provider.HideSubscription = false;
        using var db = h.Context();
        var mine = await h.Service(db).Mine(Shopper, CancellationToken.None);
        Assert.AreEqual(61, mine.Single().Id);
        Assert.IsTrue((await db.BillingClaims.ToListAsync()).All(x => x.State == "complete"));
    }

    [TestMethod]
    public async Task ConcurrentClickCannotSendSecondSubscriptionPost()
    {
        using var h = new Harness();
        h.Provider.SubscriptionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.ReleaseSubscription = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.HideSubscription = true;
        var first = h.Enroll();
        await h.Provider.SubscriptionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
            Assert.AreEqual(1, h.Provider.SubscriptionPosts);
        }
        finally { h.Provider.HideSubscription = false; h.Provider.ReleaseSubscription.TrySetResult(true); }
        Assert.AreEqual(61, (await first).Id);
    }

    [TestMethod]
    public async Task ConcurrentCustomerClaimRejectsBeforeSecondPost()
    {
        using var h = new Harness();
        h.Provider.CustomerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.ReleaseCustomer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.HideCustomer = true;
        var first = h.Enroll();
        await h.Provider.CustomerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll());
            Assert.AreEqual(1, h.Provider.CustomerPosts);
        }
        finally { h.Provider.HideCustomer = false; h.Provider.ReleaseCustomer.TrySetResult(true); }
        Assert.AreEqual(61, (await first).Id);
    }

    [TestMethod]
    public async Task UnofferedPlanCannotCreateCustomerOrSubscription()
    {
        using var h = new Harness();
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Enroll("unoffered"));
        Assert.AreEqual("plan-not-offered", error.Code);
        Assert.AreEqual(0, h.Provider.CustomerPosts + h.Provider.SubscriptionPosts);
    }

    [TestMethod]
    public async Task MalformedLookupCannotBecomeAbsenceOrAuthorizeWrite()
    {
        using var h = new Harness(); h.Provider.CorruptCustomerLookup = true;
        await Assert.ThrowsExceptionAsync<MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException>(() => h.Enroll());
        Assert.AreEqual(0, h.Provider.CustomerPosts);
    }

    [TestMethod]
    public async Task DefiniteRejectionReleasesClaimForCorrectedRetry()
    {
        using var h = new Harness(); h.Provider.RejectSubscription = true;
        await Assert.ThrowsExceptionAsync<MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>>(() => h.Enroll());
        using (var db = h.Context()) Assert.AreEqual(1, await db.BillingClaims.CountAsync());
        h.Provider.RejectSubscription = false;
        Assert.AreEqual(61, (await h.Enroll()).Id);
        Assert.AreEqual(2, h.Provider.SubscriptionPosts);
    }

    [TestMethod]
    public async Task MalformedErrorKeepsProviderRejectionAndReleasesClaim()
    {
        using var h = new Harness(); h.Provider.MalformedRejection = true;
        var error = await Assert.ThrowsExceptionAsync<MaxioAdvancedBilling.Core.Exceptions.ResponseDeserializationException>(() => h.Enroll());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        using var db = h.Context();
        Assert.AreEqual(1, await db.BillingClaims.CountAsync());
        Assert.AreEqual(1, h.Provider.SubscriptionPosts);
    }

    [TestMethod]
    public async Task SubscriptionRoutesRequireBearerAuthentication()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Maxio:ApiKey"] = "fixture-key", ["Maxio:Subdomain"] = "fixture", ["Maxio:ProductFamilyHandle"] = "fixture-family", ["UseOnlyInMemoryDatabase"] = "true" })));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = Handle, customerId = 999 })).StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedShopperUsesIdentityAndIgnoresClientCustomerId()
    {
        using var h = new Harness();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Maxio:ApiKey"] = "fixture-key", ["Maxio:Subdomain"] = "fixture", ["Maxio:ProductFamilyHandle"] = "fixture-family", ["UseOnlyInMemoryDatabase"] = "true" }));
            builder.ConfigureServices(services => services.AddSingleton(h.Client));
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = Handle, customerId = 999, userId = "someone-else" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync("demouser@microsoft.com");
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var claims = await db.BillingClaims.Where(x => x.Reference == h.Provider.CustomerReference || x.Reference == h.Provider.SubscriptionReference).ToListAsync();
        Assert.AreEqual(2, claims.Count);
        Assert.IsTrue(claims.All(x => x.UserId == user!.Id));
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
    }
}
