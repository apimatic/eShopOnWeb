using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private static WebApplicationFactory<Program>? _application;

    private HttpClient CreateClient()
    {
        _application ??= new WebApplicationFactory<Program>();
        return _application.CreateClient();
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutToken()
    {
        var response = await CreateClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedWithoutToken()
    {
        var response = await CreateClient().GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsPlansForAuthenticatedUser()
    {
        var response = await CreateAuthorizedClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SubscribeThenGetMySubscriptionsIsIdempotent()
    {
        var client = CreateAuthorizedClient();

        var create1 = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        var body1 = await create1.Content.ReadAsStringAsync();
        Assert.IsTrue(create1.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, body1);
        var result1 = await create1.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(result1);
        Assert.IsTrue(result1.Created);
        Assert.AreEqual("active", result1.Subscription.State.ToLowerInvariant());
        Assert.IsNotNull(result1.Subscription.NextBillingDate);

        var create2 = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.OK, create2.StatusCode);
        var result2 = await create2.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(result2);
        Assert.IsFalse(result2.Created);
        Assert.AreEqual(result1.Subscription.Id, result2.Subscription.Id);

        var list = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        var subscriptions = await list.Content.ReadFromJsonAsync<MySubscriptionListResponse>();
        Assert.IsNotNull(subscriptions);
        CollectionAssert.Contains(subscriptions.Subscriptions.Select(s => s.Id).ToList(), result1.Subscription.Id);
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanReturnsNotFound()
    {
        var client = CreateAuthorizedClient();
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "does-not-exist" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleReturnsBadRequest()
    {
        var client = CreateAuthorizedClient();
        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
