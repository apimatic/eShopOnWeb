using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Maxio;

namespace PublicApiIntegrationTests;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program> _application = null!;
    private static FakeMaxioClient _maxio = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _maxio = new FakeMaxioClient();
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // Replace the real Maxio HTTP client so the tests never touch the network.
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<IMaxioClient>(_maxio);
            }));
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _application?.Dispose();
    }

    private static HttpClient NewAuthenticatedClient(string userName)
    {
        var client = _application.CreateClient();
        var token = userName == "admin@microsoft.com"
            ? ApiTokenHelper.GetAdminUserToken()
            : ApiTokenHelper.GetNormalUserToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private const string NormalUser = "demouser@microsoft.com";
    private const string AdminUser = "admin@microsoft.com";

    [TestMethod]
    public async Task SubscriptionEndpointsRequireAuthentication()
    {
        var client = _application.CreateClient();

        var listPlans = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, listPlans.StatusCode);

        var mySubscriptions = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, mySubscriptions.StatusCode);

        var create = await client.PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [TestMethod]
    public async Task ListSubscriptionPlansReturnsOfferedPlans()
    {
        var client = NewAuthenticatedClient(NormalUser);

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Plans.Count);

        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);

        var basic = model.Plans.Single(p => p.Handle == "basic-plan");
        Assert.AreEqual(29.00m, basic.Price);
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentForSamePlan()
    {
        var client = NewAuthenticatedClient(NormalUser);

        var first = await client.PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel);
        Assert.IsFalse(firstModel!.WasAlreadySubscribed);
        Assert.AreEqual("eshop-pro", firstModel.Subscription.PlanHandle);
        Assert.AreEqual("active", firstModel.Subscription.State);

        // A double-click / repeated subscribe must not create a second Maxio subscription.
        var second = await client.PostAsync("api/subscriptions", Json(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(secondModel);
        Assert.IsTrue(secondModel!.WasAlreadySubscribed);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription.Id);

        Assert.AreEqual(1, _maxio.CountCustomers(NormalUser));
        Assert.AreEqual(1, _maxio.CountSubscriptions(NormalUser, "eshop-pro"));
    }

    [TestMethod]
    public async Task SubscribeToUnknownPlanReturnsNotFound()
    {
        var client = NewAuthenticatedClient(NormalUser);

        var response = await client.PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeWithoutPlanHandleReturnsBadRequest()
    {
        var client = NewAuthenticatedClient(AdminUser);

        var response = await client.PostAsync("api/subscriptions", Json(new { planHandle = string.Empty }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsReflectsSubscriptions()
    {
        var client = NewAuthenticatedClient(AdminUser);

        var created = await client.PostAsync("api/subscriptions", Json(new { planHandle = "basic-plan" }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.IsNotNull(model);

        var subscription = model!.Subscriptions.Single(s => s.PlanHandle == "basic-plan");
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(29.00m, subscription.Price);
        Assert.IsNotNull(subscription.NextAssessmentAt);
        Assert.IsNotNull(subscription.CurrentPeriodEndsAt);
    }
}
