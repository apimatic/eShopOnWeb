using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task RequiresAuthentication_OnAllSubscriptionEndpoints()
    {
        var client = ProgramTest.NewClient;

        var plans = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);

        var mySubscriptions = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, mySubscriptions.StatusCode);

        var subscribe = await client.PostAsync("api/subscriptions", JsonContent.Create(new { }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_WithoutPlanHandle_ReturnsBadRequest()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonContent.Create(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

[TestClass]
public class SubscriptionFlowTests
{
    private FakeMaxioServer? _server;
    private WebApplicationFactory<Program>? _factory;

    [TestInitialize]
    public void Initialize()
    {
        _server = new FakeMaxioServer();
        _factory = new StubMaxioApplication(_server);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory?.Dispose();
        _server?.Dispose();
    }

    [TestMethod]
    public async Task FullSubscribeFlow_IsIdempotentAndVisibleInMySubscriptions()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, plans.StatusCode);
        var plansBody = await plans.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();
        Assert.AreEqual(2, plansBody!.Plans.Count);
        Assert.AreEqual("eshop-pro", plansBody.Plans[0].Handle);
        Assert.AreEqual(29900m / 100m, plansBody.Plans[0].Price);

        var subscribe = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Created, subscribe.StatusCode);
        var created = await subscribe.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.AreEqual("eshop-pro", created!.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", created.Subscription.PlanName);
        Assert.AreEqual(299m, created.Subscription.Price);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsTrue(created.Subscription.Id > 0);
        Assert.IsNotNull(created.Subscription.NextBillingDate);
        var firstSubscriptionId = created.Subscription.Id;

        // A second, identical subscribe must not create a second subscription.
        var repeat = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        var repeated = await repeat.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.AreEqual(firstSubscriptionId, repeated!.Subscription.Id);
        Assert.AreEqual(1, _server!.Subscriptions.Count);

        var mySubscriptions = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mySubscriptions.StatusCode);
        var body = await mySubscriptions.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.AreEqual(1, body!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", body.Subscriptions[0].PlanHandle);
        Assert.AreEqual("active", body.Subscriptions[0].State);
    }

    [TestMethod]
    public async Task Subscribe_ToDifferentPlan_CreatesSecondSubscription()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var pro = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        pro.EnsureSuccessStatusCode();

        var basic = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "basic-plan" });
        basic.EnsureSuccessStatusCode();

        var mySubscriptions = await client.GetAsync("api/my-subscriptions");
        var body = await mySubscriptions.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.AreEqual(2, body!.Subscriptions.Count);
        Assert.AreEqual(2, _server!.Subscriptions.Count);
    }

    private sealed class StubMaxioApplication : WebApplicationFactory<Program>
    {
        private readonly FakeMaxioServer _server;

        public StubMaxioApplication(FakeMaxioServer server)
        {
            _server = server;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseOnlyInMemoryDatabase"] = "true"
                }));

            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _server.Handler));
        }
    }
}
