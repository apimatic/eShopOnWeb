using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private readonly StubBillingService _stub = new();

    private HttpClient CreateClient(bool authenticated = true)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Swap the live Maxio adapter for the in-memory stub (last registration wins).
            builder.ConfigureTestServices(services =>
                services.AddScoped<ISubscriptionBillingService>(_ => _stub));
        });

        var client = factory.CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        }
        return client;
    }

    [TestMethod]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        _stub.Plans.Add(new SubscriptionPlan
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            FormattedPrice = "$299.00",
            Interval = "1 month",
            ProductFamilyHandle = "eshop-subscribe"
        });

        var response = await CreateClient().GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Plans.Count);
        Assert.AreEqual("eshop-pro", model.Plans[0].Handle);
        Assert.AreEqual(29900, model.Plans[0].PriceInCents);
        Assert.AreEqual("$299.00", model.Plans[0].FormattedPrice);
    }

    [TestMethod]
    public async Task Subscribe_ReturnsSubscription_AndUsesTokenIdentity()
    {
        _stub.SubscribeResult = new CustomerSubscription
        {
            Id = 123,
            State = "active",
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            FormattedPrice = "$299.00"
        };

        var response = await CreateClient().PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<CreateSubscriptionResponse>();
        Assert.IsNotNull(model?.Subscription);
        Assert.AreEqual(123, model!.Subscription!.Id);
        Assert.AreEqual("active", model.Subscription.State);
        // Identity must come from the JWT (demo user), never the request body.
        Assert.AreEqual("eshop-pro", _stub.LastPlanHandle);
        Assert.AreEqual("demouser@microsoft.com", _stub.LastSubscriber?.Reference);
    }

    [TestMethod]
    public async Task Subscribe_MissingPlanHandle_ReturnsBadRequest()
    {
        var response = await CreateClient().PostAsJsonAsync("api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsMappedSubscriptions()
    {
        _stub.Subscriptions.Add(new CustomerSubscription
        {
            Id = 555,
            State = "active",
            PlanHandle = "basic-plan",
            PlanName = "Basic Plan",
            PriceInCents = 2900,
            FormattedPrice = "$29.00"
        });

        var response = await CreateClient().GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var model = await response.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual(555, model.Subscriptions[0].Id);
        Assert.AreEqual("basic-plan", model.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public async Task Endpoints_RequireAuthentication()
    {
        var client = CreateClient(authenticated: false);

        var plans = await client.GetAsync("api/subscription-plans");
        var mine = await client.GetAsync("api/my-subscriptions");
        var subscribe = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }
}
