using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionBillingTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _database = Guid.NewGuid().ToString();
    private readonly FakeMaxio _provider = new();
    private readonly ApplicationUser _user = new() { Id = "shopper-one", Email = "one@example.test" };
    private static SubscribeRequest Request(string handle = "test-pro") => new() { ProductHandle = handle, FirstName = "Test", LastName = "Shopper" };
    private CatalogContext Context() => new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_database, _root).Options);
    private SubscriptionService Service(CatalogContext db)
    {
        var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { MaxRetries = 0, Timeout = TimeSpan.FromSeconds(2) },
            Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
        };
        options.Server.Production.Us.BaseUrl = "https://billing.example.test";
        var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(new HttpClient(_provider, false), options);
        return new SubscriptionService(client, new MaxioSettings { ApiKey = "test-only", Subdomain = "test-only", ProductFamilyHandle = "test-family" },
            new BillingClaimStore(db), db, NullLogger<SubscriptionService>.Instance);
    }

    [TestMethod]
    public async Task EnrollsAndReadsProviderStateWithoutExposingCustomerData()
    {
        using var db = Context();
        var service = Service(db);
        var result = await service.SubscribeAsync(_user, Request(), default);
        Assert.AreEqual("confirmed", result.Status);
        Assert.AreEqual(29900L, result.Subscription!.PriceInCents);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.IsNotNull(result.Subscription.NextBillingDate);
        Assert.AreEqual("remittance", _provider.CollectionMethod);
        Assert.AreEqual(_user.Email, _provider.Email);
        var mine = await service.GetMineAsync(_user, default);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.IsFalse(mine.HasPendingEnrollment);
        Assert.AreEqual(2, await db.BillingClaims.CountAsync(x => x.ProviderId != null));
    }

    [TestMethod]
    public async Task ConcurrentDoubleClicksSendOneCustomerAndOneSubscription()
    {
        _provider.DelayWrites = true;
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var db = Context();
            return await Service(db).SubscribeAsync(_user, Request(), default);
        }));
        Assert.AreEqual(1, _provider.CustomerWrites);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
        Assert.IsTrue(results.All(x => x.Status is "pending" or "confirmed"));
        using var finalDb = Context();
        var final = await Service(finalDb).SubscribeAsync(_user, Request(), default);
        Assert.AreEqual("confirmed", final.Status);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task LostCustomerResponseReconcilesWithoutRepeatingWrite()
    {
        _provider.DropCustomerResponse = true;
        using var db = Context();
        var result = await Service(db).SubscribeAsync(_user, Request(), default);
        Assert.AreEqual("confirmed", result.Status);
        Assert.AreEqual(1, _provider.CustomerWrites);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
        Assert.IsTrue(await db.BillingClaims.AllAsync(x => x.ProviderId != null));
    }

    [TestMethod]
    public async Task LostSubscriptionResponseReconcilesWithoutRepeatingWrite()
    {
        _provider.DropSubscriptionResponse = true;
        using var db = Context();
        var result = await Service(db).SubscribeAsync(_user, Request(), default);
        Assert.AreEqual("confirmed", result.Status);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
        Assert.IsTrue(await db.BillingClaims.AllAsync(x => x.ProviderId != null));
    }

    [TestMethod]
    public async Task UnresolvedWriteRemainsClaimedUntilBackgroundReconciliation()
    {
        _provider.DropSubscriptionResponse = true;
        _provider.HideSubscription = true;
        using (var db = Context())
        {
            var result = await Service(db).SubscribeAsync(_user, Request(), default);
            Assert.AreEqual("pending", result.Status);
        }
        using (var db = Context())
        {
            Assert.AreEqual("pending", (await Service(db).SubscribeAsync(_user, Request(), default)).Status);
            Assert.AreEqual(1, _provider.SubscriptionWrites);
            var claim = await db.BillingClaims.SingleAsync(x => x.Kind == "subscription");
            Assert.IsNull(claim.ProviderId);
            _provider.HideSubscription = false;
            await Service(db).ReconcileAsync(claim, default);
            Assert.IsNotNull(claim.ProviderId);
        }
        using var final = Context();
        Assert.AreEqual("confirmed", (await Service(final).SubscribeAsync(_user, Request(), default)).Status);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task ProviderPendingStateIsNotConfirmed()
    {
        _provider.SubscriptionState = "pending";
        using var db = Context();
        var result = await Service(db).SubscribeAsync(_user, Request(), default);
        Assert.AreEqual("pending", result.Status);
        Assert.IsNull((await db.BillingClaims.SingleAsync(x => x.Kind == "subscription")).ProviderId);
    }

    [TestMethod]
    public async Task DefiniteRejectionReleasesClaimForCorrectedRequest()
    {
        _provider.RejectSubscription = true;
        using (var db = Context())
        {
            var error = await Assert.ThrowsExceptionAsync<BillingException>(() => Service(db).SubscribeAsync(_user, Request(), default));
            Assert.AreEqual(422, error.StatusCode);
            Assert.AreEqual(0, await db.BillingClaims.CountAsync(x => x.Kind == "subscription"));
        }
        _provider.RejectSubscription = false;
        using var retry = Context();
        Assert.AreEqual("confirmed", (await Service(retry).SubscribeAsync(_user, Request(), default)).Status);
    }

    [TestMethod]
    public async Task MalformedSuccessfulWriteReconcilesInsteadOfRepeating()
    {
        _provider.MalformedSubscriptionResponse = true;
        using var db = Context();
        Assert.AreEqual("confirmed", (await Service(db).SubscribeAsync(_user, Request(), default)).Status);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task CorruptCustomerLookupDoesNotBecomeAbsence()
    {
        _provider.CorruptCustomerLookup = true;
        using var db = Context();
        await Assert.ThrowsExceptionAsync<BillingException>(() => Service(db).SubscribeAsync(_user, Request(), default));
        Assert.AreEqual(0, _provider.CustomerWrites);
        Assert.AreEqual(0, _provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task RejectsUnlistedOrForeignPlanBeforeAnyWrite()
    {
        using var db = Context();
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => Service(db).SubscribeAsync(_user, Request("foreign-plan"), default));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual(0, _provider.CustomerWrites);
        Assert.AreEqual(0, _provider.SubscriptionWrites);
        var plans = await Service(db).GetPlansAsync(default);
        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("test-pro", plans[0].Handle);
    }

    [TestMethod]
    public async Task AccountReadsCannotLeakAnotherUsersSubscription()
    {
        using var db = Context();
        var service = Service(db);
        await service.SubscribeAsync(_user, Request(), default);
        var other = new ApplicationUser { Id = "shopper-two", Email = "two@example.test" };
        Assert.AreEqual(0, (await service.GetMineAsync(other, default)).Subscriptions.Count);
    }

    [TestMethod]
    public async Task LocalMappingLossRecoversProviderSubscription()
    {
        using (var db = Context())
        {
            await Service(db).SubscribeAsync(_user, Request(), default);
            db.BillingClaims.RemoveRange(db.BillingClaims);
            await db.SaveChangesAsync();
        }
        using var restarted = Context();
        Assert.AreEqual("confirmed", (await Service(restarted).SubscribeAsync(_user, Request(), default)).Status);
        Assert.AreEqual(1, _provider.CustomerWrites);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
    }

    [TestMethod]
    public async Task CatalogPageCapReturnsErrorInsteadOfPartialSuccess()
    {
        _provider.FullPagesForever = true;
        using var db = Context();
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => Service(db).GetPlansAsync(default));
        Assert.AreEqual(503, error.StatusCode);
        Assert.AreEqual(50, _provider.CatalogReads);
    }

    [TestMethod]
    public void SettingsFailFastWithoutLeakingSecrets()
    {
        var settings = new MaxioSettings();
        Assert.IsTrue(Assert.ThrowsException<InvalidOperationException>(settings.Validate).Message.Contains("Maxio:ApiKey"));
        settings.ApiKey = "test-only";
        Assert.IsTrue(Assert.ThrowsException<InvalidOperationException>(settings.Validate).Message.Contains("Maxio:Subdomain"));
        settings.Subdomain = "test-only";
        Assert.IsTrue(Assert.ThrowsException<InvalidOperationException>(settings.Validate).Message.Contains("Maxio:ProductFamilyHandle"));
        settings.ProductFamilyHandle = "test-family";
        settings.BaseUrl = "http://127.0.0.1:12345/custom-billing/";
        settings.Validate();
        Assert.AreEqual("http://127.0.0.1:12345/custom-billing/", settings.BaseUrl);
    }

    [TestMethod]
    public async Task HttpEndpointsRequireJwtAndReturnEnrollmentAndAccount()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Maxio:ApiKey", "test-only")
            .UseSetting("Maxio:Subdomain", "test-only")
            .UseSetting("Maxio:ProductFamilyHandle", "test-family")
            .UseSetting("Maxio:BaseUrl", "https://billing.example.test/custom-billing")
            .ConfigureServices(services => services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => _provider)));
        using var http = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/subscriptions", Request())).StatusCode);
        var authResponse = await http.PostAsJsonAsync("/api/authenticate", new { username = "demouser@microsoft.com", password = "Pass@word1" });
        var auth = await authResponse.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.IsTrue(auth!.Result);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var plans = await http.GetFromJsonAsync<SubscriptionPlan[]>("/api/subscription-plans");
        Assert.AreEqual(1, plans!.Length);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/subscriptions", Request("foreign-plan"))).StatusCode);
        var first = await http.PostAsJsonAsync("/api/subscriptions", Request());
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var enrollment = await first.Content.ReadFromJsonAsync<SubscriptionEnrollment>();
        Assert.AreEqual("confirmed", enrollment!.Status);
        var repeat = await http.PostAsJsonAsync("/api/subscriptions", Request());
        var repeated = await repeat.Content.ReadFromJsonAsync<SubscriptionEnrollment>();
        Assert.AreEqual(enrollment.Subscription!.Id, repeated!.Subscription!.Id);
        var mine = await http.GetFromJsonAsync<MySubscriptions>("/api/my-subscriptions");
        Assert.AreEqual(1, mine!.Subscriptions.Count);
        Assert.AreEqual(1, _provider.SubscriptionWrites);
        Assert.IsTrue(_provider.UsedBaseUrlPrefix);
    }

    private sealed class FakeMaxio : HttpMessageHandler
    {
        private string? _customerReference;
        private string? _subscriptionReference;
        public int CustomerWrites, SubscriptionWrites, CatalogReads;
        public bool UsedBaseUrlPrefix, DelayWrites, DropCustomerResponse, DropSubscriptionResponse, HideSubscription,
            RejectSubscription, MalformedSubscriptionResponse, CorruptCustomerLookup, FullPagesForever;
        public string SubscriptionState = "active";
        public string? CollectionMethod, Email;
        private object Product(string handle = "test-pro", string family = "test-family") => new
        {
            id = 101, handle, name = "Test Pro", price_in_cents = 29900, interval = 1, interval_unit = "month",
            require_credit_card = false, product_family = new { handle = family }
        };
        private object Customer() => new { id = 202, reference = _customerReference };
        private object Subscription() => new
        {
            id = 303, reference = _subscriptionReference, customer = Customer(), product = Product(),
            state = SubscriptionState, product_price_in_cents = 29900, currency = "USD", current_period_ends_at = "2027-01-15T00:00:00Z"
        };
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.AreEqual("billing.example.test", request.RequestUri.Host);
            if (path.StartsWith("/custom-billing/", StringComparison.Ordinal))
            {
                UsedBaseUrlPrefix = true;
                path = path.Substring("/custom-billing".Length);
            }
            if (request.Method == HttpMethod.Get && path == "/products.json")
            {
                Interlocked.Increment(ref CatalogReads);
                if (FullPagesForever) return Json(Enumerable.Range(0, 200).Select(_ => new { product = Product() }));
                return Json(new[] { new { product = Product() }, new { product = Product("foreign-plan", "other-family") } });
            }
            if (path == "/customers/lookup.json")
            {
                if (CorruptCustomerLookup) return Json(new { customer = "bad-shape" });
                var reference = Uri.UnescapeDataString(request.RequestUri.Query.Split('=')[1]);
                return _customerReference == reference ? Json(new { customer = Customer() }) : Json(new { }, HttpStatusCode.NotFound);
            }
            if (path == "/subscriptions/lookup.json")
                return _subscriptionReference != null && !HideSubscription ? Json(new { subscription = Subscription() }) : Json(new { }, HttpStatusCode.NotFound);
            if (path == "/customers/202/subscriptions.json") return Json(_subscriptionReference == null ? Array.Empty<object>() : new[] { new { subscription = Subscription() } });
            if (request.Method == HttpMethod.Post)
            {
                // Capture while content is still alive: the SDK disposes it after SendAsync.
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (path == "/customers.json")
                {
                    Interlocked.Increment(ref CustomerWrites);
                    if (DelayWrites) await Task.Delay(40, ct);
                    var customer = body.RootElement.GetProperty("customer");
                    _customerReference = customer.GetProperty("reference").GetString();
                    Email = customer.GetProperty("email").GetString();
                    if (DropCustomerResponse) throw new HttpRequestException("response lost after customer was committed");
                    return Json(new { customer = Customer() });
                }
                if (path == "/subscriptions.json")
                {
                    Interlocked.Increment(ref SubscriptionWrites);
                    if (DelayWrites) await Task.Delay(40, ct);
                    if (RejectSubscription) return Json(new { errors = new[] { "refused" } }, HttpStatusCode.UnprocessableEntity);
                    var subscription = body.RootElement.GetProperty("subscription");
                    Assert.AreEqual(202, subscription.GetProperty("customer_id").GetInt32());
                    Assert.AreEqual("test-pro", subscription.GetProperty("product_handle").GetString());
                    CollectionMethod = subscription.GetProperty("payment_collection_method").GetString();
                    _subscriptionReference = subscription.GetProperty("reference").GetString();
                    if (DropSubscriptionResponse) throw new HttpRequestException("response lost after subscription was committed");
                    if (MalformedSubscriptionResponse) return Json(new { subscription = "bad-shape" });
                    return Json(new { subscription = Subscription() });
                }
            }
            throw new InvalidOperationException("Unexpected SDK request: " + path);
        }
    }
}
