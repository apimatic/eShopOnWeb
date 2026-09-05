using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListSubscriptionPlansEndpointTest
{
    private static SubscriptionApiFactory _factory = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new SubscriptionApiFactory();

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsSeededPlansForAuthenticatedUser()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var model = body.FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Any(p => p.Handle == "eshop-pro" && p.PriceInCents == 29900));
        Assert.IsTrue(model.Plans.Any(p => p.Handle == "basic-plan" && p.PriceInCents == 2900));
    }
}
