using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient ClientFor(PublicApiTestFactory factory, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static async Task AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task RequiresBearerToken(string route)
    {
        await using var factory = new PublicApiTestFactory();

        var response = await ClientFor(factory, null).GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscription_RequiresBearerToken()
    {
        await using var factory = new PublicApiTestFactory();

        var response = await ClientFor(factory, null).PostAsJsonAsync("api/subscriptions", new { planHandle = "pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, factory.Maxio.Requests.Count);
    }

    [TestMethod]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        await using var factory = new PublicApiTestFactory();

        var response = await ClientFor(factory, ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        await AssertStatus(HttpStatusCode.OK, response);
        var model = (await response.Content.ReadAsStringAsync()).Deserialize<ListSubscriptionPlansResponse>()!;
        Assert.IsFalse(model.IsTruncated);
        var pro = model.Plans.Single(p => p.Handle == "pro");
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeFlow_CreatesOnce_ThenReportsExisting_AndShowsInMySubscriptions()
    {
        await using var factory = new PublicApiTestFactory();
        var client = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());

        var created = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro" });
        var repeated = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "pro" });
        var mine = await client.GetAsync("api/my-subscriptions");

        await AssertStatus(HttpStatusCode.Created, created);
        var createdModel = (await created.Content.ReadAsStringAsync()).Deserialize<CreateSubscriptionResponse>()!;
        Assert.AreEqual("created", createdModel.Status);
        Assert.AreEqual("pro", createdModel.Subscription!.PlanHandle);
        Assert.AreEqual(299m, createdModel.Subscription.Price);
        Assert.AreEqual("active", createdModel.Subscription.State);
        Assert.IsNotNull(createdModel.Subscription.NextBillingAt);

        await AssertStatus(HttpStatusCode.OK, repeated);
        var repeatedModel = (await repeated.Content.ReadAsStringAsync()).Deserialize<CreateSubscriptionResponse>()!;
        Assert.AreEqual("existing", repeatedModel.Status);
        Assert.AreEqual(createdModel.Subscription.SubscriptionId, repeatedModel.Subscription!.SubscriptionId);

        await AssertStatus(HttpStatusCode.OK, mine);
        var mineModel = (await mine.Content.ReadAsStringAsync()).Deserialize<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(createdModel.Subscription.SubscriptionId, mineModel.Subscriptions.Single().SubscriptionId);

        Assert.AreEqual(1, factory.Maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    [DataRow(null, HttpStatusCode.BadRequest)]
    [DataRow("not-a-plan", HttpStatusCode.BadRequest)]
    public async Task InvalidPlan_IsRejected(string? planHandle, HttpStatusCode expected)
    {
        await using var factory = new PublicApiTestFactory();

        var response = await ClientFor(factory, ApiTokenHelper.GetNormalUserToken())
            .PostAsJsonAsync("api/subscriptions", new { planHandle });

        await AssertStatus(expected, response);
        Assert.AreEqual(0, factory.Maxio.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task MaxioUnavailable_MapsToServiceUnavailable_WithoutLeakingDetails()
    {
        await using var factory = new PublicApiTestFactory();
        factory.Maxio.BeforeRoute = _ => throw new HttpRequestException("connection refused by maxio.test.invalid");

        var response = await ClientFor(factory, ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        await AssertStatus(HttpStatusCode.ServiceUnavailable, response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(body.Contains("maxio.test.invalid"), body);
        Assert.IsFalse(body.Contains("Sdk"), body);
    }

    [TestMethod]
    public void MissingApiKey_StopsTheHostFromStarting()
    {
        using var factory = new PublicApiTestFactory().WithWebHostBuilder(b => b.UseSetting("Maxio:ApiKey", ""));

        var ex = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());

        StringAssert.Contains(ex.Message, "Maxio:ApiKey");
    }
}
