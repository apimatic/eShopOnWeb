using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        // Bodies are captured at send time — the SDK disposes request content after sending.
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (WebApplicationFactory<Program> Factory, StubMaxioHandler Handler) CreateFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubMaxioHandler(responder);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHttpClient("Maxio")
                    .ConfigurePrimaryHttpMessageHandler(() => handler)));
        return (factory, handler);
    }

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsync("api/subscriptions",
                new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsMappedPlans()
    {
        var (factory, _) = CreateFactory(_ => Json(HttpStatusCode.OK,
            """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""));
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.GetAsync("api/subscription-plans");

            response.EnsureSuccessStatusCode();
            var model = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            Assert.AreEqual(1, model!.Plans.Count);
            Assert.AreEqual("eshop-pro", model.Plans[0].Handle);
            Assert.AreEqual("Pro Plan", model.Plans[0].Name);
            Assert.AreEqual(299.00m, model.Plans[0].Price);
            Assert.AreEqual(1, model.Plans[0].Interval);
            Assert.AreEqual("month", model.Plans[0].IntervalUnit);
        }
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenCustomerMissing()
    {
        var (factory, handler) = CreateFactory(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, "[]");
            }
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created,
                    """{"subscription":{"id":555,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-09-27T00:00:00Z","next_assessment_at":"2026-09-27T00:00:00Z"}}""");
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.NotFound, """{"errors":"not found"}""");
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created,
                    """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.PostAsync("api/subscriptions",
                new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
            var model = JsonSerializer.Deserialize<CreateSubscriptionResponse>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            Assert.AreEqual(555, model!.Subscription.SubscriptionId);
            Assert.AreEqual("active", model.Subscription.State);
            Assert.AreEqual("eshop-pro", model.Subscription.ProductHandle);
            Assert.AreEqual(299.00m, model.Subscription.Price);
            Assert.AreEqual(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), model.Subscription.NextBillingDate);

            var createSubscription = handler.Requests.Single(r =>
                r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath.Contains("subscriptions"));
            StringAssert.Contains(createSubscription.Body, "eshop-pro");
            StringAssert.Contains(createSubscription.Body, "123");
        }
    }

    [TestMethod]
    public async Task SubscribeReturnsExistingSubscriptionWhenAlreadySubscribed()
    {
        var (factory, handler) = CreateFactory(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK,
                    """[{"subscription":{"id":777,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-09-27T00:00:00Z"}}]""");
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.PostAsync("api/subscriptions",
                new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json"));

            response.EnsureSuccessStatusCode();
            var model = JsonSerializer.Deserialize<CreateSubscriptionResponse>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            Assert.AreEqual(777, model!.Subscription.SubscriptionId);
            Assert.AreEqual("active", model.Subscription.State);

            // Idempotency: no new subscription (and no new customer) was created.
            Assert.IsFalse(handler.Requests.Any(r => r.Request.Method == HttpMethod.Post));
        }
    }

    [TestMethod]
    public async Task SubscribeSurfacesProviderRejection()
    {
        var (factory, _) = CreateFactory(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, "[]");
            }
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Payment method required"]}""");
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.PostAsync("api/subscriptions",
                new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json"));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Payment method required");
        }
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsEmptyWhenUserHasNoCustomer()
    {
        var (factory, _) = CreateFactory(_ => Json(HttpStatusCode.NotFound, """{"errors":"not found"}"""));
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.GetAsync("api/my-subscriptions");

            response.EnsureSuccessStatusCode();
            var model = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            Assert.AreEqual(0, model!.Subscriptions.Count);
        }
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsMappedSubscriptions()
    {
        var (factory, _) = CreateFactory(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("subscriptions") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK,
                    """[{"subscription":{"id":777,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"current_period_ends_at":"2026-09-27T00:00:00Z","next_assessment_at":"2026-09-27T00:00:00Z"}}]""");
            }
            if (path.Contains("customers") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        using (factory)
        {
            var client = AuthorizedClient(factory);

            var response = await client.GetAsync("api/my-subscriptions");

            response.EnsureSuccessStatusCode();
            var model = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(
                await response.Content.ReadAsStringAsync(), JsonOptions);
            Assert.AreEqual(1, model!.Subscriptions.Count);
            Assert.AreEqual(777, model.Subscriptions[0].SubscriptionId);
            Assert.AreEqual("active", model.Subscriptions[0].State);
            Assert.AreEqual("eshop-pro", model.Subscriptions[0].ProductHandle);
            Assert.AreEqual(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), model.Subscriptions[0].NextBillingDate);
        }
    }
}
