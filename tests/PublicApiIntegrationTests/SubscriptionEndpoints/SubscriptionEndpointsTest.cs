using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Polly.Timeout;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task RequireBearerAuthentication()
    {
        var handler = new MaxioStubHandler(new Func<HttpRequestMessage, HttpResponseMessage>(
            _ => throw new AssertFailedException("Maxio should not be called.")));
        await using var application = CreateApplication(handler);
        using var client = application.CreateClient();

        var plans = await client.GetAsync("api/subscription-plans");
        var subscribe = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "pro" });
        var mine = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ListsPlansFromConfiguredProductFamily()
    {
        var handler = new MaxioStubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/product_families.json" => Json(HttpStatusCode.OK,
                """[{"product_family":{"id":123,"name":"eShop","handle":"test-family"}}]"""),
            "/product_families/123/products.json" => Json(HttpStatusCode.OK,
                """[{"product":{"id":10,"name":"Pro","handle":"pro","description":"Pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","request_credit_card":true,"require_credit_card":false,"product_family":{"id":123,"handle":"test-family"}}}]"""),
            _ => throw new AssertFailedException($"Unexpected Maxio path: {request.RequestUri.AbsolutePath}")
        });
        await using var application = CreateApplication(handler);
        using var client = AuthenticatedClient(application);

        var response = await client.GetAsync("api/subscription-plans");
        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model.Plans.Count);
        Assert.AreEqual("pro", model.Plans[0].ProductHandle);
        Assert.AreEqual(29900L, model.Plans[0].PriceInCents);
        Assert.AreEqual("month", model.Plans[0].IntervalUnit);
        Assert.IsTrue(model.Plans[0].RequestsCreditCard);
        Assert.IsFalse(model.Plans[0].RequiresCreditCard);
        Assert.IsTrue(handler.Requests.Any(x => x.Query.Contains("include_archived=false", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ProviderTimeoutReturnsSanitizedServiceUnavailable()
    {
        var handler = new MaxioStubHandler(new Func<HttpRequestMessage, HttpResponseMessage>(
            _ => throw new TimeoutRejectedException("provider timeout details")));
        await using var application = CreateApplication(handler);
        using var client = AuthenticatedClient(application);

        var response = await client.GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(body, "Maxio is temporarily unavailable.");
        Assert.IsFalse(body.Contains(nameof(TimeoutRejectedException), StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("provider timeout details", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConcurrentSubscribeCreatesOneCustomerAndOneSubscription()
    {
        var provider = new StatefulSubscriptionProvider();
        var handler = new MaxioStubHandler(provider.RespondAsync);
        await using var application = CreateApplication(handler);
        using var firstClient = AuthenticatedClient(application);
        using var secondClient = AuthenticatedClient(application);

        var firstCall = firstClient.PostAsJsonAsync("api/subscriptions", new { productHandle = "pro" });
        var secondCall = secondClient.PostAsJsonAsync("api/subscriptions", new { productHandle = "pro" });
        var responses = await Task.WhenAll(firstCall, secondCall);
        var models = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<CreateSubscriptionResponse>()));

        Assert.IsTrue(responses.All(x => x.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, provider.CustomerCreateCount);
        Assert.AreEqual(1, provider.SubscriptionCreateCount);
        Assert.AreEqual(1, models.Count(x => x!.Created));
        Assert.AreEqual(1, models.Count(x => !x!.Created));
        Assert.IsTrue(models.All(x => x!.Subscription.ProductHandle == "pro"));
        Assert.IsTrue(models.All(x => x!.Subscription.NextBillingDate.HasValue));

        using var myClient = AuthenticatedClient(application);
        var myResponse = await myClient.GetAsync("api/my-subscriptions");
        var mine = await myResponse.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();
        Assert.AreEqual(HttpStatusCode.OK, myResponse.StatusCode);
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual("active", mine.Subscriptions[0].State);
    }

    [TestMethod]
    public async Task SubscriptionValidationDetailsStayOutOfTheHttpResponse()
    {
        const string providerDetail = "provider diagnostic detail";
        var provider = new StatefulSubscriptionProvider(providerDetail, "validation-plan");
        var handler = new MaxioStubHandler(provider.RespondAsync);
        await using var application = CreateApplication(handler);
        using var client = AuthenticatedClient(application);

        var response = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "validation-plan" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, "Maxio rejected the subscription request.");
        Assert.IsFalse(body.Contains(providerDetail, StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateApplication(HttpMessageHandler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    ["Maxio:ApiKey"] = "test-value-not-a-secret",
                    ["Maxio:Subdomain"] = "test-site",
                    ["Maxio:ProductFamilyHandle"] = "test-family"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(MaxioOptions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StatefulSubscriptionProvider
    {
        private readonly object _gate = new();
        private readonly string? _subscriptionValidationError;
        private readonly string _productHandle;
        private bool _customerExists;
        private bool _subscriptionExists;

        public StatefulSubscriptionProvider(
            string? subscriptionValidationError = null,
            string productHandle = "pro")
        {
            _subscriptionValidationError = subscriptionValidationError;
            _productHandle = productHandle;
        }

        public int CustomerCreateCount { get; private set; }
        public int SubscriptionCreateCount { get; private set; }

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.StartsWith("/products/handle/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, ProductJson);
            }

            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                lock (_gate)
                {
                    return _customerExists
                        ? Json(HttpStatusCode.OK, CustomerJson)
                        : Json(HttpStatusCode.NotFound, "{}");
                }
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                var body = await request.Content!.ReadAsStringAsync();
                StringAssert.Contains(body, "\"reference\"");
                lock (_gate)
                {
                    CustomerCreateCount++;
                    _customerExists = true;
                    return Json(HttpStatusCode.Created, CustomerJson);
                }
            }

            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                lock (_gate)
                {
                    return _subscriptionExists
                        ? Json(HttpStatusCode.OK, SubscriptionJson)
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                var body = await request.Content!.ReadAsStringAsync();
                StringAssert.Contains(body, $"\"product_handle\":\"{_productHandle}\"");
                StringAssert.Contains(body, "\"customer_reference\"");
                StringAssert.Contains(body, "\"reference\"");
                StringAssert.Contains(body, "\"payment_collection_method\":\"remittance\"");
                lock (_gate)
                {
                    SubscriptionCreateCount++;
                    if (_subscriptionValidationError is not null)
                    {
                        return Json(
                            HttpStatusCode.UnprocessableEntity,
                            $$"""{"errors":["{{_subscriptionValidationError}}"]}""");
                    }

                    _subscriptionExists = true;
                    return Json(HttpStatusCode.Created, SubscriptionJson);
                }
            }

            if (request.Method == HttpMethod.Get
                && path == "/customers/900/subscriptions.json")
            {
                return Json(HttpStatusCode.OK, $"[{SubscriptionJson}]");
            }

            throw new AssertFailedException($"Unexpected Maxio request: {request.Method} {request.RequestUri}");
        }

        private string ProductJson => ProductJsonTemplate.Replace(
            "\"pro\"",
            $"\"{_productHandle}\"",
            StringComparison.Ordinal);

        private const string ProductJsonTemplate =
            """{"product":{"id":10,"name":"Pro","handle":"pro","price_in_cents":29900,"interval":1,"interval_unit":"month","request_credit_card":true,"require_credit_card":false,"product_family":{"id":123,"handle":"test-family"}}}""";

        private const string CustomerJson =
            """{"customer":{"id":900,"first_name":"Demo","last_name":"Customer","email":"demouser@microsoft.com","reference":"eshop-u-test"}}""";

        private string SubscriptionJson => SubscriptionJsonTemplate.Replace(
            "\"pro\"",
            $"\"{_productHandle}\"",
            StringComparison.Ordinal);

        private const string SubscriptionJsonTemplate =
            """{"subscription":{"id":700,"state":"active","product_price_in_cents":29900,"current_billing_amount_in_cents":29900,"next_assessment_at":"2026-09-24T00:00:00Z","current_period_ends_at":"2026-09-24T00:00:00Z","currency":"USD","customer":{"id":900},"product":{"id":10,"name":"Pro","handle":"pro","product_family":{"id":123,"handle":"test-family"}}}}""";
    }

    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public MaxioStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this(request => Task.FromResult(responder(request)))
        {
        }

        public MaxioStubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public ConcurrentBag<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query));
            return await _responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string Query);
}
