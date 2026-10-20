using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
    public static void ClassInitialize(TestContext _)
    {
        _application = new MaxioStubbedApplication();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _application.Dispose();
    }

    [TestMethod]
    public async Task PlansRequireAuthentication()
    {
        var response = await _application.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansFromTheConfiguredFamily()
    {
        var response = await AuthenticatedClient(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, model!.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299m, model.Plans.Single(p => p.Handle == "eshop-pro").Price);
        Assert.IsFalse(model.IsTruncated);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndShowsInMySubscriptions()
    {
        var client = AuthenticatedClient(ApiTokenHelper.GetAdminUserToken());

        var first = await client.PostAsync("api/subscriptions", PlanJson("basic-plan"));
        var second = await client.PostAsync("api/subscriptions", PlanJson("basic-plan"));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        var repeated = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual("Created", created.Status);
        Assert.AreEqual("AlreadySubscribed", repeated.Status);
        Assert.AreEqual(created.Subscription!.Id, repeated.Subscription!.Id);
        Assert.AreEqual("basic-plan", created.Subscription.PlanHandle);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.AreEqual(29m, created.Subscription.Price);
        Assert.IsNotNull(created.Subscription.NextBillingAt);
        Assert.AreEqual(1, _application.Maxio.Count("POST /subscriptions.json"));

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.IsTrue(list.Subscriptions.Any(s => s.Id == created.Subscription.Id));
    }

    [TestMethod]
    public async Task RejectsAPlanThatIsNotOffered()
    {
        var response = await AuthenticatedClient(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/subscriptions", PlanJson("api-call"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RequiresAPlanHandle()
    {
        var response = await AuthenticatedClient(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AnUnreachableProviderIsReportedAsServiceUnavailable()
    {
        using var application = new MaxioStubbedApplication();
        application.Maxio.Unreachable = true;
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient(string token)
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent PlanJson(string planHandle) =>
        new(JsonSerializer.Serialize(new { planHandle }), Encoding.UTF8, "application/json");
}
