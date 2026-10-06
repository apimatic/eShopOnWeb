using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string FamiliesJson = """
        [{"product_family": {"id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscriptions"}}]
        """;

    private const string ProProductJson = """
        {"product": {"id": 7126957, "handle": "eshop-pro", "name": "eShop Pro", "description": "Pro plan",
                     "price_in_cents": 29900, "interval": 1, "interval_unit": "month",
                     "require_credit_card": false, "product_family": {"id": 3023074, "handle": "eshop-subscribe"}}}
        """;

    private const string CustomerJson = """
        {
          "customer": {
            "id": 987654,
            "reference": "user-1",
            "email": "demouser@microsoft.com",
            "first_name": "Demouser",
            "last_name": "Shopper",
            "created_at": "2026-10-06T00:00:00Z"
          }
        }
        """;

    private const string SubscriptionJson = """
        {
          "subscription": {
            "id": 1234567,
            "state": "active",
            "balance_in_cents": 0,
            "currency": "USD",
            "reference": "user-1:eshop-pro",
            "product_price_in_cents": 29900,
            "next_assessment_at": "2026-11-06T00:00:00Z",
            "current_period_ends_at": "2026-11-06T00:00:00Z",
            "payment_collection_method": "automatic",
            "customer": { "id": 987654, "reference": "user-1" },
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "eShop Pro", "price_in_cents": 29900,
                         "product_family": { "id": 3023074, "handle": "eshop-subscribe" } }
          }
        }
        """;

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products/")) return Json(OK, ProProductJson);
            if (IsCustomerLookup(request)) return NotFound();
            if (request.Method == POST && path.Contains("/customers")) return Json(OK, CustomerJson);
            if (IsSubscriptionLookup(request)) return NotFound();
            if (request.Method == POST && path.Contains("/subscriptions")) return Json(OK, SubscriptionJson);
            return NotFound();
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1234567L, result.Subscription.Id);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(29900L, result.Subscription.PriceInCents);
        Assert.AreEqual("USD", result.Subscription.Currency);
        Assert.AreEqual("automatic", result.Subscription.CollectionMethod);
        Assert.IsNotNull(result.Subscription.NextBillingDate);
        Assert.AreEqual(1, CountRequests(handler, POST, "/customers"));
        Assert.AreEqual(1, CountRequests(handler, POST, "/subscriptions"));
    }

    [TestMethod]
    public async Task SubscribeAsync_DoubleClick_ReusesExistingSubscription()
    {
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products/")) return Json(OK, ProProductJson);
            if (IsCustomerLookup(request)) return Json(OK, CustomerJson);
            if (IsSubscriptionLookup(request)) return Json(OK, SubscriptionJson);
            return NotFound();
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.Created);
        Assert.AreEqual(1234567L, result.Subscription.Id);
        Assert.AreEqual(0, CountRequests(handler, POST, "/customers"));
        Assert.AreEqual(0, CountRequests(handler, POST, "/subscriptions"));
    }

    [TestMethod]
    public async Task SubscribeAsync_SecondPlan_ReusesSameCustomer()
    {
        var customerCreated = false;
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products/"))
            {
                var handle = path[(path.LastIndexOf('/') + 1)..].Split('?')[0];
                return Json(OK, string.Format(
                    "{{\"product\": {{\"id\": 7126957, \"handle\": \"{0}\", \"name\": \"{0}\", " +
                    "\"product_family\": {{\"id\": 3023074, \"handle\": \"eshop-subscribe\"}}}}}}", handle));
            }
            if (IsCustomerLookup(request)) return customerCreated ? Json(OK, CustomerJson) : NotFound();
            if (request.Method == POST && path.Contains("/customers"))
            {
                customerCreated = true;
                return Json(OK, CustomerJson);
            }
            if (IsSubscriptionLookup(request)) return NotFound();
            if (request.Method == POST && path.Contains("/subscriptions")) return Json(OK, SubscriptionJson);
            return NotFound();
        });
        var service = BuildService(handler);

        await service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);
        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "basic-plan", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, CountRequests(handler, POST, "/customers"));
    }

    [TestMethod]
    public async Task SubscribeAsync_TransportFailure_ReconcilesExistingSubscription()
    {
        var findCalls = 0;
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products/")) return Json(OK, ProProductJson);
            if (IsCustomerLookup(request)) return Json(OK, CustomerJson);
            if (IsSubscriptionLookup(request))
            {
                findCalls++;
                return findCalls == 1 ? NotFound() : Json(OK, SubscriptionJson);
            }
            if (request.Method == POST && path.Contains("/subscriptions"))
                throw new HttpRequestException("connection reset");
            return NotFound();
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1234567L, result.Subscription.Id);
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenMaxioRejects_Surfaces422WithMessages()
    {
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products/")) return Json(OK, ProProductJson);
            if (IsCustomerLookup(request)) return Json(OK, CustomerJson);
            if (IsSubscriptionLookup(request)) return NotFound();
            if (request.Method == POST && path.Contains("/subscriptions"))
                return Json(UNPROCESSABLE, """{"errors": ["Product handle: can't be blank"]}""");
            return NotFound();
        });
        var service = BuildService(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(
            () => service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None));

        Assert.AreEqual(422, exception.StatusCode);
        StringAssert.Contains(exception.Message, "Product handle");
    }

    [TestMethod]
    public async Task SubscribeAsync_UnknownPlan_Throws404()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            return NotFound();
        });
        var service = BuildService(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(
            () => service.SubscribeAsync("user-1", "demouser@microsoft.com", "no-such-plan", CancellationToken.None));

        Assert.AreEqual(404, exception.StatusCode);
    }

    [TestMethod]
    public async Task GetSubscriptionsForUserAsync_WhenNoCustomer_ReturnsEmptyWithoutCreating()
    {
        var handler = new StubMaxioHandler(request =>
        {
            if (IsCustomerLookup(request)) return NotFound();
            return NotFound();
        });
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsForUserAsync("user-1", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(0, CountRequests(handler, POST, "/customers"));
    }

    [TestMethod]
    public async Task GetPlanCatalogAsync_MapsPlansAndComponents()
    {
        var handler = new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (IsFamilies(request)) return Json(OK, FamiliesJson);
            if (request.Method == GET && path.Contains("/products"))
                return Json(OK, """
                    [{"product": {"id": 7126957, "handle": "eshop-pro", "name": "eShop Pro", "description": "Pro plan",
                                  "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false,
                                  "product_family": {"id": 3023074, "handle": "eshop-subscribe"}}},
                     {"product": {"id": 7126958, "handle": "basic-plan", "name": "Basic Plan", "description": "Basic plan",
                                  "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false,
                                  "product_family": {"id": 3023074, "handle": "eshop-subscribe"}}}]
                    """);
            if (request.Method == GET && path.Contains("/components"))
                return Json(OK, """
                    [{"component": {"id": 3057195, "handle": "api-call", "name": "API Call", "kind": "metered_component",
                                    "unit_name": "call", "price_per_unit_in_cents": 1}}]
                    """);
            return NotFound();
        });
        var service = BuildService(handler);

        var catalog = await service.GetPlanCatalogAsync(CancellationToken.None);

        Assert.AreEqual("eshop-subscribe", catalog.ProductFamilyHandle);
        Assert.AreEqual(2, catalog.Plans.Count);
        Assert.AreEqual("eshop-pro", catalog.Plans[0].Handle);
        Assert.AreEqual(29900L, catalog.Plans[0].PriceInCents);
        Assert.AreEqual(1, catalog.Components.Count);
        Assert.AreEqual("api-call", catalog.Components[0].Handle);
        Assert.AreEqual("metered_component", catalog.Components[0].Kind);
    }

    private static readonly HttpMethod GET = HttpMethod.Get;
    private static readonly HttpMethod POST = HttpMethod.Post;
    private const HttpStatusCode OK = HttpStatusCode.OK;
    private const HttpStatusCode UNPROCESSABLE = HttpStatusCode.UnprocessableEntity;

    private static bool IsFamilies(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        return request.Method == GET && path.Contains("/product_families")
            && !path.Contains("/products") && !path.Contains("/components");
    }

    private static bool IsCustomerLookup(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        return request.Method == GET && path.Contains("/customers") && path.Contains("reference=");
    }

    private static bool IsSubscriptionLookup(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        return request.Method == GET && path.Contains("/subscriptions") && path.Contains("reference=");
    }

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    [TestMethod]
    public async Task GetPlanCatalogAsync_BaseUrlOverride_IsUsedVerbatim()
    {
        var handler = new StubMaxioHandler(request => IsFamilies(request) ? Json(OK, FamiliesJson) : NotFound());
        var service = BuildService(handler, Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "http://maxio-override.example.com"
        }));

        await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(
            () => service.GetPlanCatalogAsync(CancellationToken.None));

        Assert.AreEqual("maxio-override.example.com", handler.Requests[0].RequestUri!.Host);
    }

    private static MaxioSubscriptionService BuildService(StubMaxioHandler handler)
    {
        return BuildService(handler, Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        }));
    }

    private static MaxioSubscriptionService BuildService(StubMaxioHandler handler, IOptions<MaxioOptions> options)
    {
        var factory = new StubHttpClientFactory(handler);
        var context = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        IRepository<MaxioSubscription> repository = new EfRepository<MaxioSubscription>(context);
        return new MaxioSubscriptionService(factory, options, repository, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static int CountRequests(StubMaxioHandler handler, HttpMethod method, string pathPart) =>
        handler.Requests.Count(r => r.Method == method && r.RequestUri!.PathAndQuery.Contains(pathPart));

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = _responder(request);
            if (response.Content is null)
            {
                response.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }
}