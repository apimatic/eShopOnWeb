using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient AuthedClient(SubscriptionApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task GetPlans_ReturnsPlansWithFormattedPrice()
    {
        using var factory = new SubscriptionApiFactory();
        factory.Billing.Plans.Add(new SubscriptionPlan
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            Price = 299m,
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            Currency = "USD",
        });

        var response = await AuthedClient(factory).GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "eshop-pro");
        StringAssert.Contains(body, "$299.00 / month");
    }

    [TestMethod]
    public async Task Subscribe_NewEnrolment_Returns201()
    {
        using var factory = new SubscriptionApiFactory();
        factory.Billing.OnSubscribe = (_, plan) => new CustomerSubscription
        {
            SubscriptionId = 1,
            PlanHandle = plan ?? "eshop-pro",
            PlanName = "Pro Plan",
            Price = 299m,
            PriceInCents = 29900,
            State = "active",
            NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1),
            AlreadyExisted = false,
        };

        var response = await AuthedClient(factory)
            .PostAsync("/api/subscriptions", JsonContent("{}"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"state\":\"active\"");
        // Identity flowed from the token, not the body.
        Assert.AreEqual("demouser@microsoft.com", factory.Billing.LastSubscriber!.Reference);
    }

    [TestMethod]
    public async Task Subscribe_AlreadySubscribed_Returns200()
    {
        using var factory = new SubscriptionApiFactory();
        factory.Billing.OnSubscribe = (_, _) => new CustomerSubscription
        {
            SubscriptionId = 1,
            PlanHandle = "eshop-pro",
            State = "active",
            AlreadyExisted = true,
        };

        var response = await AuthedClient(factory)
            .PostAsync("/api/subscriptions", JsonContent("{}"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_Returns404()
    {
        using var factory = new SubscriptionApiFactory();
        factory.Billing.SubscribeException =
            new BillingException("Plan 'nope' was not found.", HttpStatusCode.NotFound);

        var response = await AuthedClient(factory)
            .PostAsync("/api/subscriptions", JsonContent("{\"planHandle\":\"nope\"}"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsSubscriptions()
    {
        using var factory = new SubscriptionApiFactory();
        factory.Billing.Subscriptions.Add(new CustomerSubscription
        {
            SubscriptionId = 7,
            PlanHandle = "basic-plan",
            PlanName = "Basic Plan",
            Price = 29m,
            State = "active",
        });

        var response = await AuthedClient(factory).GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "basic-plan");
    }

    [TestMethod]
    public async Task Endpoints_WithoutToken_Return401()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var mine = await client.GetAsync("/api/my-subscriptions");
        var subscribe = await client.PostAsync("/api/subscriptions", JsonContent("{}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }

    private static StringContent JsonContent(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");
}
