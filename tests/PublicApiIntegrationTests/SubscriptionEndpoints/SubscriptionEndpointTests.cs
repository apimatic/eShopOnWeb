using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireJwtAuthentication()
    {
        using var client = ProgramTest.NewClient;

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("api/subscriptions", new CreateSubscriptionRequest { ProductHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansAndMakesConcurrentSubscribeIdempotent()
    {
        using var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetFromJsonAsync<List<SubscriptionPlanResponse>>("api/subscription-plans");
        Assert.IsNotNull(plans);
        Assert.IsTrue(plans.Any(x => x.Handle == "eshop-pro" && x.PriceInCents == 29900));

        var beforeCreates = ProgramTest.Maxio.SubscriptionCreateCalls;
        var request = new CreateSubscriptionRequest { ProductHandle = "eshop-pro" };
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("api/subscriptions", request),
            client.PostAsJsonAsync("api/subscriptions", request));
        responses[0].EnsureSuccessStatusCode();
        responses[1].EnsureSuccessStatusCode();

        var first = await responses[0].Content.ReadFromJsonAsync<SubscriptionResponse>();
        var second = await responses[1].Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.Reference, second.Reference);
        Assert.AreEqual(1, ProgramTest.Maxio.SubscriptionCreateCalls - beforeCreates);

        var mine = await client.GetFromJsonAsync<List<SubscriptionResponse>>("api/my-subscriptions");
        Assert.IsNotNull(mine);
        Assert.IsTrue(mine.Any(x => x.Id == first.Id && x.State == "active" && x.NextBillingDate is not null));
    }
}
