using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task PlansRequireAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingRequiresAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansAreListedForAnAuthenticatedShopper()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateAuthenticatedClient().GetAsync("/api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var plans = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>(Json);

        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans!.Plans.Count);

        var pro = plans.Plans.Single(plan => plan.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribingCreatesACustomerAndASubscription()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);
        Assert.IsNotNull(created?.Subscription);
        Assert.IsFalse(created!.AlreadySubscribed);
        Assert.AreEqual("eshop-pro", created.Subscription!.PlanHandle);
        Assert.AreEqual("active", created.Subscription.State);
        Assert.IsTrue(created.Subscription.IsLive);
        Assert.IsNotNull(created.Subscription.NextBillingAt);
        Assert.AreEqual(29900, created.Subscription.PriceInCents);
        Assert.AreEqual("eshoponweb:sub:demouser@microsoft.com:eshop-pro", created.Subscription.Reference);

        Assert.AreEqual(1, factory.Maxio.CreatedCustomerCount);
        Assert.AreEqual(1, factory.Maxio.CreatedSubscriptionCount);
    }

    [TestMethod]
    public async Task SubscribingTwiceDoesNotEnrollTheShopperTwice()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });
        var second = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);

        Assert.IsTrue(secondBody!.AlreadySubscribed);
        Assert.AreEqual(firstBody!.Subscription!.Id, secondBody.Subscription!.Id);
        Assert.AreEqual(1, factory.Maxio.CreatedSubscriptionCount);
        Assert.AreEqual(1, factory.Maxio.CreatedCustomerCount);
    }

    [TestMethod]
    public async Task ConcurrentSubscribeAttemptsResolveToOneSubscription()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" })));

        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
        }

        var bodies = await Task.WhenAll(responses.Select(response =>
            response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json)));

        Assert.AreEqual(1, bodies.Select(body => body!.Subscription!.Id).Distinct().Count());
        Assert.AreEqual(1, factory.Maxio.CreatedSubscriptionCount);
        Assert.AreEqual(1, factory.Maxio.CreatedCustomerCount);
    }

    [TestMethod]
    public async Task SubscriptionsAreReportedBackToTheShopper()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateAuthenticatedClient();

        await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "basic-plan" });

        var response = await client.GetAsync("/api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var mine = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(Json);
        var subscription = mine!.Subscriptions.Single();

        Assert.AreEqual("basic-plan", subscription.PlanHandle);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(2900, subscription.PriceInCents);
        Assert.AreEqual("USD", subscription.Currency);
        Assert.IsNotNull(subscription.NextBillingAt);
    }

    [TestMethod]
    public async Task ShoppersOnlySeeTheirOwnSubscriptions()
    {
        using var factory = new SubscriptionApiFactory();
        var demoUser = factory.CreateAuthenticatedClient();
        var otherUser = factory.CreateAuthenticatedClient(ApiTokenHelper.GetAdminUserToken());

        await demoUser.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        var response = await otherUser.GetAsync("/api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var mine = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(Json);
        Assert.AreEqual(0, mine!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task AnUnknownPlanIsReportedAsNotFound()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateAuthenticatedClient()
            .PostAsJsonAsync("/api/subscriptions", new { planHandle = "no-such-plan" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "no-such-plan");
    }

    [TestMethod]
    public async Task AMissingPlanHandleIsRejectedWhenNoDefaultIsConfigured()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync("/api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task TheConfiguredDefaultPlanIsUsedWhenTheRequestNamesNone()
    {
        using var factory = new SubscriptionApiFactory(defaultPlanHandle: "eshop-pro");
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync("/api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);
        Assert.AreEqual("eshop-pro", created!.Subscription!.PlanHandle);
    }

    [TestMethod]
    public async Task AnIdempotencyKeyIsHonoured()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro", idempotencyKey = "checkout-42" });
        var second = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro", idempotencyKey = "checkout-42" });

        var firstBody = await first.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(Json);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(firstBody!.Subscription!.Id, secondBody!.Subscription!.Id);
        Assert.AreEqual("eshoponweb:sub:demouser@microsoft.com:key:checkout-42", firstBody.Subscription.Reference);
    }

    [TestMethod]
    public async Task AnOverlongIdempotencyKeyIsRejected()
    {
        using var factory = new SubscriptionApiFactory();
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/subscriptions",
            new { planHandle = "eshop-pro", idempotencyKey = new string('k', 100) });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task WithoutConfigurationTheCapabilityReportsItselfUnavailable()
    {
        using var factory = new SubscriptionApiFactory(configureMaxio: false);
        var response = await factory.CreateAuthenticatedClient().GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey");
    }
}
