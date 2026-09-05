using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These tests exercise the real Maxio sandbox (site/credentials come from user-secrets - see
/// Maxio:ApiKey / Maxio:Subdomain / Maxio:ProductFamilyHandle). Each test uses a unique buyer
/// reference (a throwaway email) so runs don't interfere with each other or with manual testing.
/// </summary>
[TestClass]
public class CreateSubscriptionEndpointTest
{
    private static HttpClient AuthenticatedClient(string userName)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        return client;
    }

    private static string NewTestBuyer(string label) => $"itest-{label}-{Guid.NewGuid():N}@example.com";

    [TestMethod]
    public async Task SubscribingToASeededPlanCreatesAnActiveSubscription()
    {
        var client = AuthenticatedClient(NewTestBuyer("subscribe"));

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(body);
        Assert.IsFalse(body!.AlreadyEnrolled);
        Assert.AreEqual("eshop-pro", body.Subscription.PlanHandle);
        Assert.AreEqual(29900, body.Subscription.PriceInCents);
        Assert.IsTrue(body.Subscription.SubscriptionId > 0);
        Assert.IsNotNull(body.Subscription.NextBillingAt);
    }

    [TestMethod]
    public async Task SubscribingTwiceForTheSameBuyerAndPlanIsIdempotent()
    {
        var client = AuthenticatedClient(NewTestBuyer("idempotent"));

        var first = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        var second = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();

        Assert.IsTrue(secondBody!.AlreadyEnrolled);
        Assert.AreEqual(firstBody!.Subscription.SubscriptionId, secondBody.Subscription.SubscriptionId);
    }

    [TestMethod]
    public async Task SubscribingToAnUnknownPlanHandleReturnsNotFound()
    {
        var client = AuthenticatedClient(NewTestBuyer("unknown-plan"));

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "no-such-plan-handle" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
