using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioBillingServiceTest
{
    private static IMaxioBillingService NewService(MaxioStubHandler handler, ISubscriptionMappingStore? store = null) =>
        new MaxioBillingService(
            new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions()),
            store ?? new InMemorySubscriptionMappingStore(),
            Options.Create(new MaxioSettings
            {
                ApiKey = "test-only-dummy-api-key",
                Subdomain = "test-only-dummy-site",
                ProductFamilyHandle = "test-only-dummy-family",
            }),
            NullLogger<MaxioBillingService>.Instance);

    private static readonly BillingUser DemoUser = BillingUserFactory.FromIdentity("demouser@microsoft.com", "demouser@microsoft.com");

    [TestMethod]
    public async Task ListsPlansFromTheConfiguredFamily()
    {
        var stub = new MaxioStubHandler();
        var plans = await NewService(stub).ListPlansAsync();

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("$299.00", pro.PriceDisplay);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.IsTrue(stub.Requests.Any(r => r.RequestUri!.AbsolutePath == "/product_families.json"));
        Assert.IsTrue(stub.Requests.Any(r => r.RequestUri!.AbsolutePath == "/product_families/3023074/products.json"));
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAcrossDoubleClickAndRestart()
    {
        var stub = new MaxioStubHandler();
        var store = new InMemorySubscriptionMappingStore();
        var service = NewService(stub, store);

        var first = await service.SubscribeAsync(DemoUser, "eshop-pro");
        Assert.IsTrue(first.Created);
        Assert.AreEqual(90001, first.Subscription.Id);
        Assert.AreEqual("active", first.Subscription.State);
        Assert.AreEqual("Pro Plan", first.Subscription.PlanName);
        Assert.AreEqual(29900, first.Subscription.PriceInCents);
        Assert.IsNotNull(first.Subscription.NextBillingDate);
        Assert.AreEqual(1, stub.SubscriptionCreateCount);
        Assert.AreEqual(1, stub.CustomerCreateCount);

        var second = await service.SubscribeAsync(DemoUser, "eshop-pro");
        Assert.IsFalse(second.Created);
        Assert.AreEqual(90001, second.Subscription.Id);
        Assert.AreEqual(1, stub.SubscriptionCreateCount);
        Assert.AreEqual(1, stub.CustomerCreateCount);

        var afterRestart = await NewService(stub).SubscribeAsync(DemoUser, "eshop-pro");
        Assert.IsFalse(afterRestart.Created);
        Assert.AreEqual(90001, afterRestart.Subscription.Id);
        Assert.AreEqual(1, stub.SubscriptionCreateCount);

        var sentBody = stub.RequestBodies[HttpMethod.Post]["/subscriptions.json"];
        StringAssert.Contains(sentBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(sentBody, "\"customer_reference\":\"demouser@microsoft.com\"");
        StringAssert.Contains(sentBody, "\"reference\":\"demouser@microsoft.com:eshop-pro\"");
        StringAssert.Contains(sentBody, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task SubscribeRejectsUnknownPlans()
    {
        var service = NewService(new MaxioStubHandler());

        await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(DemoUser, "no-such-plan"));

        var invalid = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(DemoUser, "   "));
        Assert.AreEqual(400, invalid.StatusCode);
    }

    [TestMethod]
    public async Task ListsSubscriptionsForTheUsersMaxioCustomer()
    {
        var stub = new MaxioStubHandler();
        var service = NewService(stub);

        await service.SubscribeAsync(DemoUser, "eshop-pro");
        var mine = await service.ListForUserAsync(DemoUser);

        Assert.AreEqual(1, mine.Count);
        Assert.AreEqual(90001, mine[0].Id);
        Assert.AreEqual("eshop-pro", mine[0].PlanHandle);
        Assert.AreEqual("active", mine[0].State);
    }

    [TestMethod]
    public async Task ListsNothingForAUserWithoutACustomer()
    {
        var stub = new MaxioStubHandler();
        var mine = await NewService(stub).ListForUserAsync(DemoUser);

        Assert.AreEqual(0, mine.Count);
        Assert.IsFalse(stub.Requests.Any(r => r.Method == HttpMethod.Post));
    }
}

internal sealed class MaxioStubHandler : HttpMessageHandler
{
    private const int FamilyId = 3023074;
    private const int CustomerId = 77001;
    private const string FamilyHandle = "test-only-dummy-family";
    private const string UserReference = "demouser@microsoft.com";

    private bool _customerCreated;
    private bool _subscriptionCreated;

    public List<HttpRequestMessage> Requests { get; } = new();
    public Dictionary<HttpMethod, Dictionary<string, string>> RequestBodies { get; } = new();
    public int SubscriptionCreateCount { get; private set; }
    public int CustomerCreateCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            var bodies = RequestBodies.TryGetValue(request.Method, out var byPath)
                ? byPath
                : RequestBodies[request.Method] = new Dictionary<string, string>();
            bodies[request.RequestUri!.AbsolutePath] = await request.Content.ReadAsStringAsync(ct);
        }

        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method;

        if (method == HttpMethod.Get && path == "/product_families.json")
        {
            var body = "[{\"product_family\":{\"id\":" + FamilyId +
                       ",\"handle\":\"" + FamilyHandle + "\",\"name\":\"eShop Subscribe\"}}]";
            return Json(200, body);
        }

        if (method == HttpMethod.Get && path == $"/product_families/{FamilyId}/products.json")
        {
            return Json(200, """
                [
                  {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"test-only-dummy-family"}}},
                  {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"test-only-dummy-family"}}}
                ]
                """);
        }

        if (method == HttpMethod.Get && path == "/products/handle/eshop-pro.json")
        {
            return Json(200, """
                {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"test-only-dummy-family"}}}
                """);
        }

        if (method == HttpMethod.Get && path == "/products/handle/no-such-plan.json")
        {
            return Empty(HttpStatusCode.NotFound);
        }

        if (method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return _customerCreated ? Json(200, CustomerJson) : Empty(HttpStatusCode.NotFound);
        }

        if (method == HttpMethod.Post && path == "/customers.json")
        {
            _customerCreated = true;
            CustomerCreateCount++;
            return Json(201, CustomerJson);
        }

        if (method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            return _subscriptionCreated ? Json(200, SubscriptionJson) : Empty(HttpStatusCode.NotFound);
        }

        if (method == HttpMethod.Post && path == "/subscriptions.json")
        {
            _subscriptionCreated = true;
            SubscriptionCreateCount++;
            return Json(201, SubscriptionJson);
        }

        if (method == HttpMethod.Get && path == "/subscriptions/90001.json")
        {
            return Json(200, SubscriptionJson);
        }

        if (method == HttpMethod.Get && path == $"/customers/{CustomerId}/subscriptions.json")
        {
            return Json(200, $"[{SubscriptionJson}]");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"unstubbed request: {method} {path}", Encoding.UTF8, "text/plain")
        };
    }

    private const string CustomerJson = """
        {"customer":{"id":77001,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"Microsoft"}}
        """;

    private const string SubscriptionJson = """
        {"subscription":{"id":90001,"state":"active","reference":"demouser@microsoft.com:eshop-pro","product_price_in_cents":29900,"current_period_ends_at":"2026-10-08T12:00:00Z","created_at":"2026-09-08T12:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},"customer":{"id":77001,"reference":"demouser@microsoft.com"}}}
        """;

    private static HttpResponseMessage Json(int statusCode, string body) =>
        new((HttpStatusCode)statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Empty(HttpStatusCode statusCode) =>
        new(statusCode)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };
}
