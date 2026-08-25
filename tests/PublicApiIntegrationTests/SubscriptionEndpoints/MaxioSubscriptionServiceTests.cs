using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });
        return new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Route(HttpRequestMessage request, bool customerExists, bool subscriptionExists)
    {
        var path = request.RequestUri!.AbsolutePath;
        var isPost = request.Method == HttpMethod.Post;

        if (path.Contains("product_families") && !path.Contains("products"))
        {
            return Json(HttpStatusCode.OK,
                """[{"product_family":{"id":3023074,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]""");
        }
        if (path.Contains("products"))
        {
            return Json(HttpStatusCode.OK,
                """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");
        }
        if (path.Contains("customers") && path.Contains("lookup"))
        {
            return customerExists
                ? Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"user-1","first_name":"demo","last_name":"Customer","email":"demo@example.com"}}""")
                : Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
        }
        if (path.Contains("customers") && isPost)
        {
            return Json(HttpStatusCode.Created, """{"customer":{"id":123,"reference":"user-1","first_name":"demo","last_name":"Customer","email":"demo@example.com"}}""");
        }
        if (path.Contains("customers") && path.Contains("subscriptions"))
        {
            return Json(HttpStatusCode.OK,
                """[{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-25T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}]""");
        }
        if (path.Contains("subscriptions") && path.Contains("lookup"))
        {
            return subscriptionExists
                ? Json(HttpStatusCode.OK, """{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-25T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}""")
                : Json(HttpStatusCode.NotFound, "");
        }
        if (path.Contains("subscriptions") && isPost)
        {
            return Json(HttpStatusCode.Created, """{"subscription":{"id":555,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-25T00:00:00Z","product":{"name":"Pro Plan","handle":"eshop-pro"}}}""");
        }
        return Json(HttpStatusCode.NotFound, """{"errors":"unexpected request"}""");
    }

    [TestMethod]
    public async Task ListPlans_ReturnsPlansFromConfiguredFamily()
    {
        var handler = new StubHandler(r => Route(r, customerExists: false, subscriptionExists: false));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900L, plans[0].PriceInCents);
        Assert.AreEqual("month", plans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task Subscribe_WhenSubscriptionExists_DoesNotCreateAnything()
    {
        var handler = new StubHandler(r => Route(r, customerExists: true, subscriptionExists: true));
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "demo@example.com", "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(29900L, subscription.UnitPriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingAt);
        // Idempotency: no customer or subscription was created.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_WhenNothingExists_CreatesCustomerThenSubscription()
    {
        var handler = new StubHandler(r => Route(r, customerExists: false, subscriptionExists: false));
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "demo@example.com", "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.AreEqual(2, posts.Count);
        Assert.IsTrue(posts[0].RequestUri!.AbsolutePath.Contains("customers"));
        Assert.IsTrue(posts[1].RequestUri!.AbsolutePath.Contains("subscriptions"));
    }

    [TestMethod]
    public async Task ListSubscriptions_WhenCustomerMissing_ReturnsEmpty()
    {
        var handler = new StubHandler(r => Route(r, customerExists: false, subscriptionExists: false));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-1", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task ListSubscriptions_WhenCustomerExists_MapsSubscriptions()
    {
        var handler = new StubHandler(r => Route(r, customerExists: true, subscriptionExists: true));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-1", CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.AreEqual("Pro Plan", subscriptions[0].ProductName);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingAt);
    }

    [TestMethod]
    public async Task ListPlans_WhenProviderFails_ThrowsMaxioBillingExceptionWithStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.InternalServerError, """{"errors":"boom"}"""));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_WhenNotConfigured_ThrowsMaxioBillingException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var options = new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var service = new MaxioSubscriptionService(
            client,
            Options.Create(new MaxioSettings()),
            NullLogger<MaxioSubscriptionService>.Instance);

        await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.ListPlansAsync(CancellationToken.None));
    }
}
