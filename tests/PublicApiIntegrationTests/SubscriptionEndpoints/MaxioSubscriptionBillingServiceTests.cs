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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionBillingServiceTests
{
    private const string UserEmail = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";
    private const string FamilyJson =
        """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";
    private const string ProductJson =
        """{"product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":3023074,"handle":"eshop-subscribe"}}}""";
    private const string CustomerJson =
        """{"customer":{"id":555,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"eShopOnWeb Shopper"}}""";
    private const string SubscriptionJson =
        """{"subscription":{"id":9,"state":"active","product_price_in_cents":29900,"balance_in_cents":0,"current_period_ends_at":"2026-11-06T00:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro"},"customer":{"id":555}}}""";
    private const string AwaitingSignupSubscriptionJson =
        """{"subscription":{"id":9,"state":"awaiting_signup","product_price_in_cents":29900,"current_period_ends_at":"2026-11-06T00:00:00Z","product":{"id":7126957,"handle":"eshop-pro","name":"eShop Pro"},"customer":{"id":555}}}""";

    [TestMethod]
    public async Task ListPlans_MapsPlansFromConfiguredProductFamily()
    {
        var (service, _) = CreateService(new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products"))
            {
                return Json(HttpStatusCode.OK, "[" + ProductJson + "]");
            }
            return Json(HttpStatusCode.OK, FamilyJson);
        }));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(1, plans.Count);
        var plan = plans[0];
        Assert.AreEqual("eshop-pro", plan.Handle);
        Assert.AreEqual("eShop Pro", plan.Name);
        Assert.AreEqual(299m, plan.Price);
        Assert.AreEqual(29900, plan.PriceInCents);
        Assert.AreEqual(1, plan.Interval);
        Assert.AreEqual("month", plan.IntervalUnit);
        Assert.IsFalse(plan.PaymentMethodRequired);
    }

    [TestMethod]
    public async Task Subscribe_WhenSubscriptionAlreadyExists_ReturnsExistingWithoutCreating()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);

        var summary = await service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None);

        Assert.AreEqual(9, summary.SubscriptionId);
        Assert.AreEqual("active", summary.State);
        Assert.AreEqual(299m, summary.Price);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post), "no enrollment POST should be sent when the subscription already exists");
    }

    [TestMethod]
    public async Task Subscribe_CreatesSubscriptionWithIdempotentReference()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);

        var summary = await service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None);

        Assert.AreEqual(9, summary.SubscriptionId);
        Assert.AreEqual("active", summary.State);
        Assert.AreEqual(299m, summary.Price);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), summary.NextBillingDate);

        var body = handler.Bodies.Single(b => b.Contains("subscription"));
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_id\":555");
        StringAssert.Contains(body, $"\"reference\":\"eshop-sub:{UserEmail}:{PlanHandle}\"");
        StringAssert.Contains(body, "\"payment_collection_method\":\"automatic\"");
    }

    [TestMethod]
    public async Task Subscribe_ReusesExistingMaxioCustomerByReference()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);

        await service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None);

        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers")), "no customer POST should be sent when the customer reference already exists");
    }

    [TestMethod]
    public async Task Subscribe_ActivatesSubscriptionThatIsAwaitingSignup()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.RequestUri!.AbsolutePath.Contains("activate"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, AwaitingSignupSubscriptionJson);
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);

        var summary = await service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None);

        Assert.AreEqual("active", summary.State);
        Assert.IsTrue(handler.Requests.Any(r => r.RequestUri!.AbsolutePath.Contains("activate")), "the subscription in awaiting_signup must be activated");
    }

    [TestMethod]
    public async Task Subscribe_OnRejectedCreate_ReconcilesToSubscriptionCreatedByConcurrentCall()
    {
        var findCount = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                findCount++;
                return findCount == 1
                    ? Json(HttpStatusCode.NotFound, "")
                    : Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["reference has already been taken"]}""");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler);

        var summary = await service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None);

        Assert.AreEqual(9, summary.SubscriptionId);
        Assert.AreEqual("active", summary.State);
    }

    [TestMethod]
    public async Task Subscribe_WhenErrorBodyCannotBeRead_ReportsProviderRejectionNotOutage()
    {
        var findCount = 0;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("products") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }
            if (request.RequestUri!.AbsolutePath.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                findCount++;
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":{"base":["plan is not subscribable"]}}""");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var (service, _) = CreateService(handler, wrapStatusCaptureHandler: true);

        var exception = await Assert.ThrowsExceptionAsync<BillingException>(
            () => service.SubscribeAsync(UserEmail, PlanHandle, CancellationToken.None));

        Assert.AreEqual(422, exception.StatusCode);
    }

    [TestMethod]
    public async Task ListForUser_WhenCustomerDoesNotExist_ReturnsEmptyList()
    {
        var (service, _) = CreateService(new StubHandler(request =>
            Json(HttpStatusCode.NotFound, "")));

        var subscriptions = await service.ListForUserAsync(UserEmail, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task ListForUser_MapsCustomerSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]");
            }
            if (request.RequestUri!.Query.Contains("q="))
            {
                return Json(HttpStatusCode.OK, "[" + CustomerJson + "]");
            }
            return Json(HttpStatusCode.OK, CustomerJson);
        });
        var (service, _) = CreateService(handler);

        var subscriptions = await service.ListForUserAsync(UserEmail, CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual(9, subscriptions[0].SubscriptionId);
        Assert.AreEqual("eshop-pro", subscriptions[0].PlanHandle);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.AreEqual(299m, subscriptions[0].Price);
        Assert.AreEqual(1, handler.Requests.Count(r => r.RequestUri!.Query.Contains("q=")), "one email search page is expected");
    }

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        StubHandler handler, bool wrapStatusCaptureHandler = false)
    {
        var httpClient = wrapStatusCaptureHandler
            ? new HttpClient(new MaxioLastStatusCodeHandler { InnerHandler = handler })
            : new HttpClient(handler);
        httpClient.BaseAddress = new Uri("https://stub.local/");

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        clientOptions.Server.Production.Us.Site = "stub";

        var client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "stub",
            ProductFamilyHandle = "eshop-subscribe"
        };

        return (new MaxioSubscriptionBillingService(client, options, new TestLogger()), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responder(request);
        }
    }

    private sealed class TestLogger : IAppLogger<MaxioSubscriptionBillingService>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}