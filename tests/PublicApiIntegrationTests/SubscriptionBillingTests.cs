using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionBillingTests
{
    [TestMethod]
    public async Task RegistrationUsesBaseUrlOverrideVerbatim()
    {
        var wire = new FakeMaxio();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-only", ["Maxio:Subdomain"] = "unused-subdomain",
            ["Maxio:ProductFamilyHandle"] = "test-family", ["Maxio:BaseUrl"] = "http://billing.example.test/prefix"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSubscriptionBilling(configuration);
        services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => wire);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<MaxioAdvancedBilling.MaxioAdvancedBillingClient>();
        Assert.AreEqual("USD", (await client.Sites.ReadSite()).Site.Currency);
        Assert.AreEqual("http", wire.LastScheme);
    }

    [TestMethod]
    public void MissingCredentialsFailDuringRegistration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var error = Assert.ThrowsException<InvalidOperationException>(() => new ServiceCollection().AddSubscriptionBilling(configuration));
        StringAssert.Contains(error.Message, "Maxio:ApiKey");
    }

    [TestMethod]
    public async Task ConcurrentSubmissionsSendOnlyOneCustomerAndSubscription()
    {
        using var h = new Harness();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => h.Service().SubscribeAsync("user", "user@example.test", "pro", default)));
        Assert.AreEqual(1, h.Wire.CustomerPosts);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
        var replay = await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        Assert.AreEqual("complete", replay.Status);
        Assert.AreEqual(29900L, replay.Subscription!.PriceInCents);
        Assert.AreEqual("active", replay.Subscription.State);
        Assert.IsNotNull(replay.Subscription.NextBillingDate);
        Assert.IsTrue(outcomes.All(x => x.Status is "complete" or "pending"));
    }

    [TestMethod]
    public async Task LostCustomerResponseIsReconciledWithoutResending()
    {
        using var h = new Harness();
        h.Wire.LoseCustomerResponse = true;
        var result = await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        Assert.AreEqual("complete", result.Status);
        Assert.AreEqual(1, h.Wire.CustomerPosts);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task LostSubscriptionResponseIsReconciledWithoutResending()
    {
        using var h = new Harness();
        h.Wire.LoseSubscriptionResponse = true;
        var result = await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        Assert.AreEqual("complete", result.Status);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task MalformedSuccessfulWriteIsReconciledWithoutResending()
    {
        using var h = new Harness();
        h.Wire.MalformedSubscriptionWrite = true;
        Assert.AreEqual("complete", (await h.Service().SubscribeAsync("user", "user@example.test", "pro", default)).Status);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task LegacySiteUsesInvoiceCollectionAndPlansHaveSiteCurrency()
    {
        using var h = new Harness();
        h.Wire.RelationshipInvoicing = false;
        var plans = await h.Service().GetPlansAsync(default);
        Assert.AreEqual("USD", plans.Single().Currency);
        Assert.AreEqual("complete", (await h.Service().SubscribeAsync("user", "user@example.test", "pro", default)).Status);
    }

    [TestMethod]
    public async Task UnresolvedWriteRemainsClaimedAndWorkerSettlesIt()
    {
        using var h = new Harness();
        h.Wire.LoseSubscriptionResponse = true;
        h.Wire.HideSubscriptions = true;
        var first = await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        Assert.AreEqual("pending", first.Status);
        var second = await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        Assert.AreEqual("pending", second.Status);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
        Assert.IsTrue((await h.Service().GetSubscriptionsAsync("user", default)).HasPendingEnrollment);
        h.Wire.HideSubscriptions = false;
        await h.Service().ReconcileAsync(default);
        Assert.IsFalse((await h.Service().GetSubscriptionsAsync("user", default)).HasPendingEnrollment);
        Assert.AreEqual("complete", (await h.Service().SubscribeAsync("user", "user@example.test", "pro", default)).Status);
        Assert.AreEqual(1, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task InvalidOrForeignPlanCannotCreateAnything()
    {
        using var h = new Harness();
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Service().SubscribeAsync("user", "user@example.test", "foreign", default));
        Assert.AreEqual(400, error.StatusCode);
        Assert.AreEqual(0, h.Wire.CustomerPosts);
        Assert.AreEqual(0, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task AccountReadsLiveStateAndIsolatesUsers()
    {
        using var h = new Harness();
        await h.Service().SubscribeAsync("user", "user@example.test", "pro", default);
        h.Wire.State = "canceled";
        var mine = await h.Service().GetSubscriptionsAsync("user", default);
        Assert.AreEqual("canceled", mine.Subscriptions.Single().State);
        Assert.AreEqual(0, (await h.Service().GetSubscriptionsAsync("other-user", default)).Subscriptions.Count);
    }

    [TestMethod]
    public async Task RejectedWriteReleasesClaimAndAllowsCorrectedRetry()
    {
        using var h = new Harness();
        h.Wire.RejectSubscription = true;
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Service().SubscribeAsync("user", "user@example.test", "pro", default));
        Assert.AreEqual(422, error.StatusCode);
        h.Wire.RejectSubscription = false;
        Assert.AreEqual("complete", (await h.Service().SubscribeAsync("user", "user@example.test", "pro", default)).Status);
        Assert.AreEqual(1, h.Wire.CustomerPosts);
        Assert.AreEqual(2, h.Wire.SubscriptionPosts);
    }

    [TestMethod]
    public async Task MalformedReadIsSanitizedAndCannotTriggerCreate()
    {
        using var h = new Harness();
        h.Wire.MalformedCustomer = true;
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Service().SubscribeAsync("user", "user@example.test", "pro", default));
        Assert.AreEqual(502, error.StatusCode);
        Assert.IsFalse(error.Message.Contains("MaxioAdvancedBilling"));
        Assert.AreEqual(0, h.Wire.CustomerPosts);
    }

    [TestMethod]
    public async Task PaginationCapFailsInsteadOfReturningPartialPlans()
    {
        using var h = new Harness();
        h.Wire.EndlessPlans = true;
        var error = await Assert.ThrowsExceptionAsync<BillingException>(() => h.Service().GetPlansAsync(default));
        Assert.AreEqual(503, error.StatusCode);
        Assert.AreEqual(100, h.Wire.PlanGets);
    }

    [TestMethod]
    public async Task AllSubscriptionEndpointsRequireBearerAuthentication()
    {
        using var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/subscriptions", new StringContent("{\"productHandle\":\"pro\"}", Encoding.UTF8, "application/json"))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        // Invalid input is rejected locally before contacting any provider.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.PostAsync("/api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _name = Guid.NewGuid().ToString();
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly HttpClient _http;
        private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
        public FakeMaxio Wire { get; } = new();
        public Harness()
        {
            _http = new HttpClient(Wire);
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                Logging = new MaxioAdvancedBilling.Core.Configuration.LoggingOptions { LoggerFactory = NullLoggerFactory.Instance },
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled()
            };
            options.Server.Production.Us.BaseUrl = "https://billing.example.test/prefix";
            _client = new(_http, options);
        }
        public SubscriptionBillingService Service() => new(_client,
            new CatalogContext(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_name, _root).Options),
            new MaxioSettings { ApiKey = "test-only", Subdomain = "unused", ProductFamilyHandle = "test-family", BaseUrl = "https://billing.example.test/prefix" },
            NullLogger<SubscriptionBillingService>.Instance);
        public void Dispose() => _http.Dispose();
    }

    private sealed class FakeMaxio : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, JsonObject> _customers = new();
        private readonly ConcurrentDictionary<string, JsonObject> _subscriptions = new();
        public int CustomerPosts, SubscriptionPosts, PlanGets;
        public bool LoseCustomerResponse, LoseSubscriptionResponse, HideSubscriptions, RejectSubscription, MalformedCustomer, EndlessPlans, MalformedSubscriptionWrite;
        public bool RelationshipInvoicing = true;
        public string State = "active";
        public string? LastScheme;
        private static JsonObject Product() => JsonNode.Parse("""
            {"id":33,"handle":"pro","name":"Pro","description":"Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":22,"handle":"test-family"}}
            """)!.AsObject();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("billing.example.test", request.RequestUri!.Host);
            LastScheme = request.RequestUri.Scheme;
            Assert.IsTrue(request.RequestUri.AbsolutePath.StartsWith("/prefix/"), "BaseUrl path must be preserved verbatim");
            var path = request.RequestUri.AbsolutePath[8..];
            var reference = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("reference="))?[10..] ?? "");
            if (path == "site.json")
                return Json(new JsonObject { ["site"] = new JsonObject { ["currency"] = "USD", ["relationship_invoicing_enabled"] = RelationshipInvoicing, ["test"] = true } });
            if (path.EndsWith("/products.json"))
            {
                Interlocked.Increment(ref PlanGets);
                Assert.IsTrue(Uri.UnescapeDataString(path).Contains("handle:test-family"));
                return Json(new JsonArray(Enumerable.Range(0, EndlessPlans ? 100 : 1).Select(_ => (JsonNode)new JsonObject { ["product"] = Product() }).ToArray()));
            }
            if (path == "customers/lookup.json")
            {
                if (MalformedCustomer) return Json(new JsonObject());
                return _customers.TryGetValue(reference, out var c) ? Json(new JsonObject { ["customer"] = c.DeepClone() }) : Missing();
            }
            if (path == "subscriptions/lookup.json")
                return !HideSubscriptions && _subscriptions.TryGetValue(reference, out var s) ? Subscription(s) : Missing();
            if (request.Method == HttpMethod.Post && path == "customers.json")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!["customer"]!;
                Interlocked.Increment(ref CustomerPosts);
                var c = new JsonObject { ["id"] = 71, ["reference"] = body["reference"]!.GetValue<string>() };
                _customers[c["reference"]!.GetValue<string>()] = c;
                if (LoseCustomerResponse) throw new HttpRequestException("simulated lost customer response");
                return Json(new JsonObject { ["customer"] = c.DeepClone() });
            }
            if (request.Method == HttpMethod.Post && path == "subscriptions.json")
            {
                Interlocked.Increment(ref SubscriptionPosts);
                if (RejectSubscription) return Json(new JsonObject { ["errors"] = new JsonArray("rejected") }, HttpStatusCode.UnprocessableEntity);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!["subscription"]!;
                Assert.AreEqual("pro", body["product_handle"]!.GetValue<string>());
                Assert.AreEqual(71, body["customer_id"]!.GetValue<int>());
                Assert.AreEqual(RelationshipInvoicing ? "remittance" : "invoice", body["payment_collection_method"]!.GetValue<string>());
                var s = new JsonObject { ["id"] = 91, ["reference"] = body["reference"]!.GetValue<string>(), ["product"] = Product(),
                    ["customer"] = new JsonObject { ["id"] = 71 }, ["product_price_in_cents"] = 29900, ["currency"] = "USD",
                    ["next_assessment_at"] = "2026-11-15T00:00:00Z" };
                _subscriptions[s["reference"]!.GetValue<string>()] = s;
                if (LoseSubscriptionResponse) throw new HttpRequestException("simulated lost subscription response");
                if (MalformedSubscriptionWrite) return Json(new JsonObject { ["subscription"] = "malformed" });
                return Subscription(s);
            }
            if (path == "customers/71/subscriptions.json")
                return Json(new JsonArray(_subscriptions.Values.Select(s => (JsonNode)SubscriptionNode(s)).ToArray()));
            throw new InvalidOperationException("Unexpected SDK request in fake provider");
        }
        private JsonObject SubscriptionNode(JsonObject s)
        {
            var clone = s.DeepClone(); clone["state"] = State;
            return new JsonObject { ["subscription"] = clone };
        }
        private HttpResponseMessage Subscription(JsonObject s) => Json(SubscriptionNode(s));
        private static HttpResponseMessage Missing() => Json(null, HttpStatusCode.NotFound);
        private static HttpResponseMessage Json(JsonNode? node, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(node?.ToJsonString() ?? "", Encoding.UTF8, "application/json") };
    }
}
