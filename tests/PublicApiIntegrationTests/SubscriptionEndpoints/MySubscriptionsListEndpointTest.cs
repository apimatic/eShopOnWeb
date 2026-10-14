using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MySubscriptionsListEndpointTest
{
    [TestMethod]
    public async Task RequiresAToken()
    {
        using var app = new MaxioTestApplication(new FakeMaxio());

        var response = await app.CreateClient().GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperWithoutBillingAccountHasNoSubscriptionsAndNothingIsCreated()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(maxio);

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.AreEqual(0, body["subscriptions"]!.AsArray().Count);
        Assert.AreEqual(0, maxio.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ListsOnlyTheCallersSubscriptions()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(maxio);
        var shopper = app.ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = app.ClientFor(ApiTokenHelper.GetAdminUserToken());

        (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro" })).EnsureSuccessStatusCode();
        (await shopper.PostAsJsonAsync("api/subscriptions", new { planHandle = "basic" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("api/subscriptions", new { planHandle = "basic" })).EnsureSuccessStatusCode();

        var mine = JsonNode.Parse(await shopper.GetStringAsync("api/my-subscriptions"))!;

        var plans = mine["subscriptions"]!.AsArray().Select(s => (string)s!["planHandle"]!).OrderBy(h => h).ToArray();
        CollectionAssert.AreEqual(new[] { "basic", "pro" }, plans);
        Assert.IsTrue(mine["subscriptions"]!.AsArray().All(s => ((string)s!["reference"]!).StartsWith("eshop:demouser@microsoft.com:")));
    }
}
