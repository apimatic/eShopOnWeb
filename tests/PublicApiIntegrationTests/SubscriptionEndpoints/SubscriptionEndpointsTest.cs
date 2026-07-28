using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Deterministic endpoint-level tests that exercise the subscription handlers over a fake
/// billing service — no Maxio, no network. They pin the API contract: identity resolution,
/// the 201-vs-200 idempotency mapping, default-plan selection, and DTO projection.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static ClaimsPrincipal User(string? name) =>
        new(new ClaimsIdentity(
            name == null ? new Claim[0] : new[] { new Claim(ClaimTypes.Name, name) },
            authenticationType: "Test"));

    private static int StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 0;
    private static T ValueOf<T>(IResult result) => (T)((IValueHttpResult)result).Value!;

    [TestMethod]
    public async Task ListPlans_ProjectsAllPlansToDtos()
    {
        var endpoint = new ListSubscriptionPlansEndpoint();
        var svc = new FakeMaxioSubscriptionService();

        var result = await endpoint.HandleAsync(svc);

        Assert.AreEqual(StatusCodes.Status200OK, StatusOf(result));
        var response = ValueOf<ListSubscriptionPlansResponse>(result);
        Assert.AreEqual(2, response.Plans.Count);
        var pro = response.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("month", pro.Interval);
    }

    [TestMethod]
    public async Task Subscribe_FirstTime_Returns201AndResolvesIdentityFromToken()
    {
        var endpoint = new CreateSubscriptionEndpoint();
        var svc = new FakeMaxioSubscriptionService();
        var request = new CreateSubscriptionRequest
        {
            PlanHandle = "eshop-pro",
            Subscriber = SubscriptionMapping.ToSubscriber(User("shopper@example.com"))
        };

        var result = await endpoint.HandleAsync(request, svc);

        Assert.AreEqual(StatusCodes.Status201Created, StatusOf(result));
        var response = ValueOf<CreateSubscriptionResponse>(result);
        Assert.IsFalse(response.AlreadyExisted);
        Assert.AreEqual("eshop-pro", response.Subscription.PlanHandle);
        Assert.AreEqual("active", response.Subscription.State);
        // Identity flowed from the token to the billing command.
        Assert.AreEqual("shopper@example.com", svc.LastCommand!.Subscriber.UserId);
    }

    [TestMethod]
    public async Task Subscribe_SecondTimeSamePlan_IsIdempotent_Returns200SameSubscription()
    {
        var endpoint = new CreateSubscriptionEndpoint();
        var svc = new FakeMaxioSubscriptionService();
        var subscriber = SubscriptionMapping.ToSubscriber(User("shopper@example.com"));

        var first = await endpoint.HandleAsync(
            new CreateSubscriptionRequest { PlanHandle = "eshop-pro", Subscriber = subscriber }, svc);
        var second = await endpoint.HandleAsync(
            new CreateSubscriptionRequest { PlanHandle = "eshop-pro", Subscriber = subscriber }, svc);

        Assert.AreEqual(StatusCodes.Status201Created, StatusOf(first));
        Assert.AreEqual(StatusCodes.Status200OK, StatusOf(second));

        var firstResponse = ValueOf<CreateSubscriptionResponse>(first);
        var secondResponse = ValueOf<CreateSubscriptionResponse>(second);
        Assert.IsTrue(secondResponse.AlreadyExisted);
        Assert.AreEqual(firstResponse.Subscription.Id, secondResponse.Subscription.Id);
    }

    [TestMethod]
    public async Task Subscribe_WithoutPlanHandle_DefaultsToFirstAvailablePlan()
    {
        var endpoint = new CreateSubscriptionEndpoint();
        var svc = new FakeMaxioSubscriptionService();
        var request = new CreateSubscriptionRequest
        {
            PlanHandle = null,
            Subscriber = SubscriptionMapping.ToSubscriber(User("shopper@example.com"))
        };

        var result = await endpoint.HandleAsync(request, svc);

        Assert.AreEqual(StatusCodes.Status201Created, StatusOf(result));
        var response = ValueOf<CreateSubscriptionResponse>(result);
        Assert.AreEqual("basic-plan", response.Subscription.PlanHandle);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsShoppersSubscriptions()
    {
        var svc = new FakeMaxioSubscriptionService();
        var subscriber = SubscriptionMapping.ToSubscriber(User("shopper@example.com"));
        await new CreateSubscriptionEndpoint().HandleAsync(
            new CreateSubscriptionRequest { PlanHandle = "eshop-pro", Subscriber = subscriber }, svc);

        var result = await new ListMySubscriptionsEndpoint().HandleAsync(User("shopper@example.com"), svc);

        Assert.AreEqual(StatusCodes.Status200OK, StatusOf(result));
        var response = ValueOf<ListMySubscriptionsResponse>(result);
        Assert.AreEqual(1, response.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", response.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public void ToSubscriber_WithoutIdentityClaim_Throws401()
    {
        var ex = Assert.ThrowsException<MaxioIntegrationException>(() => SubscriptionMapping.ToSubscriber(User(null)));
        Assert.AreEqual(401, ex.StatusCode);
    }

    [TestMethod]
    public void ToSubscriber_UsesUsernameAsStableReference()
    {
        var subscriber = SubscriptionMapping.ToSubscriber(User("demouser@microsoft.com"));
        Assert.AreEqual("demouser@microsoft.com", subscriber.UserId);
        Assert.AreEqual("demouser@microsoft.com", subscriber.Email);
    }
}
