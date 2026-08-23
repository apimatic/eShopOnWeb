using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task LoadsPlansUsingStableFamilyHandleAndExactSdkRoutes()
    {
        var handler = new StubHttpMessageHandler(new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => Json(HttpStatusCode.OK, """[{"product_family":{"id":42,"handle":"family"}}]"""),
            _ => Json(HttpStatusCode.OK, """{"site":{"currency":"USD","relationship_invoicing_enabled":true}}"""),
            _ => Json(HttpStatusCode.OK, """[{"product":{"id":99,"name":"Pro","handle":"eshop-pro","description":"Monthly pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":42,"handle":"family"}}}]""")
        });
        var service = CreateService(handler, new InMemoryDatabaseRoot(), "plans");

        var plans = await service.GetPlansAsync(default);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("USD", plans[0].Currency);

        var requests = handler.Requests.ToArray();
        Assert.AreEqual("/product_families.json", requests[0].Uri.AbsolutePath);
        Assert.AreEqual("/site.json", requests[1].Uri.AbsolutePath);
        Assert.AreEqual("/product_families/42/products.json", requests[2].Uri.AbsolutePath);
        Assert.IsTrue(requests[2].Uri.Query.Contains("include_archived=false", StringComparison.Ordinal));
        Assert.IsTrue(requests[2].Uri.Query.Contains("page=1", StringComparison.Ordinal));
        Assert.IsTrue(requests[2].Uri.Query.Contains("per_page=100", StringComparison.Ordinal));
        Assert.IsTrue(requests.All(x => x.Authorization is not null && x.Authorization.StartsWith("Basic ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReplaysSameSubscriptionWithoutASecondCreate()
    {
        const string userId = "shopper-id";
        const string handle = "eshop-pro";
        var customerReference = Reference("eshop-cust", userId, 24);
        var subscriptionReference = Reference("eshop-sub", $"{userId}\n{handle}", 32);
        var subscriptionJson = SubscriptionJson(customerReference, subscriptionReference);
        using var createReached = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim();

        var handler = new StubHttpMessageHandler(new Func<HttpRequestMessage, HttpResponseMessage>[]
        {
            _ => Empty(HttpStatusCode.NotFound),
            _ => Json(HttpStatusCode.OK, """[{"product_family":{"id":42,"handle":"family"}}]"""),
            _ => Json(HttpStatusCode.OK, """{"site":{"currency":"USD","relationship_invoicing_enabled":true}}"""),
            _ => Json(HttpStatusCode.OK, """[{"product":{"id":99,"name":"Pro","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":42,"handle":"family"}}}]"""),
            _ => Empty(HttpStatusCode.NotFound),
            _ => Json(HttpStatusCode.Created, $"{{\"customer\":{{\"id\":7,\"reference\":\"{customerReference}\"}}}}"),
            _ =>
            {
                createReached.Set();
                Assert.IsTrue(releaseCreate.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting to release the create response.");
                return Json(HttpStatusCode.Created, subscriptionJson);
            },
            _ => Json(HttpStatusCode.OK, subscriptionJson)
        });
        var root = new InMemoryDatabaseRoot();
        var locks = new SubscriptionOperationLocks();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = CreateClient(handler);
        var firstService = CreateService(client, root, "subscribe", locks, cache);
        var secondService = CreateService(client, root, "subscribe", locks, cache);
        var user = new BillingUser(userId, "shopper@example.test", "Shopper", "Example");

        var firstTask = Task.Run(() => firstService.SubscribeAsync(user, handle, default));
        Assert.IsTrue(createReached.Wait(TimeSpan.FromSeconds(5)), "The first request never reached subscription creation.");
        var replayTask = Task.Run(() => secondService.SubscribeAsync(user, handle, default));
        releaseCreate.Set();
        var results = await Task.WhenAll(firstTask, replayTask);
        var first = results.Single(x => x.Created);
        var replay = results.Single(x => !x.Created);

        Assert.IsTrue(first.Created);
        Assert.IsFalse(replay.Created);
        Assert.AreEqual(first.Subscription.Id, replay.Subscription.Id);
        Assert.AreEqual(subscriptionReference, replay.Subscription.Reference);

        var requests = handler.Requests.ToArray();
        Assert.AreEqual(1, requests.Count(x => x.Method == HttpMethod.Post && x.Uri.AbsolutePath == "/subscriptions.json"));
        Assert.AreEqual(1, requests.Count(x => x.Method == HttpMethod.Post && x.Uri.AbsolutePath == "/customers.json"));
        Assert.IsTrue(requests.Single(x => x.Uri.AbsolutePath == "/subscriptions.json").Body!.Contains($"\"reference\":\"{subscriptionReference}\"", StringComparison.Ordinal));
        Assert.IsTrue(requests.Single(x => x.Uri.AbsolutePath == "/subscriptions.json").Body!.Contains("\"product_handle\":\"eshop-pro\"", StringComparison.Ordinal));
        Assert.IsTrue(requests.Single(x => x.Uri.AbsolutePath == "/subscriptions.json").Body!.Contains("\"payment_collection_method\":\"remittance\"", StringComparison.Ordinal));
    }

    private static SubscriptionBillingService CreateService(StubHttpMessageHandler handler, InMemoryDatabaseRoot root, string databaseName) =>
        CreateService(CreateClient(handler), root, databaseName, new SubscriptionOperationLocks(), new MemoryCache(new MemoryCacheOptions()));

    private static SubscriptionBillingService CreateService(
        MaxioAdvancedBillingClient client,
        InMemoryDatabaseRoot root,
        string databaseName,
        SubscriptionOperationLocks locks,
        IMemoryCache cache)
    {
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options);
        db.Database.EnsureCreated();
        return new SubscriptionBillingService(
            client,
            db,
            cache,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = "family",
                BaseUrl = "https://maxio.test"
            }),
            locks,
            NullLogger<SubscriptionBillingService>.Instance);
    }

    private static MaxioAdvancedBillingClient CreateClient(StubHttpMessageHandler stub)
    {
        var guard = new MaxioWriteOnceHandler { InnerHandler = stub };
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(2)
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        return new MaxioAdvancedBillingClient(new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(2) }, options);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Empty(HttpStatusCode status) => new(status)
    {
        Content = new ByteArrayContent(Array.Empty<byte>())
    };

    private static string Reference(string prefix, string input, int characters)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"{prefix}-{hash[..characters]}";
    }

    private static string SubscriptionJson(string customerReference, string subscriptionReference) =>
        $"{{\"subscription\":{{\"id\":9,\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2026-09-24T00:00:00Z\",\"current_period_ends_at\":\"2026-09-24T00:00:00Z\",\"reference\":\"{subscriptionReference}\",\"currency\":\"USD\",\"customer\":{{\"id\":7,\"reference\":\"{customerReference}\"}},\"product\":{{\"id\":99,\"handle\":\"eshop-pro\",\"name\":\"Pro\",\"product_family\":{{\"id\":42,\"handle\":\"family\"}},\"interval\":1,\"interval_unit\":\"month\"}}}}}}";
}
