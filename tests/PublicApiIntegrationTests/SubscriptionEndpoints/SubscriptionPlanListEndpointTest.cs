using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionPlanListEndpointTest
{
    [TestMethod]
    public async Task RequiresAToken()
    {
        using var app = new MaxioTestApplication(new FakeMaxio());

        var response = await app.CreateClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsSubscribablePlansOfTheConfiguredFamily()
    {
        var maxio = new FakeMaxio();
        maxio.Products.Add(FakeMaxio.Product(3, "retired", "Retired", 100, archived: true));
        maxio.Products.Add(FakeMaxio.Product(4, "card-only", "Card only", 100, requireCreditCard: true));
        using var app = new MaxioTestApplication(maxio);

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var handles = body["plans"]!.AsArray().Select(p => (string)p!["handle"]!).ToArray();
        CollectionAssert.AreEqual(new[] { "basic", "pro" }, handles);
        Assert.AreEqual(299m, (decimal)body["plans"]![1]!["price"]!);
        Assert.AreEqual("month", (string)body["plans"]![1]!["intervalUnit"]!);
        Assert.IsFalse((bool)body["truncated"]!);
        // The family is addressed by handle, never by a numeric id.
        Assert.AreEqual(1, maxio.Count(HttpMethod.Get, $"/product_families/handle:{maxio.FamilyHandle}/products.json"));
    }

    [TestMethod]
    public async Task MarksTheListTruncatedWhenThePageCapIsReached()
    {
        var maxio = new FakeMaxio { EndlessProducts = true };
        using var app = new MaxioTestApplication(maxio);

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.IsTrue((bool)body["truncated"]!);
        Assert.AreEqual(5, maxio.Requests.Count(r => r.Path.EndsWith("/products.json")));
    }

    [TestMethod]
    public async Task UnknownFamilyIsReportedAsUnavailable()
    {
        var maxio = new FakeMaxio();
        using var app = new MaxioTestApplication(new FakeMaxio { FamilyHandle = "configured-family" });
        // FakeMaxio answers 404 for any other family, as Maxio does for a missing handle.
        using var misconfigured = app.WithWebHostBuilder(b => b.UseSetting("Maxio:ProductFamilyHandle", "missing-family"));

        var client = misconfigured.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
