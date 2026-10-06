using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static StringContent Subscribe(string planHandle) =>
        new($$"""{"planHandle":"{{planHandle}}"}""", Encoding.UTF8, "application/json");

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresAJwt(string method, string route)
    {
        await using var app = new MaxioTestApplication();
        var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "POST" ? Subscribe("eshop-pro") : null };

        var response = await app.CreateClient().SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansOfTheConfiguredFamily()
    {
        await using var app = new MaxioTestApplication();

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>()!;
        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, model.Plans.Select(p => p.Handle).ToArray());
        Assert.AreEqual(299m, model.Plans.Single(p => p.Handle == "eshop-pro").Price);
        Assert.IsFalse(model.IsTruncated);
    }

    [TestMethod]
    public async Task SubscribeEnrollsTheCallerAndIsReflectedInMySubscriptions()
    {
        await using var app = new MaxioTestApplication();
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var created = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var model = (await created.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsTrue(model.Created);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual(299m, model.Subscription.Price);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsNotNull(model.Subscription.NextBillingAt);

        var mine = (await (await client.GetAsync("api/my-subscriptions")).Content.ReadAsStringAsync())
            .FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(model.Subscription.Id, mine.Subscriptions.Single().Id);
        Assert.AreEqual(1, app.Maxio.CustomerPosts);
        Assert.AreEqual(1, app.Maxio.SubscriptionPosts);
    }

    [TestMethod]
    public async Task RepeatingTheSubscribeReturnsTheSameSubscription()
    {
        await using var app = new MaxioTestApplication();
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());
        var first = (await (await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"))).Content.ReadAsStringAsync())
            .FromJson<CreateSubscriptionResponse>()!;

        var again = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        var model = (await again.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.IsFalse(model.Created);
        Assert.AreEqual(first.Subscription.Id, model.Subscription.Id);
        Assert.AreEqual(1, app.Maxio.SubscriptionPosts);
        Assert.AreEqual(1, app.Maxio.CustomerPosts);
    }

    [TestMethod]
    public async Task DoubleClickCreatesExactlyOneCustomerAndSubscription()
    {
        await using var app = new MaxioTestApplication();
        app.Maxio.CreateSubscriptionDelay = TimeSpan.FromMilliseconds(300);
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync("api/subscriptions", Subscribe("eshop-pro"))));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        Assert.AreEqual(1, statuses.Count(s => s == HttpStatusCode.Created), string.Join(",", statuses));
        Assert.IsTrue(statuses.All(s => s is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict), string.Join(",", statuses));
        Assert.AreEqual(1, app.Maxio.SubscriptionPosts);
        Assert.AreEqual(1, app.Maxio.CustomerPosts);
    }

    [TestMethod]
    public async Task UnknownPlanIsABadRequest()
    {
        await using var app = new MaxioTestApplication();

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync("api/subscriptions", Subscribe("gold"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, app.Maxio.CustomerPosts);
    }

    [TestMethod]
    public async Task SubscribingToADifferentPlanIsAConflict()
    {
        await using var app = new MaxioTestApplication();
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());
        await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        var response = await client.PostAsync("api/subscriptions", Subscribe("basic-plan"));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(1, app.Maxio.SubscriptionPosts);
    }

    [TestMethod]
    public async Task UnresponsiveMaxioFailsWithinTheBudgetSayingMaxioDidNotRespond()
    {
        await using var app = new MaxioTestApplication(s => { s.RequestBudgetSeconds = 2; s.SettleBudgetSeconds = 1; });
        app.Maxio.HangEverything = true;
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var watch = Stopwatch.StartNew();
        var response = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));
        watch.Stop();

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var error = (await response.Content.ReadAsStringAsync()).FromJson<ErrorDetails>()!;
        StringAssert.Contains(error.Message, "Maxio did not respond");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [TestMethod]
    public async Task LostCreateResponseIsSettledByReferenceWithoutASecondSubscription()
    {
        await using var app = new MaxioTestApplication(s => { s.RequestBudgetSeconds = 2; s.SettleBudgetSeconds = 2; });
        app.Maxio.LoseCreateSubscriptionResponse = true;
        var client = app.ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);

        app.Maxio.LoseCreateSubscriptionResponse = false;
        var again = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(1, app.Maxio.SubscriptionPosts);
    }

    [TestMethod]
    public async Task MySubscriptionsIsEmptyBeforeSubscribing()
    {
        await using var app = new MaxioTestApplication();

        var response = await app.ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>()!;
        Assert.AreEqual(0, model.Subscriptions.Count);
    }
}
