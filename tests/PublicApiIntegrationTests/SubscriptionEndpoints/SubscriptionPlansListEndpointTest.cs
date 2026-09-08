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
public class SubscriptionPlansListEndpointTest
{
    [TestMethod]
    public async Task ReturnsPlansGivenAuthenticatedUser()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(MaxioTestSupport.NewIdentity()));

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var stringResponse = await response.Content.ReadAsStringAsync();
        var model = stringResponse.FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.SubscriptionPlans.Any(), "Expected at least one subscribable plan.");

        var proPlan = model.SubscriptionPlans.FirstOrDefault(p => p.Handle == "eshop-pro");
        Assert.IsNotNull(proPlan, "Expected the eshop-pro plan in the catalog.");
        Assert.AreEqual("Pro Plan", proPlan!.Name);
        Assert.AreEqual(29900, proPlan.PriceInCents);
        Assert.AreEqual(299.00m, proPlan.Price);

        var basicPlan = model.SubscriptionPlans.FirstOrDefault(p => p.Handle == "basic-plan");
        Assert.IsNotNull(basicPlan, "Expected the basic-plan in the catalog.");
        Assert.AreEqual(2900, basicPlan!.PriceInCents);
        Assert.AreEqual("eshop-subscribe", basicPlan.ProductFamilyHandle);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
