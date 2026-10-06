using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> _application = null!;
    private static FakeMaxioSubscriptionService _fake = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _fake = new FakeMaxioSubscriptionService();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioSubscriptionService>();
                services.AddSingleton<IMaxioSubscriptionService>(_fake);
            });
        });
    }

    private static HttpClient NewClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task PlansEndpointRequiresAuthentication()
    {
        var client = _application.CreateClient();
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlansEndpointReturnsAvailablePlans()
    {
        var response = await NewClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PlansResponseContract>();
        Assert.IsNotNull(body);
        Assert.AreEqual(2, body!.Plans.Count);
        var pro = body.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeCreatesSubscriptionAndReturnsCreated()
    {
        var response = await NewClient().PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SubscriptionResponseContract>();
        Assert.IsNotNull(body?.Subscription);
        Assert.AreEqual("eshop-pro", body!.Subscription!.PlanHandle);
        Assert.AreEqual(29900, body.Subscription.PriceInCents);
        Assert.AreEqual("active", body.Subscription.State);
        Assert.IsNotNull(body.Subscription.NextBillingAtUtc);
        Assert.IsFalse(body.Subscription.AlreadySubscribed);
        Assert.AreEqual("demouser@microsoft.com", _fake.LastUsername);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentForRepeatedRequests()
    {
        var client = NewClient();
        var first = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<SubscriptionResponseContract>();

        var second = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "basic-plan" });
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<SubscriptionResponseContract>();
        Assert.IsNotNull(body?.Subscription);
        Assert.IsNotNull(firstBody?.Subscription);
        Assert.AreEqual(firstBody.Subscription.SubscriptionId, body!.Subscription!.SubscriptionId);
        Assert.IsTrue(body.Subscription.AlreadySubscribed);
    }

    [TestMethod]
    public async Task SubscribeWithUnknownPlanReturns404()
    {
        var response = await NewClient().PostAsJsonAsync("api/subscriptions", new { planHandle = "no-such-plan" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("Unknown subscription plan"), content);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleReturns400()
    {
        var response = await NewClient().PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsTheUsersSubscriptions()
    {
        await NewClient().PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        var response = await NewClient().GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MySubscriptionsResponseContract>();
        Assert.IsNotNull(body);
        Assert.IsTrue(body!.Subscriptions.Any(s => s.PlanHandle == "eshop-pro"));
    }

    private record PlansResponseContract(List<PlanContract> Plans);
    private record PlanContract(string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);
    private record SubscriptionResponseContract(SubscriptionContract? Subscription);
    private record SubscriptionContract(int SubscriptionId, string PlanHandle, string PlanName,
        long PriceInCents, string Currency, string State, string? NextBillingAtUtc, bool AlreadySubscribed);
    private record MySubscriptionsResponseContract(List<SubscriptionContract> Subscriptions);
}