using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the subscribe hero flow's HTTP surface (routing, JWT auth, JSON contracts) with a
/// fake IMaxioSubscriptionService substituted for the real Maxio-backed one, so these tests run
/// without network access or real credentials.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static (HttpClient Client, FakeMaxioSubscriptionService Fake) CreateAuthenticatedClient(string token)
    {
        var fake = new FakeMaxioSubscriptionService();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioSubscriptionService>();
                services.AddSingleton<IMaxioSubscriptionService>(fake);
            });
        });

        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return (client, fake);
    }

    [TestMethod]
    public async Task ListSubscriptionPlans_ReturnsNotAuthorizedGivenNoToken()
    {
        var (client, _) = CreateAuthenticatedClient(null!);
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListSubscriptionPlans_ReturnsConfiguredPlansGivenValidToken()
    {
        var (client, _) = CreateAuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(1, model!.SubscriptionPlans.Count);
        var plan = model.SubscriptionPlans[0];
        Assert.AreEqual(FakeMaxioSubscriptionService.ProPlan.Handle, plan.Handle);
        Assert.AreEqual(FakeMaxioSubscriptionService.ProPlan.Price, plan.Price);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsNotAuthorizedGivenNoToken()
    {
        var (client, _) = CreateAuthenticatedClient(null!);
        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsBadRequestGivenUnknownPlanHandle()
    {
        var (client, _) = CreateAuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "not-a-real-plan" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ThenListMySubscriptions_ReflectsTheNewSubscription()
    {
        var (client, fake) = CreateAuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var subscribeResponse = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));
        subscribeResponse.EnsureSuccessStatusCode();
        var subscribed = (await subscribeResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual("eshop-pro", subscribed!.Subscription.Plan.Handle);
        Assert.AreEqual("active", subscribed.Subscription.State);

        var listResponse = await client.GetAsync("api/my-subscriptions");
        listResponse.EnsureSuccessStatusCode();
        var list = (await listResponse.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.AreEqual(1, list!.Subscriptions.Count);
        Assert.AreEqual(subscribed.Subscription.SubscriptionId, list.Subscriptions[0].SubscriptionId);
        Assert.IsFalse(string.IsNullOrEmpty(fake.LastCustomerReferenceSeen));
    }

    [TestMethod]
    public async Task CreateSubscription_CalledTwice_IsIdempotent()
    {
        var (client, _) = CreateAuthenticatedClient(ApiTokenHelper.GetNormalUserToken());

        var first = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));
        var second = await client.PostAsync("api/subscriptions", JsonBody(new { planHandle = "eshop-pro" }));

        var firstBody = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var secondBody = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(firstBody!.Subscription.SubscriptionId, secondBody!.Subscription.SubscriptionId);
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
