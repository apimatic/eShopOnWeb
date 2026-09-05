using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static (WebApplicationFactory<Program> Factory, FakeMaxioSubscriptionService Fake) CreateFactory()
    {
        var fake = new FakeMaxioSubscriptionService();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioSubscriptionService>();
                services.AddSingleton<IMaxioSubscriptionService>(fake);
            }));
        return (factory, fake);
    }

    [TestMethod]
    public async Task SubscriptionPlans_Anonymous_ReturnsUnauthorized()
    {
        var (factory, _) = CreateFactory();
        var response = await factory.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionPlans_AuthenticatedUser_ReturnsPlansFromMaxio()
    {
        var (factory, _) = CreateFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(1, model!.Plans.Count);
        var plan = model.Plans[0];
        Assert.AreEqual("eshop-pro", plan.Handle);
        Assert.AreEqual(29900, plan.PriceInCents);
    }

    [TestMethod]
    public async Task CreateSubscription_MissingPlanHandle_ReturnsBadRequest()
    {
        var (factory, _) = CreateFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ValidPlan_SubscribesCurrentUserAndReturnsConfirmation()
    {
        var (factory, fake) = CreateFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(42, model!.Subscription.SubscriptionId);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual(29900, model.Subscription.PriceInCents);
        Assert.IsNotNull(model.Subscription.NextBillingAt);

        Assert.AreEqual(1, fake.SubscribeCalls.Count);
        var call = fake.SubscribeCalls[0];
        Assert.AreEqual("demouser@microsoft.com", call.CustomerReference);
        Assert.AreEqual("eshop-pro", call.PlanHandle);
    }

    [TestMethod]
    public async Task MySubscriptions_AuthenticatedUser_ReturnsCurrentUsersSubscriptions()
    {
        var (factory, _) = CreateFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(1, model!.Subscriptions.Count);
        var subscription = model.Subscriptions[0];
        Assert.AreEqual(42, subscription.SubscriptionId);
        Assert.AreEqual("eshop-pro", subscription.PlanHandle);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
