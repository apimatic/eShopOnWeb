using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private const string UserReference = "demouser@microsoft.com";

    private static string _token;
    private MaxioStubHandler _handler;
    private WebApplicationFactory<Program> _factory;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        Environment.SetEnvironmentVariable("MAXIO_API_KEY", "test-api-key");
        Environment.SetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN", "test-site");
        Environment.SetEnvironmentVariable("MAXIO_ENVIRONMENT", "US");
        Environment.SetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY", "eshop-subscribe");
        _token = ApiTokenHelper.GetNormalUserToken();
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _handler = new MaxioStubHandler(UserReference);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHttpClient("Maxio").ConfigurePrimaryHttpMessageHandler(() => _handler)));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsSubscriptionPlans()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var model = body.FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Plans.Count);

        var pro = model.Plans.Find(plan => plan.Handle == "eshop-pro");
        Assert.IsNotNull(pro);
        Assert.AreEqual("Pro Plan", pro!.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("month", pro.Interval);

        var basic = model.Plans.Find(plan => plan.Handle == "basic-plan");
        Assert.IsNotNull(basic);
        Assert.AreEqual(29m, basic!.Price);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionAndIsIdempotent()
    {
        var client = CreateAuthenticatedClient();
        var jsonContent = JsonContent(new { planHandle = "eshop-pro" });

        var first = await client.PostAsync("api/subscriptions", jsonContent);

        first.EnsureSuccessStatusCode();
        var firstBody = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstBody);
        Assert.AreEqual("eshop-pro", firstBody!.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", firstBody.Subscription.PlanName);
        Assert.AreEqual(299m, firstBody.Subscription.Price);
        Assert.AreEqual("active", firstBody.Subscription.State);
        Assert.IsNotNull(firstBody.Subscription.NextBillingDate);

        var second = await client.PostAsync("api/subscriptions", JsonContent(new { planHandle = "eshop-pro" }));

        second.EnsureSuccessStatusCode();
        var secondBody = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(firstBody.Subscription.SubscriptionId, secondBody!.Subscription.SubscriptionId);
        Assert.AreEqual(1, _handler.CreateSubscriptionCalls);
        Assert.AreEqual(1, _handler.CreateCustomerCalls);
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsync("api/subscriptions", JsonContent(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsSubscriptionsForCallingUser()
    {
        var client = CreateAuthenticatedClient();

        await client.PostAsync("api/subscriptions", JsonContent(new { planHandle = "eshop-pro" }));

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.IsNotNull(body);
        Assert.AreEqual(1, body!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", body.Subscriptions[0].PlanHandle);
        Assert.AreEqual("Pro Plan", body.Subscriptions[0].PlanName);
        Assert.AreEqual("active", body.Subscriptions[0].State);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return client;
    }

    private static StringContent JsonContent(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }

    private sealed class MaxioStubHandler : HttpMessageHandler
    {
        private readonly string _userReference;
        private bool _subscriptionCreated;

        public MaxioStubHandler(string userReference)
        {
            _userReference = userReference;
        }

        public int CreateSubscriptionCalls { get; private set; }
        public int CreateCustomerCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var method = request.Method;

            if (method == HttpMethod.Get)
            {
                if (path == "product_families.json")
                {
                    return Ok(new object[]
                    {
                        new { product_family = new { id = 1, handle = "eshop-subscribe", name = "eShop Subscribe" } }
                    });
                }

                if (path == "product_families/1/products.json")
                {
                    return Ok(new object[]
                    {
                        new { product = Product("basic-plan", "Basic Plan", 2900) },
                        new { product = Product("eshop-pro", "Pro Plan", 29900) }
                    });
                }

                if (path.StartsWith("products/handle/", StringComparison.Ordinal))
                {
                    var handle = path.Substring("products/handle/".Length).Replace(".json", string.Empty);
                    if (handle != "eshop-pro" && handle != "basic-plan")
                    {
                        return NotFound();
                    }

                    var (name, price) = handle == "basic-plan" ? ("Basic Plan", 2900) : ("Pro Plan", 29900);
                    return Ok(new { product = Product(handle, name, price) });
                }

                if (path == "customers/lookup.json")
                {
                    return NotFound();
                }

                if (path == "subscriptions/lookup.json")
                {
                    return _subscriptionCreated ? Ok(SubscriptionEnvelope()) : NotFound();
                }

                if (path == $"customers/123/subscriptions.json")
                {
                    return Ok(_subscriptionCreated ? new object[] { SubscriptionEnvelope() } : new object[0]);
                }
            }

            if (method == HttpMethod.Post)
            {
                if (path == "customers.json")
                {
                    CreateCustomerCalls++;
                    return Ok(new
                    {
                        customer = new
                        {
                            id = 123,
                            reference = _userReference,
                            email = _userReference,
                            first_name = "demouser",
                            last_name = "Customer"
                        }
                    });
                }

                if (path == "subscriptions.json")
                {
                    CreateSubscriptionCalls++;
                    _subscriptionCreated = true;
                    return Ok(SubscriptionEnvelope());
                }
            }

            return NotFound();
        }

        private object Product(string handle, string name, int priceInCents)
        {
            return new
            {
                id = handle == "eshop-pro" ? 11 : 10,
                handle,
                name,
                price_in_cents = priceInCents,
                interval = 1,
                interval_unit = "month",
                product_family = new { handle = "eshop-subscribe" }
            };
        }

        private object SubscriptionEnvelope()
        {
            return new
            {
                subscription = new
                {
                    id = 500,
                    reference = $"eshop-sub:{_userReference}:eshop-pro",
                    state = "active",
                    product_price_in_cents = 29900,
                    current_period_ends_at = "2026-10-09T00:00:00Z",
                    next_assessment_at = "2026-10-09T00:00:00Z",
                    created_at = "2026-09-09T00:00:00Z",
                    product = Product("eshop-pro", "Pro Plan", 29900)
                }
            };
        }

        private static Task<HttpResponseMessage> Ok(object body)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }

        private static Task<HttpResponseMessage> NotFound()
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }
}
