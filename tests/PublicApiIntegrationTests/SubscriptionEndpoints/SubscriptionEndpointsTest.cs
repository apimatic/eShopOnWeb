using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscription endpoints end to end over HTTP. Maxio itself is replaced by a stub
/// that answers with the payload shapes of the OpenAPI specification, so the test covers routing,
/// authentication, the billing service and the API client without touching a live billing site.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private const string PlansPath = "/api/subscription-plans";
    private const string SubscribePath = "/api/subscriptions";
    private const string MySubscriptionsPath = "/api/my-subscriptions";

    [DataTestMethod]
    [DataRow(PlansPath)]
    [DataRow(MySubscriptionsPath)]
    public async Task ReturnsUnauthorizedForReadsWithoutAToken(string path)
    {
        using var factory = new StubbedBillingApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedForSubscribeWithoutAToken()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsThePlansOfTheConfiguredProductFamily()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(PlansPath);
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Plans.Count);
        // Archived plans are not on offer, and plans come back cheapest first.
        Assert.AreEqual("basic-plan", model.Plans[0].Handle);
        Assert.AreEqual("eshop-pro", model.Plans[1].Handle);
        Assert.AreEqual(299m, model.Plans[1].Price);
        Assert.AreEqual("USD", model.Plans[1].Currency);
        Assert.AreEqual("every month", model.Plans[1].BillingPeriod);

        Assert.IsTrue(factory.Stub.Requests.Any(request =>
            request.Contains("/product_families/handle%3Ademo-subscriptions/products.json", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SubscribeCreatesTheCustomerAndTheSubscriptionOnFirstCall()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Created);
        Assert.IsNotNull(model.Subscription);
        Assert.AreEqual("active", model.Subscription!.State);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.IsNotNull(model.Subscription.NextBillingAt);
        Assert.AreEqual("eshoponweb:demouser@microsoft.com", model.Subscription.CustomerReference);

        Assert.AreEqual(1, factory.Stub.CreatedCustomers);
        Assert.AreEqual(1, factory.Stub.CreatedSubscriptions);
    }

    [TestMethod]
    public async Task SubscribingTwiceDoesNotEnrollTheShopperTwice()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var first = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));
        var second = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.IsTrue(firstModel!.Created);
        Assert.IsFalse(secondModel!.Created);
        Assert.AreEqual(firstModel.Subscription!.Id, secondModel.Subscription!.Id);

        Assert.AreEqual(1, factory.Stub.CreatedCustomers);
        Assert.AreEqual(1, factory.Stub.CreatedSubscriptions);
    }

    [TestMethod]
    public async Task ConcurrentSubscribesProduceASingleSubscription()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"))));

        Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(4, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, factory.Stub.CreatedSubscriptions);
        Assert.AreEqual(1, factory.Stub.CreatedCustomers);
    }

    [TestMethod]
    public async Task ReportsTheSubscriptionsOfTheSignedInShopper()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var empty = (await (await client.GetAsync(MySubscriptionsPath)).Content.ReadAsStringAsync())
            .FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(0, empty!.Subscriptions.Count);

        await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));

        var response = await client.GetAsync(MySubscriptionsPath);
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
        Assert.IsTrue(model.Subscriptions[0].IsActive);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForAPlanThatIsNotOnOffer()
    {
        using var factory = new StubbedBillingApiFactory();
        var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""not-a-plan""}"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, factory.Stub.CreatedSubscriptions);
    }

    [TestMethod]
    public async Task PassesBillingRejectionsBackAsUnprocessableEntity()
    {
        using var factory = new StubbedBillingApiFactory();
        factory.Stub.RejectSubscriptionWith = @"{""errors"":[""No payment method was on file for the $299.00 balance""]}";
        var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsync(SubscribePath, JsonBody(@"{""planHandle"":""eshop-pro""}"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "No payment method was on file");
    }

    [TestMethod]
    public async Task ReportsServiceUnavailableWhenBillingIsNotConfigured()
    {
        using var factory = new StubbedBillingApiFactory(configureBilling: false);
        var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync(PlansPath);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Boots PublicApi with the Maxio base address pointed at an in-memory stub of the API.
    /// </summary>
    private sealed class StubbedBillingApiFactory : WebApplicationFactory<Program>
    {
        private readonly bool _configureBilling;

        public StubbedBillingApiFactory(bool configureBilling = true) => _configureBilling = configureBilling;

        public MaxioStubHandler Stub { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true",
                    // Deliberately fake values: the stub never checks them, and no real credential
                    // may live in the repository.
                    ["Maxio:ApiKey"] = _configureBilling ? "test-api-key" : string.Empty,
                    ["Maxio:Subdomain"] = _configureBilling ? "test-site" : string.Empty,
                    ["Maxio:ProductFamilyHandle"] = _configureBilling ? "demo-subscriptions" : string.Empty,
                    ["Maxio:BaseUrl"] = _configureBilling ? "https://maxio.stub" : string.Empty,
                    ["Maxio:PlanCacheSeconds"] = "0"
                };

                configuration.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IMaxioApiClient, MaxioApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => Stub);
            });
        }
    }

    /// <summary>
    /// A minimal Maxio site: it answers the handful of operations the integration uses with the
    /// payload shapes the specification defines, and remembers what was created.
    /// </summary>
    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly List<string> _subscriptions = new();
        private int _customerId;

        public List<string> Requests { get; } = new();

        public int CreatedCustomers { get; private set; }

        public int CreatedSubscriptions { get; private set; }

        /// <summary>When set, subscription creation answers 422 with this body.</summary>
        public string? RejectSubscriptionWith { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_sync)
            {
                Requests.Add(uri.PathAndQuery);

                if (path == "/site.json")
                {
                    return Json(HttpStatusCode.OK, @"{""site"":{""id"":1,""subdomain"":""test-site"",""currency"":""USD""}}");
                }

                if (path.StartsWith("/product_families/", StringComparison.Ordinal) && path.EndsWith("/products.json", StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.OK, """
                        [
                          {"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
                            "interval":1,"interval_unit":"month","require_credit_card":false,
                            "product_family":{"id":9,"handle":"demo-subscriptions"}}},
                          {"product":{"id":2,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,
                            "interval":1,"interval_unit":"month","require_credit_card":false,
                            "product_family":{"id":9,"handle":"demo-subscriptions"}}},
                          {"product":{"id":3,"handle":"legacy-plan","name":"Legacy Plan","price_in_cents":100,
                            "interval":1,"interval_unit":"month","archived_at":"2026-01-01T00:00:00-05:00",
                            "product_family":{"id":9,"handle":"demo-subscriptions"}}}
                        ]
                        """);
                }

                if (path == "/customers/lookup.json")
                {
                    return _customerId == 0
                        ? Json(HttpStatusCode.NotFound, string.Empty)
                        : Json(HttpStatusCode.OK, Customer(_customerId));
                }

                if (path == "/customers.json" && request.Method == HttpMethod.Post)
                {
                    CreatedCustomers++;
                    _customerId = 4242;
                    return Json(HttpStatusCode.Created, Customer(_customerId));
                }

                if (path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.OK, "[" + string.Join(",", _subscriptions) + "]");
                }

                if (path == "/subscriptions/lookup.json")
                {
                    return Json(HttpStatusCode.NotFound, string.Empty);
                }

                if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
                {
                    if (RejectSubscriptionWith is not null)
                    {
                        return Json(HttpStatusCode.UnprocessableEntity, RejectSubscriptionWith);
                    }

                    CreatedSubscriptions++;
                    var subscription = Subscription(90000 + CreatedSubscriptions, body.Contains("basic-plan", StringComparison.Ordinal) ? "basic-plan" : "eshop-pro");
                    _subscriptions.Add(@"{""subscription"":" + subscription + "}");
                    return Json(HttpStatusCode.Created, @"{""subscription"":" + subscription + "}");
                }

                return Json(HttpStatusCode.NotFound, string.Empty);
            }
        }

        private static string Customer(int id) =>
            $@"{{""customer"":{{""id"":{id},""first_name"":""Demouser"",""last_name"":""Shopper"",
               ""email"":""demouser@microsoft.com"",""reference"":""eshoponweb:demouser@microsoft.com""}}}}";

        private static string Subscription(int id, string planHandle)
        {
            var priceInCents = planHandle == "basic-plan" ? 2900 : 29900;
            var name = planHandle == "basic-plan" ? "Basic Plan" : "Pro Plan";

            return $@"{{""id"":{id},""state"":""active"",""reference"":""eshoponweb:demouser@microsoft.com:{planHandle}"",
                ""product_price_in_cents"":{priceInCents},""balance_in_cents"":{priceInCents},""currency"":""USD"",
                ""payment_collection_method"":""remittance"",
                ""current_period_started_at"":""2026-09-06T00:00:00-05:00"",
                ""current_period_ends_at"":""2026-10-06T00:00:00-05:00"",
                ""next_assessment_at"":""2026-10-06T00:00:00-05:00"",
                ""activated_at"":""2026-09-06T00:00:00-05:00"",""created_at"":""2026-09-06T00:00:00-05:00"",
                ""product"":{{""id"":1,""handle"":""{planHandle}"",""name"":""{name}"",""price_in_cents"":{priceInCents},
                  ""interval"":1,""interval_unit"":""month""}},
                ""customer"":{{""id"":4242,""email"":""demouser@microsoft.com"",
                  ""reference"":""eshoponweb:demouser@microsoft.com""}}}}";
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
