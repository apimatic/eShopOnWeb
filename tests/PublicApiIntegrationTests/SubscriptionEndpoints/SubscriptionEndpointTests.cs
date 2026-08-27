using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireBearerAuthentication()
    {
        await using var app = CreateApplication(new StubBillingService());
        using var client = app.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-subscriptions")).StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/subscriptions", new { productHandle = "eshop-pro" })).StatusCode);
    }

    [TestMethod]
    public async Task SubscribeUsesTheAuthenticatedEshopUser()
    {
        var billing = new StubBillingService();
        await using var app = CreateApplication(billing);
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync(
            "/api/subscriptions",
            new { productHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsNotNull(billing.LastUser);
        Assert.AreEqual("demouser@microsoft.com", billing.LastUser.Email);
        Assert.AreNotEqual("demouser@microsoft.com", billing.LastUser.UserId);
        Assert.AreEqual("eshop-pro", billing.LastProductHandle);
    }

    [TestMethod]
    public async Task PlanListReturnsBillingPlans()
    {
        await using var app = CreateApplication(new StubBillingService());
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());

        var plans = await client.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/subscription-plans");

        Assert.IsNotNull(plans);
        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].ProductHandle);
    }

    private static WebApplicationFactory<Program> CreateApplication(StubBillingService billing) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionBillingService>();
                services.AddSingleton<ISubscriptionBillingService>(billing);
            }));

    private sealed class StubBillingService : ISubscriptionBillingService
    {
        public BillingUserIdentity? LastUser { get; private set; }
        public string? LastProductHandle { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>([
                new("eshop-pro", "Pro", "Pro plan", 29900, 1, "month")
            ]);

        public Task<CreateSubscriptionResult> SubscribeAsync(
            BillingUserIdentity user,
            string productHandle,
            CancellationToken cancellationToken)
        {
            LastUser = user;
            LastProductHandle = productHandle;
            return Task.FromResult(new CreateSubscriptionResult(
                new SubscriptionDto(123, productHandle, "Pro", 29900, "active", null),
                Created: true));
        }

        public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
            BillingUserIdentity user,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>([]);
    }
}
