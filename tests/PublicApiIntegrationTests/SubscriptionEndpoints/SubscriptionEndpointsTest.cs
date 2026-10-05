using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
    private static readonly FakeBillingGateway s_gateway = new();
    private static WebApplicationFactory<Program> s_application = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        s_application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBillingGateway>();
                services.AddSingleton<IBillingGateway>(s_gateway);
            }));
    }

    [ClassCleanup]
    public static void ClassCleanup() => s_application.Dispose();

    private static HttpClient ShopperClient()
    {
        var client = s_application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Subscribe(string planHandle) =>
        new(JsonSerializer.Serialize(new { planHandle }), Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task ReturnsUnauthorizedWithoutToken(string route)
    {
        var response = await s_application.CreateClient().GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedWithoutToken()
    {
        var response = await s_application.CreateClient().PostAsync("api/subscriptions", Subscribe("plan-a"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        var response = await ShopperClient().GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsFalse(model!.IsTruncated);
        var planA = model.Plans.Single(p => p.Handle == "plan-a");
        Assert.AreEqual(299m, planA.Price);
        Assert.AreEqual("month", planA.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribesThenRepeatReturnsSameSubscription()
    {
        var client = ShopperClient();

        var first = await client.PostAsync("api/subscriptions", Subscribe("plan-b"));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual("plan-b", created!.Subscription.PlanHandle);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.AreEqual(29m, created.Subscription.Price);
        Assert.IsNotNull(created.Subscription.NextBillingAt);
        Assert.IsFalse(created.AlreadySubscribed);

        var repeat = await client.PostAsync("api/subscriptions", Subscribe("plan-b"));
        Assert.AreEqual(HttpStatusCode.OK, repeat.StatusCode);
        var again = (await repeat.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsTrue(again!.AlreadySubscribed);
        Assert.AreEqual(created.Subscription.SubscriptionId, again.Subscription.SubscriptionId);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(1, list!.Subscriptions.Count(s => s.PlanHandle == "plan-b"));
    }

    [TestMethod]
    public async Task DoubleClickCreatesOneSubscription()
    {
        var before = s_gateway.CreateSubscriptionCalls;
        var client = ShopperClient();

        var responses = await Task.WhenAll(
            client.PostAsync("api/subscriptions", Subscribe("plan-c")),
            client.PostAsync("api/subscriptions", Subscribe("plan-c")));

        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict or HttpStatusCode.OK));
        Assert.AreEqual(before + 1, s_gateway.CreateSubscriptionCalls);
    }

    [TestMethod]
    public async Task RejectsPlanThatIsNotOffered()
    {
        var response = await ShopperClient().PostAsync("api/subscriptions", Subscribe("api-call"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RequiresPlanHandle()
    {
        var response = await ShopperClient().PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnresponsiveMaxioReturnsGatewayTimeoutAndKeepsRequestPending()
    {
        var client = ShopperClient();

        var response = await client.PostAsync("api/subscriptions", Subscribe(FakeBillingGateway.TimeoutPlan));

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var error = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetails>();
        StringAssert.StartsWith(error!.Message, "Maxio did not respond");

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync())
            .FromJson<ListMySubscriptionsResponse>();
        Assert.IsTrue(mine!.Pending.Any(p => p.PlanHandle == FakeBillingGateway.TimeoutPlan));
    }

    [TestMethod]
    public async Task MaxioRejectionIsUnprocessableWithMaxioMessage()
    {
        var response = await ShopperClient().PostAsync("api/subscriptions", Subscribe(FakeBillingGateway.RejectedPlan));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetails>();
        StringAssert.Contains(error!.Message, "Product is not available.");
    }
}
