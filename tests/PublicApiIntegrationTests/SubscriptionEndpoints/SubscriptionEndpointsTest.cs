using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private const string DemoUserName = "demouser@microsoft.com";

    [TestInitialize]
    public void TestInitialize()
    {
        if (!MaxioTestContext.IsConfigured)
        {
            Assert.Inconclusive("MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY are not set; skipping live Maxio test.");
        }
    }

    [TestMethod]
    public async Task SubscriptionPlans_ReturnsAvailablePlans()
    {
        var response = await GetAsync("api/subscription-plans", ApiTokenHelper.GetNormalUserToken());
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count > 0, "Expected at least one subscribable plan in the configured product family.");
        Assert.IsTrue(model.Plans.All(p => !string.IsNullOrWhiteSpace(p.Handle) && p.PriceInCents > 0),
            "Every plan must expose a handle and a positive price.");
    }

    [TestMethod]
    public async Task Subscribe_IsIdempotent_AndShowsUpInMySubscriptions()
    {
        var token = ApiTokenHelper.GetNormalUserToken();

        var plans = (await (await GetAsync("api/subscription-plans", token)).Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(plans);
        var targetPlan = plans!.Plans.First();

        // First subscribe creates the Maxio customer and the subscription.
        var first = await PostSubscribeAsync(token, targetPlan.Handle);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel?.Subscription);
        Assert.IsTrue(firstModel!.Created);
        var subscriptionId = firstModel.Subscription!.SubscriptionId;

        try
        {
            // A double-click must not create a second subscription.
            var second = await PostSubscribeAsync(token, targetPlan.Handle);
            Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
            var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
            Assert.IsNotNull(secondModel?.Subscription);
            Assert.IsFalse(secondModel!.Created, "Re-subscribing to the same plan must return the existing subscription.");
            Assert.AreEqual(subscriptionId, secondModel.Subscription!.SubscriptionId,
                "Re-subscribing must return the same Maxio subscription.");

            // The subscription is visible on the account.
            var mine = await GetAsync("api/my-subscriptions", token);
            mine.EnsureSuccessStatusCode();
            var myModel = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
            Assert.IsNotNull(myModel);
            Assert.IsTrue(myModel!.Subscriptions.Any(s => s.SubscriptionId == subscriptionId),
                "The created subscription must appear in my-subscriptions.");
        }
        finally
        {
            await MaxioTestContext.CancelSubscriptionAsync(subscriptionId);
            await MaxioTestContext.DeleteCustomerByReferenceAsync(await GetDemoUserReferenceAsync());
        }
    }

    [TestMethod]
    public async Task Subscribe_WithUnknownPlan_ReturnsNotFound()
    {
        var response = await PostSubscribeAsync(ApiTokenHelper.GetNormalUserToken(), "plan-handle-that-does-not-exist");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostSubscribeAsync(string token, string planHandle)
    {
        var request = new CreateSubscriptionRequest { PlanHandle = planHandle };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        return PostAsync("api/subscriptions", content, token);
    }

    private static async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await ProgramTest.NewClient.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await ProgramTest.NewClient.SendAsync(request);
    }

    private static async Task<string> GetDemoUserReferenceAsync()
    {
        using var scope = ProgramTest.Application.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(DemoUserName);
        Assert.IsNotNull(user, "The demo user must be seeded by the test host.");
        return user!.Id;
    }
}
