using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionBillingEndpoints;

[TestClass]
[DoNotParallelize]
public class SubscriptionBillingEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task SubscriptionEndpointsRequireJwtAuthentication()
    {
        var client = ProgramTest.NewClient;
        var plans = await client.GetAsync("api/subscription-plans");
        var mine = await client.GetAsync("api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest("test-plan"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentSubscribeRequestsCreateOneEnrollmentAndListIt()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var firstClient = AuthorizedClient(token);
        var secondClient = AuthorizedClient(token);

        var plansResponse = await firstClient.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = await plansResponse.Content.ReadFromJsonAsync<SubscriptionPlansResponse>(JsonOptions);
        Assert.IsNotNull(plans);
        Assert.AreEqual(1, plans.Plans.Count);
        Assert.AreEqual("test-plan", plans.Plans[0].ProductHandle);

        var firstPost = firstClient.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest("test-plan"),
            JsonOptions);
        var secondPost = secondClient.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest("test-plan"),
            JsonOptions);
        var responses = await Task.WhenAll(firstPost, secondPost);

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.Created));
        var first = await responses[0].Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        var second = await responses[1].Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.Reference, second.Reference);
        Assert.AreEqual(1, ProgramTest.BillingGateway.CreateCalls);

        var mineResponse = await firstClient.GetAsync("api/my-subscriptions");
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<SubscriptionsResponse>(JsonOptions);
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Subscriptions.Count);
        Assert.AreEqual(first.Reference, mine.Subscriptions[0].Reference);
    }

    private static HttpClient AuthorizedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
