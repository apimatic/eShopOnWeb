using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static MaxioStubbedApplication _application = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _application = new MaxioStubbedApplication();

    [ClassCleanup]
    public static void ClassCleanup() => _application.Dispose();

    private static HttpClient ClientFor(string? userName)
    {
        var client = _application.CreateClient();
        if (userName is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        }
        return client;
    }

    private static string NewUser() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static StringContent Subscribe(string planHandle) =>
        new($"{{\"planHandle\":\"{planHandle}\"}}", Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task ReadEndpointsRequireAToken(string route)
    {
        var response = await ClientFor(null).GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var response = await ClientFor(null).PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansWithPrices()
    {
        var response = await ClientFor(NewUser()).GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsFalse(model!.IsTruncated);
        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeCreatesOnceThenReturnsTheExistingSubscription()
    {
        var client = ClientFor(NewUser());
        var createsBefore = _application.Maxio.SubscriptionCreates;

        var first = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));
        var second = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        var replayed = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(created.Created);
        Assert.IsFalse(replayed.Created);
        Assert.AreEqual(created.Subscription.SubscriptionId, replayed.Subscription.SubscriptionId);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.AreEqual(299.00m, created.Subscription.Price);
        Assert.IsNotNull(created.Subscription.NextBillingDate);
        Assert.AreEqual(createsBefore + 1, _application.Maxio.SubscriptionCreates);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(created.Subscription.SubscriptionId, list.Subscriptions.Single().SubscriptionId);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnUnknownPlan()
    {
        var response = await ClientFor(NewUser()).PostAsync("api/subscriptions", Subscribe("no-such-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Unknown plan");
    }

    [TestMethod]
    public async Task SubscribeRequiresAPlanHandle()
    {
        var response = await ClientFor(NewUser()).PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsIsEmptyForANewShopper()
    {
        var response = await ClientFor(NewUser()).GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;

        Assert.AreEqual(0, model.Subscriptions.Count);
    }
}
