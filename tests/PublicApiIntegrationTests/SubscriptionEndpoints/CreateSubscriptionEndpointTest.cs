using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTest
{
    [TestMethod]
    public async Task CreatesSubscriptionAndIsIdempotent()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var request = new CreateSubscriptionRequest { PlanHandle = "eshop-pro" };

        var first = await client.PostAsJsonAsync("api/subscriptions", request);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstModel = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel);
        Assert.AreEqual("active", firstModel!.Subscription.State);
        Assert.AreEqual("eshop-pro", firstModel.Subscription.PlanHandle);
        Assert.AreEqual(299.00m, firstModel.Subscription.Price);
        Assert.IsNotNull(firstModel.Subscription.CurrentPeriodEndsAt);

        var second = await client.PostAsJsonAsync("api/subscriptions", request);
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);
        var secondModel = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(secondModel);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel!.Subscription.Id);
    }

    [TestMethod]
    public async Task ReturnsBadRequestForMissingPlanHandle()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest());
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { PlanHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
