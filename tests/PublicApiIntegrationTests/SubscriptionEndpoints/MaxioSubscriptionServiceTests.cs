using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string FamilyJson = """{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}""";
    private const string ProductJson = """{"product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}""";
    private const string CustomerJson = """{"customer":{"id":123,"first_name":"Test","last_name":"User","email":"demouser@microsoft.com","reference":"eshop-user-u1"}}""";
    private const string SubscriptionJson = """{"subscription":{"id":900,"state":"active","product_price_in_cents":29900,"currency":"USD","reference":"eshop-sub-u1-eshop-pro","next_assessment_at":"2026-10-09T00:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900}}}""";

    [TestMethod]
    public async Task GetPlansMapsProductsFromConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("product_families") && path.EndsWith("products.json"))
            {
                return Json(HttpStatusCode.OK, $"[{ProductJson}]");
            }
            return Json(HttpStatusCode.OK, $"[{FamilyJson}]");
        });

        var service = CreateService(handler);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual("eShop Pro", plans[0].Name);
        Assert.AreEqual(29900, plans[0].PriceInCents);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionWhenNoneExists()
    {
        var createCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions.json"))
            {
                createCalls++;
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }
            if (request.Method == HttpMethod.Post && path.Contains("customers.json"))
            {
                return Json(HttpStatusCode.Created, CustomerJson);
            }
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (path.EndsWith("products.json"))
            {
                return Json(HttpStatusCode.OK, $"[{ProductJson}]");
            }
            return Json(HttpStatusCode.OK, $"[{FamilyJson}]");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(1, createCalls);
        var sentBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        StringAssert.Contains(sentBody, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual(900, result.SubscriptionId);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual("eShop Pro", result.PlanName);
        Assert.AreEqual(29900, result.PriceInCents);
        Assert.IsNotNull(result.NextBillingDate);
    }

    [TestMethod]
    public async Task SubscribeReturnsExistingSubscriptionForRepeatedRequest()
    {
        var createCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions.json"))
            {
                createCalls++;
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (path.EndsWith("products.json"))
            {
                return Json(HttpStatusCode.OK, $"[{ProductJson}]");
            }
            return Json(HttpStatusCode.OK, $"[{FamilyJson}]");
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(0, createCalls);
        Assert.IsTrue(first.AlreadySubscribed);
        Assert.IsTrue(second.AlreadySubscribed);
        Assert.AreEqual(first.SubscriptionId, second.SubscriptionId);
    }

    [TestMethod]
    public async Task SubscribeWithUnknownPlanReturns404()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("products.json"))
            {
                return Json(HttpStatusCode.OK, $"[{ProductJson}]");
            }
            return Json(HttpStatusCode.OK, $"[{FamilyJson}]");
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(User, "no-such-plan", CancellationToken.None));

        Assert.AreEqual(404, ex.RecommendedHttpStatusCode);
    }

    [TestMethod]
    public async Task SubscribeSurfacesProviderCredentialsFailureAsBadGateway()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("products.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(User, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(502, ex.RecommendedHttpStatusCode);
        Assert.AreEqual(401, ex.ProviderStatusCode);
        StringAssert.Contains(ex.Message, "credentials");
    }

    [TestMethod]
    public async Task GetMySubscriptionsReturnsEmptyWhenCustomerUnknown()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var service = CreateService(handler);

        var subscriptions = await service.GetMySubscriptionsAsync(User, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    private static MaxioUserInfo User { get; } = new("u1", "demouser@microsoft.com", "Demo", "User");

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.FromMilliseconds(1),
                Timeout = TimeSpan.FromSeconds(2)
            },
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = "test-key",
                Password = "x"
            }
        };
        clientOptions.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioSubscriptionService(client, options, new MemoryCache(new MemoryCacheOptions()));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
