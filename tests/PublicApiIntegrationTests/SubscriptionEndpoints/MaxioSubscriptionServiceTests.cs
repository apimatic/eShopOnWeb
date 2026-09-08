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
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string Reference = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string ProPlanHandle = "eshop-pro";
    private const string ProductFamilyHandle = "eshop-subscribe";

    private static readonly DateTimeOffset OctoberFirst =
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private const string CustomerJson =
        "{\"customer\":{\"id\":55,\"reference\":\"" + Reference + "\",\"email\":\"demouser@microsoft.com\"," +
        "\"first_name\":\"Demouser\",\"last_name\":\"Demouser\"}}";

    private const string SubscriptionJson =
        "{\"subscription\":{\"id\":900,\"state\":\"active\"," +
        "\"product\":{\"id\":7126957,\"handle\":\"eshop-pro\",\"name\":\"Pro\"}," +
        "\"product_price_in_cents\":29900," +
        "\"current_period_started_at\":\"2026-09-01T00:00:00Z\"," +
        "\"current_period_ends_at\":\"2026-10-01T00:00:00Z\"," +
        "\"next_assessment_at\":\"2026-10-01T00:00:00Z\"," +
        "\"created_at\":\"2026-09-01T00:00:00Z\"}}";

    private static readonly MaxioShopper Shopper =
        new MaxioShopper(Reference, "demouser@microsoft.com", "Demouser", "Demouser");

    private static readonly MaxioOptions MaxioSettings = new MaxioOptions
    {
        ApiKey = "unit-test-key",
        Subdomain = "cp-exp-1",
        ProductFamilyHandle = ProductFamilyHandle
    };

    [TestMethod]
    public async Task GetPlansAsync_ReturnsActivePlansFromConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/product_families.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"eShop Subscribe\"}}]");
            }

            if (path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK,
                    "[" +
                    "{\"product\":{\"id\":7126957,\"handle\":\"eshop-pro\",\"name\":\"Pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":null}}," +
                    "{\"product\":{\"id\":7126958,\"handle\":\"basic-plan\",\"name\":\"Basic\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":null}}," +
                    "{\"product\":{\"id\":9,\"handle\":\"retired-plan\",\"name\":\"Retired\",\"price_in_cents\":100,\"interval\":1,\"interval_unit\":\"month\",\"archived_at\":\"2020-01-01T00:00:00Z\"}}" +
                    "]");
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(plan => plan.Handle == ProPlanHandle);
        Assert.AreEqual("Pro", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.AreEqual(7126957, pro.Id);
        Assert.IsTrue(plans.All(plan => plan.Handle != "retired-plan"));
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return EmptyResponse(HttpStatusCode.NotFound);
            }

            if (IsPost(request) && path.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.Created, CustomerJson);
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            if (IsPost(request) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.Created, SubscriptionJson);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, ProPlanHandle, CancellationToken.None);

        Assert.IsTrue(result.CreatedNew);
        Assert.AreEqual(900, result.Subscription.Id);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(ProPlanHandle, result.Subscription.PlanHandle);
        Assert.AreEqual("Pro", result.Subscription.PlanName);
        Assert.AreEqual(299.00m, result.Subscription.Price);
        Assert.AreEqual(OctoberFirst, result.Subscription.NextBillingDate);

        var createCustomerIndex = handler.Requests.FindIndex(request =>
            IsPost(request) && request.RequestUri!.AbsolutePath.EndsWith("/customers.json", StringComparison.Ordinal));
        Assert.IsTrue(createCustomerIndex >= 0);
        Assert.IsTrue(handler.RequestBodies[createCustomerIndex]!
            .Contains("\"reference\":\"" + Reference + "\"", StringComparison.Ordinal));

        var createSubscriptionIndex = handler.Requests.FindIndex(request =>
            IsPost(request) && request.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal));
        Assert.IsTrue(createSubscriptionIndex >= 0);
        var subscriptionBody = handler.RequestBodies[createSubscriptionIndex]!;
        Assert.IsTrue(subscriptionBody.Contains("\"product_handle\":\"eshop-pro\"", StringComparison.Ordinal));
        Assert.IsTrue(subscriptionBody.Contains("\"customer_reference\":\"" + Reference + "\"", StringComparison.Ordinal));
        Assert.IsFalse(subscriptionBody.Contains("\"payment_profile", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenAlreadyOnPlan()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, CustomerJson);
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[" + SubscriptionJson + "]");
            }

            if (IsGet(request) && path.EndsWith("/subscriptions/900.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, SubscriptionJson);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, ProPlanHandle, CancellationToken.None);

        Assert.IsFalse(result.CreatedNew);
        Assert.AreEqual(900, result.Subscription.Id);
        Assert.IsFalse(handler.Requests.Any(request =>
            IsPost(request) && request.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SubscribeAsync_AdoptsConcurrentCustomer_WhenCreateReturnsConflict()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, CustomerJson);
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            if (IsPost(request) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.Created, SubscriptionJson);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, ProPlanHandle, CancellationToken.None);

        Assert.IsTrue(result.CreatedNew);
        Assert.AreEqual(900, result.Subscription.Id);
    }

    [TestMethod]
    public async Task SubscribeAsync_AdoptsExistingCustomer_WhenDuplicateCustomerCreationRejected()
    {
        var customerCreated = false;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return customerCreated
                    ? JsonResponse(HttpStatusCode.OK, CustomerJson)
                    : EmptyResponse(HttpStatusCode.NotFound);
            }

            if (IsPost(request) && path.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                customerCreated = true;
                return JsonResponse(HttpStatusCode.UnprocessableEntity, "{\"errors\":{}}");
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            if (IsPost(request) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.Created, SubscriptionJson);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(Shopper, ProPlanHandle, CancellationToken.None);

        Assert.IsTrue(result.CreatedNew);
        Assert.AreEqual(900, result.Subscription.Id);
        Assert.AreEqual(2, handler.Requests.Count(request =>
            IsGet(request) && request.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SubscribeAsync_SurfacesValidationRejection_WhenSubscriptionRejected()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, CustomerJson);
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            if (IsPost(request) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Product handle is not valid\"]}");
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var exception = await Assert.ThrowsExceptionAsync<MaxioProviderException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.IsTrue(exception.Message.Contains("Product handle is not valid", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenShopperHasNoCustomer()
    {
        var handler = new StubHandler(request =>
        {
            if (IsGet(request) && request.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return EmptyResponse(HttpStatusCode.NotFound);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var subscriptions = await service.GetSubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task GetSubscriptionsAsync_ReturnsShopperSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (IsGet(request) && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, CustomerJson);
            }

            if (IsGet(request) && path.Contains("/customers/", StringComparison.Ordinal) &&
                path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "[" + SubscriptionJson + "]");
            }

            if (IsGet(request) && path.EndsWith("/subscriptions/900.json", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, SubscriptionJson);
            }

            return JsonResponse(HttpStatusCode.InternalServerError, "{}");
        });

        var service = CreateService(handler);
        var subscriptions = await service.GetSubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.IsNotNull(subscriptions);
        var subscription = subscriptions.Single();
        Assert.AreEqual(900, subscription.Id);
        Assert.AreEqual(ProPlanHandle, subscription.PlanHandle);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(299.00m, subscription.Price);
        Assert.AreEqual(OctoberFirst, subscription.NextBillingDate);
    }

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = MaxioSettings.ApiKey, Password = "x" }
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        return new MaxioSubscriptionService(client, Options.Create(MaxioSettings));
    }

    private static bool IsGet(HttpRequestMessage request) => request.Method == HttpMethod.Get;

    private static bool IsPost(HttpRequestMessage request) => request.Method == HttpMethod.Post;

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage EmptyResponse(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        public List<string?> RequestBodies { get; } = new List<string?>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }
}
