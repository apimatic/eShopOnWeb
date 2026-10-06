using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private WebApplicationFactory<Program> _factory = null!;
    private InMemorySubscriptionBillingClient _billing = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Initialize()
    {
        _billing = new InMemorySubscriptionBillingClient();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingClient>();
                services.AddSingleton<ISubscriptionBillingClient>(_billing);
            });
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [TestMethod]
    public async Task ReturnsPlans_GivenAuthenticatedRequest()
    {
        var response = await _client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(2, model!.Plans.Count(p => !p.PaymentMethodRequired));
        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("299.00 / month", pro.PriceDisplay);
    }

    [TestMethod]
    public async Task CreatesCustomerAndSubscription_GivenFirstSubscribe()
    {
        var response = await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("api/my-subscriptions", response.Headers.Location!.ToString());

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsTrue(model!.WasNewlyCreated);
        Assert.AreEqual("active", model.Subscription!.State);
        Assert.AreEqual("Pro Plan", model.Subscription.PlanName);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual("remittance", model.Subscription.PaymentCollectionMethod);
        Assert.IsNotNull(model.Subscription.NextBillingDate);

        Assert.AreEqual(1, _billing.CustomerCount);
        Assert.AreEqual(1, _billing.Subscriptions.Count);
        Assert.AreEqual("remittance", _billing.CreationAttempts.Single().PaymentCollectionMethod);
    }

    [TestMethod]
    public async Task ReturnsExistingSubscription_GivenRepeatedSubscribe()
    {
        var first = await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });
        first.EnsureSuccessStatusCode();

        var second = await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.IsTrue(firstModel!.WasNewlyCreated);
        Assert.IsFalse(secondModel!.WasNewlyCreated);
        Assert.AreEqual(firstModel.Subscription!.Id, secondModel.Subscription!.Id);
        Assert.AreEqual(1, _billing.Subscriptions.Count);
        Assert.AreEqual(1, _billing.CreationAttempts.Count());
    }

    [TestMethod]
    public async Task CreatesOnlyOneSubscription_GivenConcurrentDoubleClick()
    {
        var requests = Enumerable.Range(0, 3)
            .Select(_ => PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "eshop-pro", FirstName = "Demo", LastName = "User" }))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(2, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, _billing.Subscriptions.Count);

        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!.Subscription!.Id));
        Assert.AreEqual(1, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task ReturnsNotFound_GivenUnknownPlanHandle()
    {
        var response = await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "no-such-plan" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequest_GivenMissingPlanHandle()
    {
        var response = await PostSubscribe(new CreateSubscriptionRequest());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnprocessable_GivenPlanThatRequiresAPaymentMethod()
    {
        var response = await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "card-required-plan" });

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.AreEqual(0, _billing.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReturnsMySubscriptions_GivenSubscribedShopper()
    {
        await PostSubscribe(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });

        var response = await _client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public async Task ReturnsEmptyList_GivenShopperWhoNeverSubscribed()
    {
        var response = await _client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReturnsUnauthorized_GivenNoBearerToken()
    {
        using var anonymous = _factory.CreateClient();

        foreach (var path in new[] { "api/subscription-plans", "api/my-subscriptions" })
        {
            var getResponse = await anonymous.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.Unauthorized, getResponse.StatusCode, path);
        }

        var postResponse = await anonymous.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    private Task<HttpResponseMessage> PostSubscribe(CreateSubscriptionRequest request) =>
        _client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
}
