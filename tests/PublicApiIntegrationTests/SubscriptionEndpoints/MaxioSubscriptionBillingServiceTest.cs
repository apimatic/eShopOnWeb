using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingServiceTest
{
    [TestMethod]
    public async Task ListsPlansByResolvingConfiguredFamilyHandle()
    {
        var handler = new SandboxStubHandler();
        var fixture = new BillingFixture(handler);
        await using var context = fixture.NewContext();
        var service = fixture.NewService(context);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("basic-plan", plans[0].Handle);
        Assert.AreEqual(2900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.IsTrue(handler.Requests.Any(x => x.Path == "/product_families.json"));
        Assert.IsTrue(handler.Requests.Any(x =>
            x.Path == "/product_families/42/products.json" &&
            x.Query.Contains("include_archived=false", StringComparison.Ordinal) &&
            x.Query.Contains("per_page=100", StringComparison.Ordinal)));
        Assert.IsTrue(handler.Requests.All(x => x.AuthorizationScheme == "Basic"));
    }

    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOnlyOneSubscription()
    {
        var handler = new SandboxStubHandler();
        var fixture = new BillingFixture(handler);
        await using var firstContext = fixture.NewContext();
        await using var secondContext = fixture.NewContext();
        var firstService = fixture.NewService(firstContext);
        var secondService = fixture.NewService(secondContext);
        var user = new ApplicationUser
        {
            Id = "stable-user-id",
            UserName = "subscriber@example.test",
            Email = "subscriber@example.test"
        };

        var results = await Task.WhenAll(
            firstService.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            secondService.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, handler.SubscriptionPostCount);
        Assert.AreEqual(results[0].Id, results[1].Id);
        Assert.AreEqual("eshop-pro", results[0].PlanHandle);
        Assert.AreEqual("active", results[0].State);
        Assert.IsNotNull(results[0].NextBillingDate);
        var post = handler.Requests.Single(x => x.Path == "/subscriptions.json");
        StringAssert.Contains(post.Body!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(post.Body!, "\"customer_reference\"");
        StringAssert.Contains(post.Body!, "\"reference\"");
        StringAssert.Contains(post.Body!, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(post.Body!.Contains("product_id", StringComparison.Ordinal));
        Assert.IsFalse(post.Body.Contains("customer_id", StringComparison.Ordinal));
        Assert.IsFalse(post.Body.Contains("payment_profile", StringComparison.Ordinal));
        Assert.IsFalse(post.Body.Contains("credit_card", StringComparison.Ordinal));
        Assert.IsFalse(post.Body.Contains("bank_account", StringComparison.Ordinal));
    }

    private sealed class BillingFixture
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly MaxioAdvancedBillingClient _client;
        private readonly MaxioOptions _options;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly MaxioWriteGuard _writeGuard = new();
        private readonly SubscriptionKeyLock _keyLock = new();

        public BillingFixture(HttpMessageHandler terminalHandler)
        {
            _options = new MaxioOptions
            {
                ApiKey = Guid.NewGuid().ToString("N"),
                Subdomain = "unused",
                ProductFamilyHandle = "eshop-subscribe",
                BaseUrl = "https://maxio.test"
            };
            var writeHandler = new MaxioSingleWriteHandler(_writeGuard) { InnerHandler = terminalHandler };
            var httpClient = new HttpClient(writeHandler) { Timeout = TimeSpan.FromSeconds(5) };
            var sdkOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(1)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = Guid.NewGuid().ToString("N"),
                    Password = Guid.NewGuid().ToString("N")
                }
            };
            sdkOptions.Server.Production.Us.BaseUrl = _options.BaseUrl;
            _client = new MaxioAdvancedBillingClient(httpClient, sdkOptions);
        }

        public AppIdentityDbContext NewContext()
        {
            var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("MaxioSubscriptionTests", _databaseRoot)
                .Options;
            return new AppIdentityDbContext(options);
        }

        public MaxioSubscriptionBillingService NewService(AppIdentityDbContext context) =>
            new(
                _client,
                Options.Create(_options),
                context,
                _cache,
                _writeGuard,
                _keyLock,
                NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private sealed class SandboxStubHandler : HttpMessageHandler
    {
        private readonly object _stateLock = new();
        private bool _customerCreated;
        private bool _subscriptionCreated;
        private int _subscriptionPostCount;

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        public int SubscriptionPostCount => Volatile.Read(ref _subscriptionPostCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new CapturedRequest(
                request.Method,
                path,
                request.RequestUri.Query,
                body,
                request.Headers.Authorization?.Scheme));

            if (request.Method == HttpMethod.Get && path == "/product_families.json")
                return Json("[{\"product_family\":{\"id\":42,\"handle\":\"eshop-subscribe\",\"archived_at\":null}}]");
            if (request.Method == HttpMethod.Get && path == "/product_families/42/products.json")
                return Json("[{\"product\":" + ProductJson("basic-plan", "Basic Plan", 2900) + "}]");
            if (request.Method == HttpMethod.Get && path.StartsWith("/products/handle/", StringComparison.Ordinal))
            {
                var handle = path.Contains("eshop-pro", StringComparison.Ordinal) ? "eshop-pro" : "basic-plan";
                return Json("{\"product\":" + ProductJson(handle, handle == "eshop-pro" ? "Pro Plan" : "Basic Plan", handle == "eshop-pro" ? 29900 : 2900) + "}");
            }
            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                lock (_stateLock)
                {
                    return _customerCreated
                        ? Json("{\"customer\":{\"id\":7,\"reference\":\"" + CustomerReferenceFromQuery(request.RequestUri.Query) + "\",\"first_name\":\"subscriber\",\"last_name\":\"Customer\",\"email\":\"subscriber@example.test\"}}")
                        : Empty(HttpStatusCode.NotFound);
                }
            }
            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                lock (_stateLock) _customerCreated = true;
                var reference = ExtractJsonValue(body!, "reference");
                return Json("{\"customer\":{\"id\":7,\"reference\":\"" + reference + "\",\"first_name\":\"subscriber\",\"last_name\":\"Customer\",\"email\":\"subscriber@example.test\"}}");
            }
            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                lock (_stateLock)
                {
                    return _subscriptionCreated ? Json(SubscriptionEnvelope()) : Empty(HttpStatusCode.NotFound);
                }
            }
            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                Interlocked.Increment(ref _subscriptionPostCount);
                await Task.Delay(50, cancellationToken);
                lock (_stateLock) _subscriptionCreated = true;
                return Json(SubscriptionEnvelope());
            }

            return Empty(HttpStatusCode.NotFound);
        }

        private static string ProductJson(string handle, string name, long price) =>
            "{\"id\":1,\"name\":\"" + name + "\",\"handle\":\"" + handle +
            "\",\"description\":\"Seeded plan\",\"price_in_cents\":" + price +
            ",\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":null," +
            "\"product_family\":{\"handle\":\"eshop-subscribe\"}}";

        private static string SubscriptionEnvelope() =>
            "{\"subscription\":{\"id\":99,\"reference\":\"eshop-sub-fixed\"," +
            "\"state\":\"active\",\"product_price_in_cents\":29900," +
            "\"next_assessment_at\":\"2026-09-27T00:00:00Z\",\"currency\":\"USD\"," +
            "\"customer\":{\"reference\":\"eshop-customer-" + HashForTest("stable-user-id") + "\"}," +
            "\"product\":" + ProductJson("eshop-pro", "Pro Plan", 29900) + "}}";

        private static string HashForTest(string value)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant()[..32];
        }

        private static string CustomerReferenceFromQuery(string query) =>
            Uri.UnescapeDataString(query.Split("reference=", StringSplitOptions.None)[1].Split('&')[0]);

        private static string ExtractJsonValue(string json, string property)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.GetProperty("customer").GetProperty(property).GetString()!;
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Empty(HttpStatusCode statusCode) => new(statusCode)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        };
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string? Body,
        string? AuthorizationScheme);
}
